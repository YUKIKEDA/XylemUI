// The wgpu-native calls of the Linux probe (spike/LinuxProbe/README.md), one exported function per step.
//
// Written in C, not as C# LibraryImport declarations, because the structs of webgpu.h (string views, chained structs, callback
// info structs passed by value, 64-bit flag types) cannot be checked without a compiler, and this machine compiles nothing for
// Linux. gcc checks every field name and type here against the header of the pinned release on the runner; the C# side passes
// only integers, doubles, and pointers to its own buffers. The shim is compiled into the same static archive as wgpu-native
// (ci/run-probe.sh), so the NativeAOT executable links both statically.
//
// Every function returns 0 on success and a negative number on failure; wp_last_error() then holds the reason.
// Asynchronous calls use WGPUCallbackMode_AllowProcessEvents and are waited for by polling, with a timeout, so a hung driver
// ends in an error instead of a hung job.

#define _GNU_SOURCE
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#include "webgpu/wgpu.h"

#define WP_TIMEOUT_NS (20ull * 1000ull * 1000ull * 1000ull)

static WGPUInstance g_instance;
static WGPUAdapter g_adapter;
static WGPUDevice g_device;
static WGPUQueue g_queue;
static WGPUTexture g_texture;
static WGPUTextureView g_view;
static WGPUBuffer g_buffer;
static uint32_t g_width;
static uint32_t g_height;
static uint32_t g_stride;

static char g_error[4096];
static int g_uncaptured_errors;
static int g_device_lost;

static void set_error(const char *format, ...)
{
    va_list args;
    va_start(args, format);
    vsnprintf(g_error, sizeof g_error, format, args);
    va_end(args);
}

static uint64_t now_ns(void)
{
    struct timespec t;
    clock_gettime(CLOCK_MONOTONIC, &t);
    return (uint64_t)t.tv_sec * 1000000000ull + (uint64_t)t.tv_nsec;
}

static void sleep_short(void)
{
    struct timespec t = {0, 200 * 1000};
    nanosleep(&t, NULL);
}

// A string view as a length and a pointer for printf's "%.*s": {NULL, *} is empty, {p, WGPU_STRLEN} is null-terminated.
static int view_length(WGPUStringView view)
{
    if (view.data == NULL)
    {
        return 0;
    }

    size_t length = view.length == WGPU_STRLEN ? strlen(view.data) : view.length;
    return length > 1024 ? 1024 : (int)length;
}

static const char *view_data(WGPUStringView view)
{
    return view.data == NULL ? "" : view.data;
}

// Appends to a caller's buffer of `capacity` bytes; the text stays null-terminated and is cut at the end of the buffer.
static void append(char *out, int capacity, const char *format, ...)
{
    if (out == NULL || capacity <= 0)
    {
        return;
    }

    size_t used = strnlen(out, (size_t)capacity);
    if (used + 1 >= (size_t)capacity)
    {
        return;
    }

    va_list args;
    va_start(args, format);
    vsnprintf(out + used, (size_t)capacity - used, format, args);
    va_end(args);
}

static const char *backend_name(WGPUBackendType backend)
{
    switch (backend)
    {
        case WGPUBackendType_Undefined: return "Undefined";
        case WGPUBackendType_Null: return "Null";
        case WGPUBackendType_WebGPU: return "WebGPU";
        case WGPUBackendType_D3D11: return "D3D11";
        case WGPUBackendType_D3D12: return "D3D12";
        case WGPUBackendType_Metal: return "Metal";
        case WGPUBackendType_Vulkan: return "Vulkan";
        case WGPUBackendType_OpenGL: return "OpenGL";
        case WGPUBackendType_OpenGLES: return "OpenGLES";
        default: return "Other";
    }
}

static const char *adapter_type_name(WGPUAdapterType type)
{
    switch (type)
    {
        case WGPUAdapterType_DiscreteGPU: return "DiscreteGPU";
        case WGPUAdapterType_IntegratedGPU: return "IntegratedGPU";
        case WGPUAdapterType_CPU: return "CPU";
        case WGPUAdapterType_Unknown: return "Unknown";
        default: return "Other";
    }
}

static void on_log(WGPULogLevel level, WGPUStringView message, void *userdata)
{
    (void)userdata;
    fprintf(stderr, "[wgpu log %d] %.*s\n", (int)level, view_length(message), view_data(message));
    fflush(stderr);
}

static void on_uncaptured_error(WGPUDevice const *device, WGPUErrorType type, WGPUStringView message, void *userdata1,
    void *userdata2)
{
    (void)device;
    (void)userdata1;
    (void)userdata2;
    g_uncaptured_errors++;
    set_error("uncaptured error type %d: %.*s", (int)type, view_length(message), view_data(message));
    fprintf(stderr, "[wgpu uncaptured error %d] %.*s\n", (int)type, view_length(message), view_data(message));
    fflush(stderr);
}

static void on_device_lost(WGPUDevice const *device, WGPUDeviceLostReason reason, WGPUStringView message, void *userdata1,
    void *userdata2)
{
    (void)device;
    (void)userdata1;
    (void)userdata2;
    g_device_lost = (int)reason;
    fprintf(stderr, "[wgpu device lost reason %d] %.*s\n", (int)reason, view_length(message), view_data(message));
    fflush(stderr);
}

const char *wp_last_error(void)
{
    return g_error;
}

int wp_uncaptured_error_count(void)
{
    return g_uncaptured_errors;
}

// `log_level` is a WGPULogLevel (0 off .. 5 trace); messages go to stderr, which the job's log keeps.
void wp_set_log_level(int log_level)
{
    wgpuSetLogCallback(on_log, NULL);
    wgpuSetLogLevel((WGPULogLevel)log_level);
}

// `backends` is a WGPUInstanceBackend mask: 0 all, 1 Vulkan, 2 GL.
int wp_instance_create(uint32_t backends)
{
    WGPUInstanceExtras extras;
    memset(&extras, 0, sizeof extras);
    extras.chain.next = NULL;
    extras.chain.sType = (WGPUSType)WGPUSType_InstanceExtras;
    extras.backends = backends;
    extras.flags = WGPUInstanceFlag_Default;
    extras.dxcPath = (WGPUStringView){NULL, 0};

    WGPUInstanceDescriptor descriptor = WGPU_INSTANCE_DESCRIPTOR_INIT;
    descriptor.nextInChain = &extras.chain;

    g_instance = wgpuCreateInstance(&descriptor);
    if (g_instance == NULL)
    {
        set_error("wgpuCreateInstance returned NULL");
        return -1;
    }

    return 0;
}

static void describe_adapter(WGPUAdapter adapter, char *out, int capacity)
{
    WGPUAdapterInfo info = WGPU_ADAPTER_INFO_INIT;
    WGPUStatus status = wgpuAdapterGetInfo(adapter, &info);
    if (status != WGPUStatus_Success)
    {
        append(out, capacity, "wgpuAdapterGetInfo failed (status %d)", (int)status);
        return;
    }

    append(out, capacity,
        "backend=%s type=%s vendor='%.*s' architecture='%.*s' device='%.*s' description='%.*s' vendorID=0x%04x deviceID=0x%04x "
        "dualSourceBlending=%s",
        backend_name(info.backendType), adapter_type_name(info.adapterType), view_length(info.vendor), view_data(info.vendor),
        view_length(info.architecture), view_data(info.architecture), view_length(info.device), view_data(info.device),
        view_length(info.description), view_data(info.description), info.vendorID, info.deviceID,
        wgpuAdapterHasFeature(adapter, WGPUFeatureName_DualSourceBlending) ? "yes" : "no");
    wgpuAdapterInfoFreeMembers(info);
}

// Writes one line per adapter of the `backends` mask into `out`; returns the number of adapters.
int wp_enumerate_adapters(uint32_t backends, char *out, int capacity)
{
    if (out != NULL && capacity > 0)
    {
        out[0] = '\0';
    }

    WGPUInstanceEnumerateAdapterOptions options;
    memset(&options, 0, sizeof options);
    options.backends = backends;

    size_t count = wgpuInstanceEnumerateAdapters(g_instance, &options, NULL);
    if (count == 0)
    {
        return 0;
    }

    WGPUAdapter *adapters = calloc(count, sizeof(WGPUAdapter));
    if (adapters == NULL)
    {
        set_error("calloc failed for %zu adapters", count);
        return -1;
    }

    // The second call writes the same number of adapters; the array has room for `count`.
    count = wgpuInstanceEnumerateAdapters(g_instance, &options, adapters);
    for (size_t i = 0; i < count; i++)
    {
        append(out, capacity, "[%zu] ", i);
        describe_adapter(adapters[i], out, capacity);
        append(out, capacity, "\n");
        wgpuAdapterRelease(adapters[i]);
    }

    free(adapters);
    return (int)count;
}

typedef struct AdapterRequest
{
    int done;
    WGPURequestAdapterStatus status;
    WGPUAdapter adapter;
    char message[1024];
} AdapterRequest;

static void on_adapter(WGPURequestAdapterStatus status, WGPUAdapter adapter, WGPUStringView message, void *userdata1,
    void *userdata2)
{
    (void)userdata2;
    AdapterRequest *request = userdata1;
    request->status = status;
    request->adapter = adapter;
    snprintf(request->message, sizeof request->message, "%.*s", view_length(message), view_data(message));
    request->done = 1;
}

static int wait_for(volatile int *done, const char *what)
{
    uint64_t start = now_ns();
    while (!*done)
    {
        wgpuInstanceProcessEvents(g_instance);
        if (*done)
        {
            break;
        }

        if (g_device != NULL)
        {
            wgpuDevicePoll(g_device, 0, NULL);
        }

        if (now_ns() - start > WP_TIMEOUT_NS)
        {
            set_error("timed out after 20 s waiting for %s", what);
            return -1;
        }

        sleep_short();
    }

    return 0;
}

// `force_fallback` sets forceFallbackAdapter; `backend_type` is a WGPUBackendType (0 undefined, 6 Vulkan, 8 OpenGLES).
// The adapter replaces the one held before; its description goes to `out`.
int wp_request_adapter(int force_fallback, int backend_type, char *out, int capacity)
{
    if (out != NULL && capacity > 0)
    {
        out[0] = '\0';
    }

    if (g_adapter != NULL)
    {
        wgpuAdapterRelease(g_adapter);
        g_adapter = NULL;
    }

    WGPURequestAdapterOptions options = WGPU_REQUEST_ADAPTER_OPTIONS_INIT;
    options.featureLevel = WGPUFeatureLevel_Core;
    options.forceFallbackAdapter = force_fallback ? 1u : 0u;
    options.backendType = (WGPUBackendType)backend_type;

    AdapterRequest request;
    memset(&request, 0, sizeof request);

    WGPURequestAdapterCallbackInfo callback = WGPU_REQUEST_ADAPTER_CALLBACK_INFO_INIT;
    callback.mode = WGPUCallbackMode_AllowProcessEvents;
    callback.callback = on_adapter;
    callback.userdata1 = &request;

    wgpuInstanceRequestAdapter(g_instance, &options, callback);
    if (wait_for(&request.done, "the adapter") != 0)
    {
        return -1;
    }

    if (request.status != WGPURequestAdapterStatus_Success || request.adapter == NULL)
    {
        set_error("request adapter status %d: %s", (int)request.status, request.message);
        return -2;
    }

    g_adapter = request.adapter;
    describe_adapter(g_adapter, out, capacity);
    return 0;
}

typedef struct DeviceRequest
{
    int done;
    WGPURequestDeviceStatus status;
    WGPUDevice device;
    char message[1024];
} DeviceRequest;

static void on_device(WGPURequestDeviceStatus status, WGPUDevice device, WGPUStringView message, void *userdata1,
    void *userdata2)
{
    (void)userdata2;
    DeviceRequest *request = userdata1;
    request->status = status;
    request->device = device;
    snprintf(request->message, sizeof request->message, "%.*s", view_length(message), view_data(message));
    request->done = 1;
}

int wp_request_device(void)
{
    if (g_adapter == NULL)
    {
        set_error("no adapter");
        return -1;
    }

    WGPUDeviceDescriptor descriptor = WGPU_DEVICE_DESCRIPTOR_INIT;
    descriptor.deviceLostCallbackInfo.mode = WGPUCallbackMode_AllowSpontaneous;
    descriptor.deviceLostCallbackInfo.callback = on_device_lost;
    descriptor.uncapturedErrorCallbackInfo.callback = on_uncaptured_error;

    DeviceRequest request;
    memset(&request, 0, sizeof request);

    WGPURequestDeviceCallbackInfo callback = WGPU_REQUEST_DEVICE_CALLBACK_INFO_INIT;
    callback.mode = WGPUCallbackMode_AllowProcessEvents;
    callback.callback = on_device;
    callback.userdata1 = &request;

    wgpuAdapterRequestDevice(g_adapter, &descriptor, callback);
    if (wait_for(&request.done, "the device") != 0)
    {
        return -1;
    }

    if (request.status != WGPURequestDeviceStatus_Success || request.device == NULL)
    {
        set_error("request device status %d: %s", (int)request.status, request.message);
        return -2;
    }

    g_device = request.device;
    g_queue = wgpuDeviceGetQueue(g_device);
    return 0;
}

// An RGBA8 texture to clear and a buffer to read it back into; a row of the copy is a multiple of 256 bytes (WebGPU's
// bytesPerRow alignment).
int wp_create_targets(uint32_t width, uint32_t height)
{
    if (g_device == NULL)
    {
        set_error("no device");
        return -1;
    }

    int errors_before = g_uncaptured_errors;
    g_width = width;
    g_height = height;
    g_stride = (width * 4u + 255u) & ~255u;

    WGPUTextureDescriptor texture = WGPU_TEXTURE_DESCRIPTOR_INIT;
    texture.usage = WGPUTextureUsage_RenderAttachment | WGPUTextureUsage_CopySrc;
    texture.dimension = WGPUTextureDimension_2D;
    texture.size.width = width;
    texture.size.height = height;
    texture.size.depthOrArrayLayers = 1;
    texture.format = WGPUTextureFormat_RGBA8Unorm;
    texture.mipLevelCount = 1;
    texture.sampleCount = 1;
    g_texture = wgpuDeviceCreateTexture(g_device, &texture);
    if (g_texture == NULL)
    {
        set_error("wgpuDeviceCreateTexture returned NULL");
        return -1;
    }

    g_view = wgpuTextureCreateView(g_texture, NULL);
    if (g_view == NULL)
    {
        set_error("wgpuTextureCreateView returned NULL");
        return -1;
    }

    WGPUBufferDescriptor buffer = WGPU_BUFFER_DESCRIPTOR_INIT;
    buffer.usage = WGPUBufferUsage_MapRead | WGPUBufferUsage_CopyDst;
    buffer.size = (uint64_t)g_stride * height;
    g_buffer = wgpuDeviceCreateBuffer(g_device, &buffer);
    if (g_buffer == NULL)
    {
        set_error("wgpuDeviceCreateBuffer returned NULL");
        return -1;
    }

    return g_uncaptured_errors == errors_before ? 0 : -3;
}

typedef struct MapRequest
{
    int done;
    WGPUMapAsyncStatus status;
    char message[1024];
} MapRequest;

static void on_map(WGPUMapAsyncStatus status, WGPUStringView message, void *userdata1, void *userdata2)
{
    (void)userdata2;
    MapRequest *request = userdata1;
    request->status = status;
    snprintf(request->message, sizeof request->message, "%.*s", view_length(message), view_data(message));
    request->done = 1;
}

static uint8_t to_unorm8(double value)
{
    double clamped = value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
    return (uint8_t)(clamped * 255.0 + 0.5);
}

// One frame: clear the texture to (r, g, b, a) in a render pass, copy it to the buffer, submit, map, and read back.
// `pixel` receives the 4 bytes at (x, y); `mismatches` the number of pixels whose channels differ from the expected unorm8 value
// by more than 1; `encode_ns` the time to record and submit, `wait_ns` the time from submit until the map completed.
int wp_frame(double r, double g, double b, double a, uint32_t x, uint32_t y, uint8_t *pixel, uint32_t *mismatches,
    uint64_t *encode_ns, uint64_t *wait_ns)
{
    if (g_buffer == NULL || g_view == NULL)
    {
        set_error("no targets");
        return -1;
    }

    if (x >= g_width || y >= g_height)
    {
        set_error("pixel (%u, %u) is outside %ux%u", x, y, g_width, g_height);
        return -1;
    }

    int errors_before = g_uncaptured_errors;
    uint64_t start = now_ns();

    WGPUCommandEncoder encoder = wgpuDeviceCreateCommandEncoder(g_device, NULL);
    if (encoder == NULL)
    {
        set_error("wgpuDeviceCreateCommandEncoder returned NULL");
        return -1;
    }

    WGPURenderPassColorAttachment color = WGPU_RENDER_PASS_COLOR_ATTACHMENT_INIT;
    color.view = g_view;
    color.loadOp = WGPULoadOp_Clear;
    color.storeOp = WGPUStoreOp_Store;
    color.clearValue = (WGPUColor){r, g, b, a};

    WGPURenderPassDescriptor pass_descriptor = WGPU_RENDER_PASS_DESCRIPTOR_INIT;
    pass_descriptor.colorAttachmentCount = 1;
    pass_descriptor.colorAttachments = &color;

    WGPURenderPassEncoder pass = wgpuCommandEncoderBeginRenderPass(encoder, &pass_descriptor);
    wgpuRenderPassEncoderEnd(pass);
    wgpuRenderPassEncoderRelease(pass);

    WGPUTexelCopyTextureInfo source = WGPU_TEXEL_COPY_TEXTURE_INFO_INIT;
    source.texture = g_texture;
    source.mipLevel = 0;
    source.aspect = WGPUTextureAspect_All;

    WGPUTexelCopyBufferInfo destination = WGPU_TEXEL_COPY_BUFFER_INFO_INIT;
    destination.buffer = g_buffer;
    destination.layout.offset = 0;
    destination.layout.bytesPerRow = g_stride;
    destination.layout.rowsPerImage = g_height;

    WGPUExtent3D size = WGPU_EXTENT_3D_INIT;
    size.width = g_width;
    size.height = g_height;
    size.depthOrArrayLayers = 1;
    wgpuCommandEncoderCopyTextureToBuffer(encoder, &source, &destination, &size);

    WGPUCommandBuffer commands = wgpuCommandEncoderFinish(encoder, NULL);
    wgpuCommandEncoderRelease(encoder);
    if (commands == NULL)
    {
        set_error("wgpuCommandEncoderFinish returned NULL");
        return -1;
    }

    wgpuQueueSubmit(g_queue, 1, &commands);
    wgpuCommandBufferRelease(commands);
    uint64_t submitted = now_ns();
    *encode_ns = submitted - start;

    size_t byte_count = (size_t)g_stride * g_height;
    MapRequest request;
    memset(&request, 0, sizeof request);
    WGPUBufferMapCallbackInfo callback = WGPU_BUFFER_MAP_CALLBACK_INFO_INIT;
    callback.mode = WGPUCallbackMode_AllowProcessEvents;
    callback.callback = on_map;
    callback.userdata1 = &request;
    wgpuBufferMapAsync(g_buffer, WGPUMapMode_Read, 0, byte_count, callback);

    // Block in the poll first, then let the instance deliver the callback; wait_for polls again if it did not come.
    wgpuDevicePoll(g_device, 1, NULL);
    if (wait_for(&request.done, "the buffer map") != 0)
    {
        return -1;
    }

    *wait_ns = now_ns() - submitted;
    if (request.status != WGPUMapAsyncStatus_Success)
    {
        set_error("map status %d: %s", (int)request.status, request.message);
        return -2;
    }

    const uint8_t *data = wgpuBufferGetConstMappedRange(g_buffer, 0, byte_count);
    if (data == NULL)
    {
        set_error("wgpuBufferGetConstMappedRange returned NULL");
        wgpuBufferUnmap(g_buffer);
        return -1;
    }

    // Indices stay below g_stride * g_height = byte_count: x < g_width, y < g_height, g_width * 4 <= g_stride.
    const uint8_t expected[4] = {to_unorm8(r), to_unorm8(g), to_unorm8(b), to_unorm8(a)};
    uint32_t wrong = 0;
    for (uint32_t row = 0; row < g_height; row++)
    {
        const uint8_t *line = data + (size_t)row * g_stride;
        for (uint32_t column = 0; column < g_width; column++)
        {
            const uint8_t *p = line + (size_t)column * 4u;
            for (int channel = 0; channel < 4; channel++)
            {
                int difference = (int)p[channel] - (int)expected[channel];
                if (difference > 1 || difference < -1)
                {
                    wrong++;
                    break;
                }
            }
        }
    }

    memcpy(pixel, data + (size_t)y * g_stride + (size_t)x * 4u, 4);
    *mismatches = wrong;
    wgpuBufferUnmap(g_buffer);
    return g_uncaptured_errors == errors_before ? 0 : -3;
}

void wp_release_all(void)
{
    if (g_buffer != NULL)
    {
        wgpuBufferRelease(g_buffer);
        g_buffer = NULL;
    }

    if (g_view != NULL)
    {
        wgpuTextureViewRelease(g_view);
        g_view = NULL;
    }

    if (g_texture != NULL)
    {
        wgpuTextureRelease(g_texture);
        g_texture = NULL;
    }

    if (g_queue != NULL)
    {
        wgpuQueueRelease(g_queue);
        g_queue = NULL;
    }

    if (g_device != NULL)
    {
        wgpuDeviceRelease(g_device);
        g_device = NULL;
    }

    if (g_adapter != NULL)
    {
        wgpuAdapterRelease(g_adapter);
        g_adapter = NULL;
    }

    if (g_instance != NULL)
    {
        wgpuInstanceRelease(g_instance);
        g_instance = NULL;
    }
}

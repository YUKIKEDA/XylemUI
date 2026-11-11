// The image decoders of XylemUI.Imaging behind one C interface (design §5.0.4, "Where they live and how they are called", row IM1):
// Wuffs for PNG, GIF, and BMP, libjpeg-turbo for JPEG, and libwebp for WebP. Wuffs's C API is mostly static inline functions and
// its format-sniffing layer is C++, so it cannot be called through P/Invoke (design §0, "Native interop"); libjpeg-turbo and
// libwebp sit behind the same two functions so that C# calls the three one way.
//
//   xi_info    reads the format and the size of an image, and whether it is an animation, without decoding its pixels
//   xi_decode  decodes the image (the first frame of a GIF) to premultiplied BGRA, top-down rows of `stride` bytes; a JPEG at a
//              scale of eighths, libjpeg-turbo's DCT scaling (design §5.0.4, "Decoding size", row IM1 1b-2); and the colors of a
//              color profile C# read converted to sRGB with skcms (design §5.0.4, "EXIF orientation and color", IM1's Grill,
//              Q23 to Q29)
//
//   xi_animation_open, xi_animation_next, xi_animation_close
//              open the decoder of an animated GIF or WebP, compose its next frame, and close it (design §5.0.4,
//              "Animations")
//
// Every function takes untrusted bytes and returns an XI_ status; nothing is read past `length`. vendor/build.ps1 builds this file
// with Wuffs into xylemui_image.lib (NativeAOT, static CRT) and xylemui_image.dll (the JIT).
#include <setjmp.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define WUFFS_IMPLEMENTATION
#define WUFFS_CONFIG__STATIC_FUNCTIONS
#define WUFFS_CONFIG__MODULES
#define WUFFS_CONFIG__MODULE__BASE
#define WUFFS_CONFIG__MODULE__ADLER32
#define WUFFS_CONFIG__MODULE__BMP
#define WUFFS_CONFIG__MODULE__CRC32
#define WUFFS_CONFIG__MODULE__DEFLATE
#define WUFFS_CONFIG__MODULE__GIF
#define WUFFS_CONFIG__MODULE__LZW
#define WUFFS_CONFIG__MODULE__PNG
#define WUFFS_CONFIG__MODULE__ZLIB
// MSVC compiles Wuffs's SIMD paths only when asked; the CPU is still checked at run time (spike S11, Issue #730).
#define WUFFS_CONFIG__ENABLE_MSVC_CPU_ARCH__X86_64_V3
#include "wuffs-v0.4.c"

#include <jpeglib.h>
#include <webp/decode.h>
#include <webp/demux.h>

#include "skcms/skcms.h"

#if defined(_WIN32)
#define XI_EXPORT __declspec(dllexport)
#else
#define XI_EXPORT __attribute__((visibility("default")))
#endif

#define XI_OK 0
#define XI_UNSUPPORTED 1
#define XI_BROKEN 2
#define XI_OUT_OF_MEMORY 3
#define XI_END 4

#define XI_PNG 1
#define XI_JPEG 2
#define XI_WEBP 3
#define XI_GIF 4
#define XI_BMP 5

// The color information C# read from the image (design §5.0.4, IM1's Grill, Q23 and Q24): an ICC profile's bytes, with room past
// its end that skcms's parse reads ahead into; or, for a PNG with no iCCP and no sRGB chunk, its cHRM (white, red, green, and
// blue x and y) and its gAMA (the encoding gamma, gAMA's value / 100,000).
typedef struct xi_animation xi_animation;

typedef struct xi_color {
  const uint8_t* icc;
  size_t icc_length;
  int32_t has_chromaticities;
  float chromaticities[8];
  int32_t has_gamma;
  float gamma;
} xi_color;

// ---- skcms: color profiles to sRGB ----

// The profile the colors are converted from, or NULL for none: an ICC profile skcms parses, or one made from a PNG's cHRM and gAMA
// as Skia makes it (SkPngCodec.cpp's read_color_profile; the sRGB primaries or curve where one is missing). A profile skcms does
// not parse, or one close to sRGB (skcms_ApproximatelyEqualProfiles), converts nothing (Q26 and Q29); a profile of the wrong kind
// for the image is dropped by the decoder that knows the image's kind.
static const skcms_ICCProfile* xi_profile(const xi_color* color, skcms_ICCProfile* storage) {
  if (!color) {
    return NULL;
  }

  if (color->icc && color->icc_length > 0) {
    if (!skcms_Parse(color->icc, color->icc_length, storage)) {
      return NULL;
    }
  } else if (color->has_chromaticities || color->has_gamma) {
    skcms_Matrix3x3 to_xyz = skcms_sRGB_profile()->toXYZD50;
    if (color->has_chromaticities) {
      const float* c = color->chromaticities;
      skcms_Matrix3x3 primaries;
      if (skcms_PrimariesToXYZD50(c[2], c[3], c[4], c[5], c[6], c[7], c[0], c[1], &primaries)) {
        to_xyz = primaries;
      }
    }

    skcms_TransferFunction curve = *skcms_sRGB_TransferFunction();
    if (color->has_gamma && color->gamma > 0) {
      skcms_TransferFunction power = {1.0f / color->gamma, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f};
      curve = power;
    }

    skcms_Init(storage);
    skcms_SetTransferFunction(storage, &curve);
    skcms_SetXYZD50(storage, &to_xyz);
  } else {
    return NULL;
  }

  return skcms_ApproximatelyEqualProfiles(storage, skcms_sRGB_profile()) ? NULL : storage;
}

// Converts rows of `width` pixels of `format` in place to sRGB BGRA, 8 bits a channel, clamped (Q27): premultiplied from
// unpremultiplied colors for an image with alpha (Skia converts unpremultiplied colors, then premultiplies, SkCodec.cpp's
// applyColorXform), opaque for one without.
static int32_t xi_to_srgb(uint8_t* pixels, size_t stride, int32_t width, int32_t height, skcms_PixelFormat format, skcms_AlphaFormat from,
                          skcms_AlphaFormat to, const skcms_ICCProfile* profile) {
  for (int32_t y = 0; y < height; y++) {
    uint8_t* row = pixels + (size_t)y * stride;
    if (!skcms_Transform(row, format, from, profile, row, skcms_PixelFormat_BGRA_8888, to, skcms_sRGB_profile(), (size_t)width)) {
      return XI_BROKEN;
    }
  }

  return XI_OK;
}

// ---- Wuffs: PNG, GIF, BMP ----

static wuffs_base__image_decoder* xi_wuffs_decoder(int32_t format) {
  switch (format) {
    case XI_PNG:
      return wuffs_png__decoder__alloc_as__wuffs_base__image_decoder();
    case XI_GIF:
      return wuffs_gif__decoder__alloc_as__wuffs_base__image_decoder();
    case XI_BMP:
      return wuffs_bmp__decoder__alloc_as__wuffs_base__image_decoder();
    default:
      return NULL;
  }
}

// Reads the size, and decodes when `pixels` is not null. A PNG's checksums are checked: Wuffs's C API checks them unless the
// quirk to ignore them is set, which its C++ layer sets (IM1's Grill, Q4).
static int32_t xi_wuffs(int32_t format, const uint8_t* data, size_t length, int32_t* width, int32_t* height, uint8_t* pixels,
                        size_t stride, const skcms_ICCProfile* profile) {
  wuffs_base__image_decoder* decoder = xi_wuffs_decoder(format);
  if (!decoder) {
    return XI_OUT_OF_MEMORY;
  }

  int32_t result = XI_OK;
  uint8_t* work = NULL;
  wuffs_base__io_buffer source = wuffs_base__ptr_u8__reader((uint8_t*)data, length, true);
  wuffs_base__image_config config = {0};
  wuffs_base__status status = wuffs_base__image_decoder__decode_image_config(decoder, &config, &source);
  if (status.repr) {
    result = XI_BROKEN;
    goto done;
  }

  uint32_t w = wuffs_base__pixel_config__width(&config.pixcfg);
  uint32_t h = wuffs_base__pixel_config__height(&config.pixcfg);
  if (w == 0 || h == 0 || w > INT32_MAX / 4 || h > INT32_MAX) {
    result = XI_BROKEN;
    goto done;
  }

  *width = (int32_t)w;
  *height = (int32_t)h;
  if (!pixels) {
    goto done;
  }

  // Only a PNG has a profile (GIF and BMP have none, as Skia reads none); a CMYK profile, or a gray one on an image in color, is
  // dropped (SkPngCodecBase.cpp's isCompatibleColorProfileAndType). The colors are decoded unpremultiplied to be converted.
  if (profile) {
    uint32_t image_format = wuffs_base__pixel_config__pixel_format(&config.pixcfg).repr;
    int gray = (image_format & 0xF0000000u) == 0x20000000u;
    if (format != XI_PNG || profile->data_color_space == skcms_Signature_CMYK || (profile->data_color_space == skcms_Signature_Gray && !gray)) {
      profile = NULL;
    }
  }

  wuffs_base__pixel_config__set(&config.pixcfg, profile ? WUFFS_BASE__PIXEL_FORMAT__BGRA_NONPREMUL : WUFFS_BASE__PIXEL_FORMAT__BGRA_PREMUL,
                                WUFFS_BASE__PIXEL_SUBSAMPLING__NONE, w, h);
  for (uint32_t y = 0; y < h; y++) {
    memset(pixels + (size_t)y * stride, 0, (size_t)w * 4);
  }

  wuffs_base__pixel_buffer buffer;
  status = wuffs_base__pixel_buffer__set_interleaved(&buffer, &config.pixcfg, wuffs_base__make_table_u8(pixels, (size_t)w * 4, h, stride),
                                                     wuffs_base__empty_slice_u8());
  if (status.repr) {
    result = XI_BROKEN;
    goto done;
  }

  uint64_t work_length = wuffs_base__image_decoder__workbuf_len(decoder).max_incl;
  if (work_length > SIZE_MAX) {
    result = XI_OUT_OF_MEMORY;
    goto done;
  }

  work = (uint8_t*)malloc(work_length ? (size_t)work_length : 1);
  if (!work) {
    result = XI_OUT_OF_MEMORY;
    goto done;
  }

  status = wuffs_base__image_decoder__decode_frame(decoder, &buffer, &source, WUFFS_BASE__PIXEL_BLEND__SRC,
                                                   wuffs_base__make_slice_u8(work, (size_t)work_length), NULL);
  if (status.repr) {
    result = XI_BROKEN;
  } else if (profile) {
    result = xi_to_srgb(pixels, stride, (int32_t)w, (int32_t)h, skcms_PixelFormat_BGRA_8888, skcms_AlphaFormat_Unpremul,
                        skcms_AlphaFormat_PremulAsEncoded, profile);
  }

done:
  free(work);
  free(decoder);
  return result;
}

// ---- libjpeg-turbo: JPEG ----

typedef struct {
  struct jpeg_error_mgr manager;
  jmp_buf jump;
} xi_jpeg_error;

static void xi_jpeg_exit(j_common_ptr info) { longjmp(((xi_jpeg_error*)info->err)->jump, 1); }

static void xi_jpeg_message(j_common_ptr info) { (void)info; }

// A CMYK JPEG is written inverted by Adobe's applications, as libjpeg reads it: red is C × K / 255 (as Skia converts it). Every
// CMYK JPEG is taken as inverted, whether or not it has Adobe's APP14 marker (libjpeg's saw_Adobe_marker), as Skia's SkJpegCodec
// takes it; a CMYK JPEG of another writer (rare) would show its colors inverted.
static void xi_cmyk_to_bgra(uint8_t* row, uint32_t width) {
  for (uint32_t x = 0; x < width; x++) {
    uint8_t* p = row + (size_t)x * 4;
    uint32_t c = p[0], m = p[1], y = p[2], k = p[3];
    p[0] = (uint8_t)((y * k + 127) / 255);
    p[1] = (uint8_t)((m * k + 127) / 255);
    p[2] = (uint8_t)((c * k + 127) / 255);
    p[3] = 255;
  }
}

// A side of a JPEG decoded at `eighths` / 8 of its size, rounded up as libjpeg-turbo rounds it (jdiv_round_up in
// jpeg_calc_output_dimensions).
static int32_t xi_scaled(int32_t side, int32_t eighths) { return (int32_t)(((int64_t)side * eighths + 7) / 8); }

static int32_t xi_jpeg(const uint8_t* data, size_t length, int32_t* width, int32_t* height, uint8_t* pixels, size_t stride,
                       int32_t eighths, const skcms_ICCProfile* profile) {
  struct jpeg_decompress_struct info;
  xi_jpeg_error error;
  info.err = jpeg_std_error(&error.manager);
  error.manager.error_exit = xi_jpeg_exit;
  error.manager.output_message = xi_jpeg_message;
  if (setjmp(error.jump)) {
    jpeg_destroy_decompress(&info);
    return XI_BROKEN;
  }

  jpeg_create_decompress(&info);
  jpeg_mem_src(&info, data, (unsigned long)length);
  if (jpeg_read_header(&info, TRUE) != JPEG_HEADER_OK) {
    jpeg_destroy_decompress(&info);
    return XI_BROKEN;
  }

  // Only 8-bit samples (IM1's Grill, Q5): Skia's JPEG decoder reads no other.
  if (info.data_precision != 8) {
    jpeg_destroy_decompress(&info);
    return XI_UNSUPPORTED;
  }

  if (info.image_width == 0 || info.image_height == 0 || info.image_width > INT32_MAX / 4) {
    jpeg_destroy_decompress(&info);
    return XI_BROKEN;
  }

  *width = (int32_t)info.image_width;
  *height = (int32_t)info.image_height;
  if (!pixels) {
    jpeg_destroy_decompress(&info);
    return XI_OK;
  }

  int cmyk = info.jpeg_color_space == JCS_CMYK || info.jpeg_color_space == JCS_YCCK;

  // A profile of the JPEG's kind (SkJpegCodec.cpp's check): CMYK for a CMYK image, gray or RGB for a gray one, RGB for the others
  // (Q26). A gray image is widened to BGRA first: skcms gives a gray profile's curve to the three channels and the white point to
  // its matrix, so a gray widened converts as the gray would. A CMYK image with a CMYK profile is converted from its CMYK by skcms,
  // which takes Photoshop's inverted CMYK (Q25); with none, it is turned to BGRA as before.
  if (profile) {
    uint32_t space = profile->data_color_space;
    int fits = cmyk ? space == skcms_Signature_CMYK
                    : info.jpeg_color_space == JCS_GRAYSCALE ? (space == skcms_Signature_Gray || space == skcms_Signature_RGB)
                                                             : space == skcms_Signature_RGB;
    if (!fits) {
      profile = NULL;
    }
  }

  info.out_color_space = cmyk ? JCS_CMYK : JCS_EXT_BGRA;
  info.scale_num = (unsigned int)eighths;
  info.scale_denom = 8;
  jpeg_start_decompress(&info);
  if ((int32_t)info.output_width != xi_scaled(*width, eighths) || (int32_t)info.output_height != xi_scaled(*height, eighths)) {
    jpeg_destroy_decompress(&info);
    return XI_BROKEN;
  }

  while (info.output_scanline < info.output_height) {
    JSAMPROW row = pixels + (size_t)info.output_scanline * stride;
    if (jpeg_read_scanlines(&info, &row, 1) != 1) {
      jpeg_destroy_decompress(&info);
      return XI_BROKEN;
    }

    if (cmyk && !profile) {
      xi_cmyk_to_bgra(row, info.output_width);
    }
  }

  jpeg_finish_decompress(&info);
  jpeg_destroy_decompress(&info);
  if (profile) {
    return xi_to_srgb(pixels, stride, (int32_t)info.output_width, (int32_t)info.output_height,
                      cmyk ? skcms_PixelFormat_RGBA_8888 : skcms_PixelFormat_BGRA_8888, skcms_AlphaFormat_Opaque, skcms_AlphaFormat_Opaque, profile);
  }

  return XI_OK;
}

// ---- libwebp: WebP ----

static int32_t xi_webp(const uint8_t* data, size_t length, int32_t* width, int32_t* height, uint8_t* pixels, size_t stride,
                       const skcms_ICCProfile* profile) {
  WebPDecoderConfig config;
  if (!WebPInitDecoderConfig(&config) || WebPGetFeatures(data, length, &config.input) != VP8_STATUS_OK) {
    return XI_BROKEN;
  }

  if (config.input.width <= 0 || config.input.height <= 0) {
    return XI_BROKEN;
  }

  *width = config.input.width;
  *height = config.input.height;
  if (!pixels) {
    return XI_OK;
  }

  // An animation's frames come from xi_animation_next.
  if (config.input.has_animation) {
    return XI_UNSUPPORTED;
  }

  // Only an RGB profile (SkWebpCodec.cpp's check, Q26); with one, the colors are decoded unpremultiplied to be converted.
  if (profile && profile->data_color_space != skcms_Signature_RGB) {
    profile = NULL;
  }

  config.output.colorspace = profile ? MODE_BGRA : MODE_bgrA;
  config.output.is_external_memory = 1;
  config.output.u.RGBA.rgba = pixels;
  config.output.u.RGBA.stride = (int)stride;
  config.output.u.RGBA.size = stride * (size_t)config.input.height;
  int32_t result = WebPDecode(data, length, &config) == VP8_STATUS_OK ? XI_OK : XI_BROKEN;
  WebPFreeDecBuffer(&config.output);
  if (result == XI_OK && profile) {
    result = xi_to_srgb(pixels, stride, *width, *height, skcms_PixelFormat_BGRA_8888, skcms_AlphaFormat_Unpremul,
                        skcms_AlphaFormat_PremulAsEncoded, profile);
  }

  return result;
}

// ---- The interface ----

static int32_t xi_format(const uint8_t* data, size_t length) {
  int32_t fourcc = wuffs_base__magic_number_guess_fourcc(wuffs_base__make_slice_u8((uint8_t*)data, length), true);
  switch (fourcc) {
    case WUFFS_BASE__FOURCC__PNG:
      return XI_PNG;
    case WUFFS_BASE__FOURCC__JPEG:
      return XI_JPEG;
    case WUFFS_BASE__FOURCC__WEBP:
      return XI_WEBP;
    case WUFFS_BASE__FOURCC__GIF:
      return XI_GIF;
    case WUFFS_BASE__FOURCC__BMP:
      return XI_BMP;
    default:
      return 0;
  }
}

static int32_t xi_run(const uint8_t* data, size_t length, int32_t* format, int32_t* width, int32_t* height, uint8_t* pixels,
                      size_t stride, int32_t eighths, const skcms_ICCProfile* profile) {
  if (!data || length == 0) {
    return XI_UNSUPPORTED;
  }

  *format = xi_format(data, length);
  switch (*format) {
    case XI_PNG:
    case XI_GIF:
    case XI_BMP:
      return xi_wuffs(*format, data, length, width, height, pixels, stride, profile);
    case XI_JPEG:
      return xi_jpeg(data, length, width, height, pixels, stride, eighths, profile);
    case XI_WEBP:
      return xi_webp(data, length, width, height, pixels, stride, profile);
    default:
      return XI_UNSUPPORTED;
  }
}

static int32_t xi_gif_animated(const uint8_t* data, size_t length);

// `animated` may be NULL. It is 1 for a WebP whose header says it is an animation, and for a GIF with a second frame (design
// §5.0.4, "Whether it is an animation"): an image xi_animation_open opens. A GIF with one frame and a still WebP are 0.
XI_EXPORT int32_t xi_info(const uint8_t* data, size_t length, int32_t* format, int32_t* width, int32_t* height, int32_t* animated) {
  *format = 0;
  *width = 0;
  *height = 0;
  if (animated) {
    *animated = 0;
  }

  int32_t result = xi_run(data, length, format, width, height, NULL, 0, 8, NULL);
  if (result == XI_OK && animated) {
    if (*format == XI_GIF) {
      *animated = xi_gif_animated(data, length);
    } else if (*format == XI_WEBP) {
      WebPBitstreamFeatures features;
      *animated = WebPGetFeatures(data, length, &features) == VP8_STATUS_OK && features.has_animation;
    }
  }

  return result;
}

// `eighths` is the scale, 1 to 8 eighths, which only a JPEG takes (8 for the others). `width` and `height` are those xi_info gave
// for the same bytes at that scale, a side times eighths / 8 rounded up; `pixels` holds `height` rows of `stride` bytes, `stride` at
// least width × 4. `color` is the color information C# read, or NULL for none: the colors of a profile that applies are converted to
// sRGB.
XI_EXPORT int32_t xi_decode(const uint8_t* data, size_t length, uint8_t* pixels, size_t stride, int32_t width, int32_t height,
                            int32_t eighths, const xi_color* color) {
  int32_t format = 0;
  int32_t w = 0;
  int32_t h = 0;
  int32_t result = xi_info(data, length, &format, &w, &h, NULL);
  if (result != XI_OK) {
    return result;
  }

  if (eighths < 1 || eighths > 8 || (eighths != 8 && format != XI_JPEG)) {
    return XI_UNSUPPORTED;
  }

  if (xi_scaled(w, eighths) != width || xi_scaled(h, eighths) != height || !pixels || stride < (size_t)width * 4) {
    return XI_BROKEN;
  }

  skcms_ICCProfile storage;
  return xi_run(data, length, &format, &w, &h, pixels, stride, eighths, xi_profile(color, &storage));
}

// ---- Animations: GIF and WebP ----

// An animation's decoder (design §5.0.4, "Animations", "Decoding and composing"): it gives one composed frame at a time, the whole
// canvas in premultiplied BGRA. A GIF's frames are decoded with Wuffs onto a canvas, with each frame's disposal applied before the
// next and a second canvas for "restore to previous"; a WebP's with libwebp's WebPAnimDecoder, which composes on two canvases of its
// own. The image's bytes are not copied: the caller keeps them in place until xi_animation_close.
struct xi_animation {
  int32_t format;
  int32_t width;
  int32_t height;
  const uint8_t* data;
  size_t length;

  // The plays so far, and the frames given in the play going on.
  uint32_t played;
  uint32_t frames;

  // WebP: the decoder, the plays the file asks for (0 for no end), the end of the last frame in milliseconds, and the profile the
  // colors are converted from with its bytes, when one applies.
  WebPAnimDecoder* webp;
  uint32_t webp_loops;
  int32_t webp_previous;
  int32_t converts;
  skcms_ICCProfile profile;
  uint8_t* icc;

  // GIF: the decoder with its work buffer, the canvas with the copy "restore to previous" puts back, where the first frame
  // starts, and what the frame given last asks to be done before the next.
  wuffs_base__image_decoder* gif;
  wuffs_base__io_buffer source;
  wuffs_base__pixel_buffer buffer;
  uint8_t* work;
  size_t work_length;
  uint8_t* canvas;
  uint8_t* saved;
  uint64_t first_frame;
  uint8_t disposal;
  wuffs_base__rect_ie_u32 disposal_bounds;
  uint32_t disposal_color;
};

static size_t xi_canvas_bytes(const xi_animation* animation) { return (size_t)animation->width * (size_t)animation->height * 4; }

// Whether a GIF has a second frame. Wuffs skips the pixels of a frame whose configuration alone is asked for.
static int32_t xi_gif_animated(const uint8_t* data, size_t length) {
  wuffs_base__image_decoder* decoder = xi_wuffs_decoder(XI_GIF);
  if (!decoder) {
    return 0;
  }

  wuffs_base__io_buffer source = wuffs_base__ptr_u8__reader((uint8_t*)data, length, true);
  wuffs_base__frame_config frame = {0};
  int32_t animated = wuffs_base__image_decoder__decode_frame_config(decoder, &frame, &source).repr == NULL &&
                     wuffs_base__image_decoder__decode_frame_config(decoder, &frame, &source).repr == NULL;
  free(decoder);
  return animated;
}

static int32_t xi_gif_open(xi_animation* animation, size_t max_canvas_bytes) {
  animation->gif = xi_wuffs_decoder(XI_GIF);
  if (!animation->gif) {
    return XI_OUT_OF_MEMORY;
  }

  animation->source = wuffs_base__ptr_u8__reader((uint8_t*)animation->data, animation->length, true);
  wuffs_base__image_config config = {0};
  if (wuffs_base__image_decoder__decode_image_config(animation->gif, &config, &animation->source).repr) {
    return XI_BROKEN;
  }

  uint32_t w = wuffs_base__pixel_config__width(&config.pixcfg);
  uint32_t h = wuffs_base__pixel_config__height(&config.pixcfg);
  if (w == 0 || h == 0 || w > INT32_MAX / 4 || h > INT32_MAX) {
    return XI_BROKEN;
  }

  if ((uint64_t)w * h * 4 > max_canvas_bytes) {
    return XI_UNSUPPORTED;
  }

  animation->width = (int32_t)w;
  animation->height = (int32_t)h;
  uint64_t work_length = wuffs_base__image_decoder__workbuf_len(animation->gif).max_incl;
  if (work_length > SIZE_MAX) {
    return XI_OUT_OF_MEMORY;
  }

  animation->work_length = (size_t)work_length;
  animation->work = (uint8_t*)malloc(animation->work_length ? animation->work_length : 1);
  animation->canvas = (uint8_t*)calloc(xi_canvas_bytes(animation), 1);
  animation->saved = (uint8_t*)malloc(xi_canvas_bytes(animation));
  if (!animation->work || !animation->canvas || !animation->saved) {
    return XI_OUT_OF_MEMORY;
  }

  wuffs_base__pixel_config__set(&config.pixcfg, WUFFS_BASE__PIXEL_FORMAT__BGRA_PREMUL, WUFFS_BASE__PIXEL_SUBSAMPLING__NONE, w, h);
  if (wuffs_base__pixel_buffer__set_interleaved(&animation->buffer, &config.pixcfg,
                                                wuffs_base__make_table_u8(animation->canvas, (size_t)w * 4, h, (size_t)w * 4),
                                                wuffs_base__empty_slice_u8())
          .repr) {
    return XI_BROKEN;
  }

  return XI_OK;
}

// Fills the part of a rectangle that is on the canvas with one premultiplied color.
static void xi_gif_fill(xi_animation* animation, wuffs_base__rect_ie_u32 bounds, uint32_t color) {
  uint32_t right = bounds.max_excl_x < (uint32_t)animation->width ? bounds.max_excl_x : (uint32_t)animation->width;
  uint32_t bottom = bounds.max_excl_y < (uint32_t)animation->height ? bounds.max_excl_y : (uint32_t)animation->height;
  for (uint32_t y = bounds.min_incl_y; y < bottom; y++) {
    uint8_t* row = animation->canvas + ((size_t)y * (size_t)animation->width + bounds.min_incl_x) * 4;
    for (uint32_t x = bounds.min_incl_x; x < right; x++, row += 4) {
      memcpy(row, &color, 4);
    }
  }
}

static int32_t xi_gif_next(xi_animation* animation, const uint8_t** frame, int32_t* duration) {
  wuffs_base__frame_config config = {0};
  wuffs_base__status status = wuffs_base__image_decoder__decode_frame_config(animation->gif, &config, &animation->source);
  if (status.repr == wuffs_base__note__end_of_data) {
    // The play ended. The count of plays is known by now (the extension that holds it can come anywhere before the end): 0 is
    // no end.
    uint32_t loops = wuffs_base__image_decoder__num_animation_loops(animation->gif);
    animation->played++;
    if (animation->frames == 0) {
      return XI_BROKEN;
    }

    if (loops != 0 && animation->played >= loops) {
      return XI_END;
    }

    if (wuffs_base__image_decoder__restart_frame(animation->gif, 0, animation->first_frame).repr) {
      return XI_BROKEN;
    }

    animation->source.meta.ri = (size_t)animation->first_frame;
    animation->frames = 0;
    status = wuffs_base__image_decoder__decode_frame_config(animation->gif, &config, &animation->source);
  }

  if (status.repr) {
    return XI_BROKEN;
  }

  if (animation->frames == 0) {
    // A play starts on an empty canvas.
    animation->first_frame = wuffs_base__frame_config__io_position(&config);
    memset(animation->canvas, 0, xi_canvas_bytes(animation));
  } else if (animation->disposal == WUFFS_BASE__ANIMATION_DISPOSAL__RESTORE_BACKGROUND) {
    xi_gif_fill(animation, animation->disposal_bounds, animation->disposal_color);
  } else if (animation->disposal == WUFFS_BASE__ANIMATION_DISPOSAL__RESTORE_PREVIOUS) {
    memcpy(animation->canvas, animation->saved, xi_canvas_bytes(animation));
  }

  animation->disposal = wuffs_base__frame_config__disposal(&config);
  animation->disposal_bounds = wuffs_base__frame_config__bounds(&config);
  animation->disposal_color = wuffs_base__frame_config__background_color(&config);
  if (animation->disposal == WUFFS_BASE__ANIMATION_DISPOSAL__RESTORE_PREVIOUS) {
    memcpy(animation->saved, animation->canvas, xi_canvas_bytes(animation));
  }

  wuffs_base__pixel_blend blend =
      wuffs_base__frame_config__overwrite_instead_of_blend(&config) ? WUFFS_BASE__PIXEL_BLEND__SRC : WUFFS_BASE__PIXEL_BLEND__SRC_OVER;
  if (wuffs_base__image_decoder__decode_frame(animation->gif, &animation->buffer, &animation->source, blend,
                                              wuffs_base__make_slice_u8(animation->work, animation->work_length), NULL)
          .repr) {
    return XI_BROKEN;
  }

  uint64_t milliseconds = wuffs_base__frame_config__duration(&config) / WUFFS_BASE__FLICKS_PER_MILLISECOND;
  *duration = milliseconds > INT32_MAX ? INT32_MAX : (int32_t)milliseconds;
  *frame = animation->canvas;
  animation->frames++;
  return XI_OK;
}

static int32_t xi_webp_open(xi_animation* animation, const xi_color* color, size_t max_canvas_bytes) {
  WebPBitstreamFeatures features;
  if (WebPGetFeatures(animation->data, animation->length, &features) != VP8_STATUS_OK || features.width <= 0 || features.height <= 0) {
    return XI_BROKEN;
  }

  if ((uint64_t)features.width * (uint64_t)features.height * 4 > max_canvas_bytes) {
    return XI_UNSUPPORTED;
  }

  // Only an RGB profile, as for a still WebP (Q26). Its bytes are copied, with the room skcms's parse reads ahead into: the
  // profile points into them for as long as the frames are converted.
  if (color && color->icc && color->icc_length > 0) {
    animation->icc = (uint8_t*)calloc(color->icc_length + 4, 1);
    if (!animation->icc) {
      return XI_OUT_OF_MEMORY;
    }

    memcpy(animation->icc, color->icc, color->icc_length);
    xi_color copy = {animation->icc, color->icc_length, 0, {0}, 0, 0};
    const skcms_ICCProfile* profile = xi_profile(&copy, &animation->profile);
    animation->converts = profile && profile->data_color_space == skcms_Signature_RGB;
  }

  WebPAnimDecoderOptions options;
  if (!WebPAnimDecoderOptionsInit(&options)) {
    return XI_BROKEN;
  }

  // The colors that are converted are composed unpremultiplied, and premultiplied after the conversion (Q47).
  options.color_mode = animation->converts ? MODE_BGRA : MODE_bgrA;
  options.use_threads = 0;
  WebPData data = {animation->data, animation->length};
  animation->webp = WebPAnimDecoderNew(&data, &options);
  WebPAnimInfo info;
  if (!animation->webp || !WebPAnimDecoderGetInfo(animation->webp, &info)) {
    return XI_BROKEN;
  }

  animation->width = (int32_t)info.canvas_width;
  animation->height = (int32_t)info.canvas_height;
  animation->webp_loops = info.loop_count;
  return XI_OK;
}

static int32_t xi_webp_next(xi_animation* animation, uint8_t* converted, const uint8_t** frame, int32_t* duration) {
  if (!WebPAnimDecoderHasMoreFrames(animation->webp)) {
    animation->played++;
    if (animation->frames == 0) {
      return XI_BROKEN;
    }

    if (animation->webp_loops != 0 && animation->played >= animation->webp_loops) {
      return XI_END;
    }

    WebPAnimDecoderReset(animation->webp);
    animation->webp_previous = 0;
    animation->frames = 0;
  }

  uint8_t* canvas = NULL;
  int end = 0;
  if (!WebPAnimDecoderGetNext(animation->webp, &canvas, &end)) {
    return XI_BROKEN;
  }

  *duration = end > animation->webp_previous ? end - animation->webp_previous : 0;
  animation->webp_previous = end;
  animation->frames++;
  if (!animation->converts) {
    *frame = canvas;
    return XI_OK;
  }

  // The decoder's canvas is left as it is: the next frame is composed on it.
  if (!converted) {
    return XI_BROKEN;
  }

  size_t stride = (size_t)animation->width * 4;
  for (int32_t y = 0; y < animation->height; y++) {
    if (!skcms_Transform(canvas + (size_t)y * stride, skcms_PixelFormat_BGRA_8888, skcms_AlphaFormat_Unpremul, &animation->profile,
                         converted + (size_t)y * stride, skcms_PixelFormat_BGRA_8888, skcms_AlphaFormat_PremulAsEncoded,
                         skcms_sRGB_profile(), (size_t)animation->width)) {
      return XI_BROKEN;
    }
  }

  *frame = converted;
  return XI_OK;
}

// Closes an animation's decoder and frees its canvases; NULL is allowed.
XI_EXPORT void xi_animation_close(xi_animation* animation) {
  if (!animation) {
    return;
  }

  if (animation->webp) {
    WebPAnimDecoderDelete(animation->webp);
  }

  free(animation->icc);
  free(animation->gif);
  free(animation->work);
  free(animation->canvas);
  free(animation->saved);
  free(animation);
}

// Opens the decoder of an animated GIF or WebP. `data` stays in place, unchanged, until xi_animation_close. `color` is the color
// information C# read, or NULL: `converts` is 1 when the frames' colors are converted to sRGB, which xi_animation_next then needs
// a buffer for. A canvas of more than `max_canvas_bytes` (width × height × 4) is not opened. `width` and `height` are the canvas's.
XI_EXPORT int32_t xi_animation_open(const uint8_t* data, size_t length, const xi_color* color, size_t max_canvas_bytes,
                                    xi_animation** animation, int32_t* width, int32_t* height, int32_t* converts) {
  *animation = NULL;
  *width = 0;
  *height = 0;
  *converts = 0;
  if (!data || length == 0) {
    return XI_UNSUPPORTED;
  }

  int32_t format = xi_format(data, length);
  if (format != XI_GIF && format != XI_WEBP) {
    return XI_UNSUPPORTED;
  }

  xi_animation* opened = (xi_animation*)calloc(1, sizeof(xi_animation));
  if (!opened) {
    return XI_OUT_OF_MEMORY;
  }

  opened->format = format;
  opened->data = data;
  opened->length = length;
  int32_t result = format == XI_GIF ? xi_gif_open(opened, max_canvas_bytes) : xi_webp_open(opened, color, max_canvas_bytes);
  if (result != XI_OK) {
    xi_animation_close(opened);
    return result;
  }

  *animation = opened;
  *width = opened->width;
  *height = opened->height;
  *converts = opened->converts;
  return XI_OK;
}

// Composes the next frame: the first of the next play after the last of one, and XI_END once the plays the file asks for are
// done. `frame` is the whole canvas in premultiplied BGRA, rows of width × 4 bytes, good until the next call: the decoder's own
// canvas, or `converted` (as large as the canvas), which the colors are converted into when the decoder converts and which may be
// NULL when it does not. `duration` is how long the frame shows, in milliseconds, as the file gives it.
XI_EXPORT int32_t xi_animation_next(xi_animation* animation, uint8_t* converted, const uint8_t** frame, int32_t* duration) {
  *frame = NULL;
  *duration = 0;
  return animation->format == XI_GIF ? xi_gif_next(animation, frame, duration) : xi_webp_next(animation, converted, frame, duration);
}

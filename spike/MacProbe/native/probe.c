// The static library of the macOS CI probe (spike/MacProbe/README.md, probes 6 and 7). ci/probe.sh builds it with the runner's
// clang into native/out/<rid>/libprobe.a; MacProbe.csproj links it into the NativeAOT executable with NativeLibrary + DirectPInvoke.
#include <Block.h>

int probe_add(int a, int b) { return a + b; }

const char *probe_compiler(void) { return "clang " __clang_version__; }

const char *probe_arch(void) {
#if defined(__aarch64__)
    return "arm64";
#elif defined(__x86_64__)
    return "x86_64";
#else
    return "unknown";
#endif
}

// The deployment target the library was compiled for (140000 = macOS 14.0), or -1 when clang does not say.
int probe_min_macos(void) {
#if defined(__ENVIRONMENT_MAC_OS_X_VERSION_MIN_REQUIRED__)
    return __ENVIRONMENT_MAC_OS_X_VERSION_MIN_REQUIRED__;
#elif defined(__ENVIRONMENT_OS_VERSION_MIN_REQUIRED__)
    return __ENVIRONMENT_OS_VERSION_MIN_REQUIRED__;
#else
    return -1;
#endif
}

static int global_block_runs;

// A block that clang builds itself, so that the C# side can compare its layout (isa, flags, descriptor) with the one it assumes.
// It captures nothing (a static is not a capture), so clang emits it as a global block.
void *probe_global_block(void) {
    void (^block)(void) = ^{
        global_block_runs++;
    };
    return (void *)block;
}

int probe_global_block_runs(void) { return global_block_runs; }

// Calls a block the way clang-compiled code calls any block: through its invoke pointer, with the block as first argument.
void probe_invoke_block(void *block) {
    void (^b)(void) = (void (^)(void))block;
    b();
}

// Block_copy (which moves a stack block to the heap), a call through the copy, and Block_release. Returns 1 when the copy is a
// different object, and the copy's isa in *isa_after_copy.
int probe_copy_call_release(void *block, void **isa_after_copy) {
    void (^b)(void) = (void (^)(void))block;
    void (^copy)(void) = Block_copy(b);
    *isa_after_copy = *(void **)(void *)copy;
    copy();
    int moved = (void *)copy != block;
    Block_release(copy);
    return moved;
}

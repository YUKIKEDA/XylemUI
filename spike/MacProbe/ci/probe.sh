#!/usr/bin/env bash
# The stages of the macOS CI probe (spike/MacProbe/README.md), one per workflow step of .github/workflows/mac-probe.yml:
#   bash spike/MacProbe/ci/probe.sh env|native|publish|inspect|run|metal-tool|summary
# Each stage writes `PROBE <name>: <value>` lines to $PROBE_OUT/summary.txt and its full output to $PROBE_OUT/log/<stage>.log.
# A stage does not stop at a failure: it records `FAILED ...` and goes on, so one run answers as much as it can. Written for the
# bash 3.2 that macOS ships.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
PROJECT_DIR="$ROOT/spike/MacProbe"
OUT="${PROBE_OUT:?set PROBE_OUT to the output folder}"
SUMMARY="$OUT/summary.txt"
TEMP_DIR="${RUNNER_TEMP:-/tmp}/mac-probe"
RIDS="osx-arm64 osx-x64"
PROBE_GROUPS="env frameworks objc classreg blocks staticlink metal coretext display runloop"
mkdir -p "$OUT/log" "$OUT/bin" "$TEMP_DIR"
touch "$SUMMARY"

probe() { printf 'PROBE %s: %s\n' "$1" "$2" | tee -a "$SUMMARY"; }
one_line() { tr '\n' ' ' | sed -e 's/  */ /g' -e 's/ *$//'; }
arch_of() { case "$1" in osx-arm64) echo arm64 ;; osx-x64) echo x86_64 ;; esac; }
size_of() { stat -f%z "$1" 2>/dev/null || echo "?"; }

# The deployment targets recorded in a Mach-O file or in every member of a static archive, with their counts.
minos_of() {
    otool -l "$1" 2>/dev/null |
        awk '/LC_BUILD_VERSION|LC_VERSION_MIN_MACOSX/ { f = 1 } f && ($1 == "minos" || $1 == "version") { print $2; f = 0 }' |
        sort | uniq -c | awk '{ printf "%s%s(x%s)", sep, $2, $1; sep = " " }'
}

# Runs a command with a time limit (macOS has no `timeout`). A killed command returns 137.
run_with_timeout() {
    local seconds=$1
    shift
    "$@" &
    local pid=$!
    (sleep "$seconds" && kill -9 "$pid" 2>/dev/null) &
    local watcher=$!
    wait "$pid"
    local rc=$?
    kill "$watcher" 2>/dev/null
    return $rc
}

stage_env() {
    probe runner.label "${RUNNER_LABEL:-?}"
    probe runner.macos "$(sw_vers -productVersion 2>&1) build $(sw_vers -buildVersion 2>&1)"
    probe runner.uname "$(uname -a)"
    probe runner.arch "$(uname -m)"
    probe runner.cpu "$(sysctl -n machdep.cpu.brand_string 2>&1)"
    probe runner.model "$(sysctl -n hw.model 2>&1)"
    probe runner.ncpu "$(sysctl -n hw.ncpu 2>&1)"
    probe runner.memsize "$(sysctl -n hw.memsize 2>&1)"
    if [ "$(uname -m)" = arm64 ]; then
        if arch -x86_64 /usr/bin/true 2>/dev/null; then
            probe runner.rosetta "available (arch -x86_64 /usr/bin/true ran)"
        else
            probe runner.rosetta "unavailable (arch -x86_64 /usr/bin/true failed)"
        fi
    else
        probe runner.rosetta "n/a (x86_64 host)"
    fi
    probe runner.xcode "$(xcodebuild -version 2>&1 | one_line)"
    probe runner.xcode_select "$(xcode-select -p 2>&1)"
    probe runner.sdk "$(xcrun --sdk macosx --show-sdk-version 2>&1) at $(xcrun --sdk macosx --show-sdk-path 2>&1)"
    probe runner.clang "$(clang --version 2>&1 | head -n 1)"
    probe runner.dotnet "$(dotnet --version 2>&1)"
    dotnet --info 2>&1
    local displays="$OUT/log/system_profiler_displays.txt"
    system_profiler SPDisplaysDataType >"$displays" 2>&1
    cat "$displays"
    probe runner.gpu "$(awk -F': ' '/Chipset Model/ { print $2 }' "$displays" | one_line)"
    probe runner.gpu_metal "$(awk -F': ' '/Metal Support|Metal Family/ { print $2 }' "$displays" | one_line)"
    probe runner.display_resolutions "$(awk -F': ' '/Resolution/ { print $2 }' "$displays" | paste -sd ';' -)"
}

stage_native() {
    for rid in $RIDS; do
        local arch dir
        arch="$(arch_of "$rid")"
        dir="$PROJECT_DIR/native/out/$rid"
        mkdir -p "$dir"
        if clang -c -O2 -fblocks -Wall -arch "$arch" -o "$dir/probe.o" "$PROJECT_DIR/native/probe.c" &&
            ar rcs "$dir/libprobe.a" "$dir/probe.o"; then
            probe "native.libprobe.$rid" "ok $(size_of "$dir/libprobe.a") bytes; minos $(minos_of "$dir/libprobe.a")"
        else
            probe "native.libprobe.$rid" "FAILED clang or ar (see log/native.log)"
        fi
    done

    # AccessKit: the archive and checksum pinned in vendor/versions.lock (name version sha256 url [mirror ...]).
    local line version sha zip extract actual
    line="$(awk '$1 == "accesskit"' "$ROOT/vendor/versions.lock")"
    version="$(echo "$line" | awk '{ print $2 }')"
    sha="$(echo "$line" | awk '{ print $3 }')"
    zip="$TEMP_DIR/accesskit-$version.zip"
    extract="$TEMP_DIR/accesskit-$version"
    probe accesskit.version "${version:-not in versions.lock}"
    rm -f "$zip"
    for url in $(echo "$line" | awk '{ for (i = 4; i <= NF; i++) print $i }'); do
        echo "download $url"
        # The API URL needs the octet-stream Accept header; the release download URL ignores it.
        if curl -fsSL --retry 3 --retry-delay 5 -H "Accept: application/octet-stream" -o "$zip" "$url"; then
            probe accesskit.download "ok from $url"
            break
        fi
        rm -f "$zip"
    done
    if [ ! -f "$zip" ]; then
        probe accesskit.download "FAILED every URL of vendor/versions.lock"
        return 0
    fi
    actual="$(shasum -a 256 "$zip" | awk '{ print $1 }')"
    if [ "$actual" = "$sha" ]; then
        probe accesskit.sha256 "match $actual"
    else
        probe accesskit.sha256 "MISMATCH got $actual, versions.lock has $sha; not used"
        return 0
    fi
    rm -rf "$extract"
    mkdir -p "$extract"
    unzip -q -o "$zip" -d "$extract"
    probe accesskit.archive_macos_files "$(unzip -Z1 "$zip" | grep '/lib/macos/' | grep -v '/$' | one_line)"
    for rid in $RIDS; do
        local arch lib
        arch="$(arch_of "$rid")"
        lib="$(find "$extract" -path "*/lib/macos/$arch/static/libaccesskit.a" | head -n 1)"
        if [ -z "$lib" ]; then
            probe "accesskit.static.$rid" "FAILED no lib/macos/$arch/static/libaccesskit.a in the archive"
            continue
        fi
        cp "$lib" "$PROJECT_DIR/native/out/$rid/libaccesskit.a"
        probe "accesskit.static.$rid" "ok $(size_of "$lib") bytes; minos $(minos_of "$lib")"
        probe "accesskit.symbols.$rid" "$(nm -gU "$lib" 2>/dev/null | awk '{ print $3 }' | grep -E '^_accesskit_(node_new|macos_adapter_new)$' | one_line)"
    done
}

publish_one() {
    local rid=$1 variant=$2 out=$3
    shift 3
    local log="$OUT/log/publish-$rid-$variant.log"
    local start=$SECONDS
    dotnet publish "$PROJECT_DIR/MacProbe.csproj" -c Release -r "$rid" -o "$out" "$@" >"$log" 2>&1
    local rc=$?
    cat "$log"
    local warnings
    warnings="$(grep -c ': warning ' "$log")"
    if [ $rc -eq 0 ] && [ -x "$out/MacProbe" ]; then
        probe "publish.$rid.$variant" "ok in $((SECONDS - start)) s; $warnings warning line(s)"
    else
        probe "publish.$rid.$variant" "FAILED rc $rc in $((SECONDS - start)) s: $(grep -E ': error |error :|ld: |Undefined symbols' "$log" | head -n 5 | one_line)"
    fi
}

stage_publish() {
    for rid in $RIDS; do
        publish_one "$rid" base "$OUT/bin/$rid"
        if [ -f "$PROJECT_DIR/native/out/$rid/libaccesskit.a" ]; then
            publish_one "$rid" accesskit "$OUT/bin/$rid-accesskit" -p:ProbeAccessKit=true
        else
            probe "publish.$rid.accesskit" "SKIPPED no libaccesskit.a (see the native step)"
        fi
    done
}

stage_inspect() {
    ls -lR "$OUT/bin"
    for exe in "$OUT"/bin/*/MacProbe; do
        [ -f "$exe" ] || continue
        local name dylibs outside
        name="$(basename "$(dirname "$exe")")"
        echo "== $exe"
        otool -L "$exe"
        codesign -dvvv "$exe" 2>&1
        probe "exe.$name.size" "$(size_of "$exe") bytes"
        probe "exe.$name.file" "$(file -b "$exe")"
        probe "exe.$name.minos" "$(minos_of "$exe")"
        dylibs="$(otool -L "$exe" | tail -n +2 | awk '{ print $1 }')"
        probe "exe.$name.dylibs" "$(echo "$dylibs" | one_line)"
        outside="$(echo "$dylibs" | grep -v -E '^(/usr/lib/|/System/Library/)' | one_line)"
        probe "exe.$name.only_system_dylibs" "$([ -z "$outside" ] && echo yes || echo "no: $outside")"
        probe "exe.$name.codesign" "$(codesign -dv "$exe" 2>&1 | grep -E '^(Signature|CodeDirectory|Identifier|TeamIdentifier)' | one_line)"
        probe "exe.$name.codesign_verify" "$(codesign --verify --verbose "$exe" 2>&1 | one_line)"
        if [ -d "$exe.dSYM" ]; then
            probe "exe.$name.dsym" "$(du -sk "$exe.dSYM" | awk '{ print $1 }') KB beside the executable"
        fi
    done
    for rid in $RIDS; do
        local base="$OUT/bin/$rid/MacProbe" with="$OUT/bin/$rid-accesskit/MacProbe"
        if [ -f "$base" ] && [ -f "$with" ]; then
            probe "exe.$rid.accesskit_growth" "$(($(size_of "$with") - $(size_of "$base"))) bytes ($(size_of "$base") -> $(size_of "$with"))"
        fi
    done
}

run_group() {
    local label=$1 exe=$2 group=$3
    local log="$OUT/log/run-$label-$group.log"
    PROBE_SUMMARY="$SUMMARY" run_with_timeout 180 "$exe" --only "$group" >"$log" 2>&1
    local rc=$?
    cat "$log"
    if [ $rc -eq 0 ]; then
        probe "run.$label.$group" "exit 0"
    else
        local last
        last="$(grep '^STEP ' "$log" | tail -n 1 | cut -d' ' -f2)"
        probe "run.$label.$group" "exit $rc${last:+, last step $last}; $(grep -v -E '^(STEP|PROBE|GROUP|  )' "$log" | head -n 3 | one_line)"
    fi
}

stage_run() {
    for rid in $RIDS; do
        local exe="$OUT/bin/$rid/MacProbe"
        if [ ! -x "$exe" ]; then
            probe "run.$rid" "SKIPPED no executable"
            continue
        fi
        for group in $PROBE_GROUPS; do
            run_group "$rid" "$exe" "$group"
        done
        local with="$OUT/bin/$rid-accesskit/MacProbe"
        if [ -x "$with" ]; then
            run_group "$rid-accesskit" "$with" accesskit
            run_group "$rid-accesskit" "$with" staticlink
        fi
    done
}

stage_metal_tool() {
    local src="$TEMP_DIR/probe.metal"
    cat >"$src" <<'EOF'
#include <metal_stdlib>
using namespace metal;
fragment half4 fs() { return half4(0.0, 1.0, 0.0, 1.0); }
EOF
    probe metal_tool.version "$(xcrun -sdk macosx metal --version 2>&1 | head -n 2 | one_line)"
    if xcrun -sdk macosx metal -c "$src" -o "$TEMP_DIR/probe.air" 2>&1 && xcrun -sdk macosx metallib "$TEMP_DIR/probe.air" -o "$TEMP_DIR/probe.metallib" 2>&1; then
        probe metal_tool.compile "ok .metal -> .air -> .metallib ($(size_of "$TEMP_DIR/probe.metallib") bytes)"
    else
        probe metal_tool.compile "FAILED xcrun metal/metallib (see log/metal-tool.log; Xcode may need its Metal toolchain component)"
    fi
}

stage_summary() {
    local count failed
    count="$(grep -c '^PROBE ' "$SUMMARY")"
    failed="$(grep -c -E 'FAILED|MISMATCH' "$SUMMARY")"
    echo "$count PROBE lines, $failed with FAILED or MISMATCH"
    cat "$SUMMARY"
    if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
        {
            echo "## macOS probe on ${RUNNER_LABEL:-?}"
            echo
            echo "$count PROBE lines, $failed with FAILED or MISMATCH. The full logs are in the artifact."
            echo
            echo '```text'
            cat "$SUMMARY"
            echo '```'
        } >>"$GITHUB_STEP_SUMMARY"
    fi
}

stage="${1:?stage: env|native|publish|inspect|run|metal-tool|summary}"
case "$stage" in
env | native | publish | inspect | run | metal-tool | summary) ;;
*)
    echo "unknown stage $stage" >&2
    exit 2
    ;;
esac
# A stage records its failures as PROBE lines and always exits 0, so that every later step still runs and answers.
"stage_${stage//-/_}" 2>&1 | tee -a "$OUT/log/$stage.log"
exit 0

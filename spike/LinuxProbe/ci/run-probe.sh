#!/usr/bin/env bash
# The Linux probe (spike/LinuxProbe/README.md). Run by .github/workflows/linux-probe.yml on a GitHub-hosted Ubuntu runner:
#
#   bash spike/LinuxProbe/ci/run-probe.sh <linux-x64|linux-arm64> <output folder>
#
# Every answer is a line "PROBE <name>: <value>" in the log and in <output folder>/summary.txt. Every step is guarded: a failed
# step writes "PROBE <name>: FAILED <reason>" and the script goes on. The script itself always ends with 0; the summary says what
# failed. Needs: WGPU_NATIVE_TAG and WGPU_NATIVE_SHA256 in the environment (the workflow pins them), passwordless sudo, dotnet.

set -u -o pipefail

RID="${1:?usage: run-probe.sh <linux-x64|linux-arm64> <output folder>}"
OUT="${2:?usage: run-probe.sh <linux-x64|linux-arm64> <output folder>}"
: "${WGPU_NATIVE_TAG:?WGPU_NATIVE_TAG is not set}"
: "${WGPU_NATIVE_SHA256:?WGPU_NATIVE_SHA256 is not set}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
PROJECT="$ROOT/spike/LinuxProbe"
WORK="${RUNNER_TEMP:-/tmp}/linux-probe-work"
mkdir -p "$OUT" "$WORK"
SUMMARY="$OUT/summary.txt"
# The D-Bus part (below) runs this script again and adds to the same summary.
[ "${PROBE_DBUS_INNER:-}" = "1" ] || : > "$SUMMARY"

case "$RID" in
  linux-x64) WGPU_ARCH=x86_64 ;;
  linux-arm64) WGPU_ARCH=aarch64 ;;
  *) echo "unknown RID $RID"; exit 2 ;;
esac

export DEBIAN_FRONTEND=noninteractive
export RUST_BACKTRACE=1

# ---------------------------------------------------------------------------------------------------------------------------
# Helpers

record() { # record <name> <value...>
  local name="$1"
  shift
  local line="PROBE $name: $*"
  echo "$line"
  echo "$line" >> "$SUMMARY"
}

fail() { # fail <name> <reason...>
  local name="$1"
  shift
  record "$name" "FAILED $*"
}

group() { echo "::group::$*"; }
endgroup() { echo "::endgroup::"; }

# Runs a command with a timeout, keeps its output in $OUT/<file>, prints it, and records how it ended.
# capture <name> <seconds> <file> <command...>
capture() {
  local name="$1" seconds="$2" file="$3"
  shift 3
  timeout --kill-after=10 "$seconds" "$@" > "$OUT/$file" 2>&1
  local code=$?
  cat "$OUT/$file"
  case $code in
    0) record "$name" "ok" ;;
    124) fail "$name" "timed out after ${seconds}s (output in $file)" ;;
    *) fail "$name" "exit $code$( [ $code -gt 128 ] && echo " (signal $((code - 128)))") (output in $file)" ;;
  esac
  return $code
}

# Runs a section of the probe executable; its PROBE lines go to the summary, the rest stays in the log and in $OUT/<file>.
# probe_run <label> <seconds> <executable> <arguments...>
probe_run() {
  local label="$1" seconds="$2" exe="$3"
  shift 3
  local file="run-$label.log"
  if [ ! -x "$exe" ]; then
    fail "run.$label" "executable $exe does not exist (its publish failed)"
    return
  fi
  timeout --kill-after=10 "$seconds" "$exe" "$@" > "$OUT/$file" 2>&1
  local code=$?
  cat "$OUT/$file"
  grep '^PROBE ' "$OUT/$file" >> "$SUMMARY"
  case $code in
    0) record "run.$label.exit" "0" ;;
    124) fail "run.$label.exit" "timed out after ${seconds}s" ;;
    *) fail "run.$label.exit" "exit $code$( [ $code -gt 128 ] && echo " (signal $((code - 128)))")" ;;
  esac
}

apt_install() { # apt_install <name> <packages...>: all at once, then one by one when that fails
  local name="$1"
  shift
  if sudo apt-get install -y --no-install-recommends "$@" > "$OUT/apt-$name.log" 2>&1; then
    record "apt.$name" "ok: $*"
    return
  fi
  tail -n 30 "$OUT/apt-$name.log"
  local missing=()
  for package in "$@"; do
    sudo apt-get install -y --no-install-recommends "$package" >> "$OUT/apt-$name.log" 2>&1 || missing+=("$package")
  done
  if [ ${#missing[@]} -eq 0 ]; then
    record "apt.$name" "ok one by one: $*"
  else
    fail "apt.$name" "could not install: ${missing[*]} (log apt-$name.log)"
  fi
}

package_state() { dpkg-query -W -f='${Status} ${Version}' "$1" 2>/dev/null || echo "not installed"; }

# ELF facts of one executable: size, the separate debug file, DT_NEEDED, ldd, the newest GLIBC symbol version it needs.
elf_facts() { # elf_facts <name> <path>
  local name="$1" exe="$2"
  if [ ! -f "$exe" ]; then
    fail "$name.size" "no executable at $exe"
    return
  fi
  record "$name.size" "$(stat -c %s "$exe") bytes"
  if [ -f "$exe.dbg" ]; then record "$name.dbg_size" "$(stat -c %s "$exe.dbg") bytes"; fi
  record "$name.file" "$(file -b "$exe")"
  readelf -d "$exe" > "$OUT/$name.readelf-d.txt" 2>&1
  record "$name.needed" "$(grep NEEDED "$OUT/$name.readelf-d.txt" | sed -E 's/.*\[(.*)\].*/\1/' | tr '\n' ' ')"
  ldd "$exe" > "$OUT/$name.ldd.txt" 2>&1
  record "$name.ldd" "$(tr -s ' \t\n' ' ' < "$OUT/$name.ldd.txt")"
  objdump -T "$exe" > "$OUT/$name.objdump-T.txt" 2>&1
  record "$name.glibc_min" "$(grep -o 'GLIBC_[0-9][0-9.]*' "$OUT/$name.objdump-T.txt" | sort -uV | tail -n 1)"
  record "$name.glibc_versions" "$(grep -o 'GLIBC_[0-9][0-9.]*' "$OUT/$name.objdump-T.txt" | sort -uV | tr '\n' ' ')"
  local other
  other="$(grep -oE '(GLIBCXX|CXXABI|GCC)_[0-9][0-9.]*' "$OUT/$name.objdump-T.txt" | sort -uV | tr '\n' ' ')"
  record "$name.other_symbol_versions" "${other:-none}"
  size -A "$exe" > "$OUT/$name.sections.txt" 2>&1
}

wait_for_path() { # wait_for_path <path> <seconds>
  local path="$1" seconds="$2" i
  for ((i = 0; i < seconds * 10; i++)); do
    [ -e "$path" ] && return 0
    sleep 0.1
  done
  return 1
}

# The Wayland globals a compositor advertises, as wayland-info prints them, and whether the protocols the report asks about are
# among them (question 6).
wayland_globals() { # wayland_globals <label>
  local label="$1"
  if ! command -v wayland-info > /dev/null; then
    fail "wayland.$label.globals" "wayland-info not installed"
    return
  fi
  timeout 20 wayland-info > "$OUT/wayland-info-$label.txt" 2>&1
  local code=$?
  if [ $code -ne 0 ]; then
    fail "wayland.$label.globals" "wayland-info exit $code: $(head -c 300 "$OUT/wayland-info-$label.txt" | tr '\n' ' ')"
    return
  fi
  record "wayland.$label.globals" "$(grep -oE "interface: '[a-z0-9_]+'" "$OUT/wayland-info-$label.txt" | sed -E "s/interface: '(.*)'/\1/" | sort -u | tr '\n' ' ')"
  local interface
  for interface in zwp_text_input_manager_v3 zwp_text_input_manager_v1 zwp_input_method_manager_v2 wp_fractional_scale_manager_v1 \
    wp_viewporter wp_presentation xdg_wm_base zxdg_decoration_manager_v1 zwp_primary_selection_device_manager_v1 \
    wp_cursor_shape_manager_v1 xdg_activation_v1 zwp_linux_dmabuf_v1; do
    if grep -q "'$interface'" "$OUT/wayland-info-$label.txt"; then
      record "wayland.$label.has.$interface" "yes ($(grep -A0 "'$interface'" "$OUT/wayland-info-$label.txt" | grep -oE 'version: *[0-9]+' | head -n 1))"
    else
      record "wayland.$label.has.$interface" "no"
    fi
  done
}

stop_pid() { [ -n "${1:-}" ] && kill "$1" 2> /dev/null && sleep 0.5; kill -9 "$1" 2> /dev/null || true; }

# ---------------------------------------------------------------------------------------------------------------------------
# The D-Bus part runs inside dbus-run-session, as a second invocation of this script (question 5).

if [ "${PROBE_DBUS_INNER:-}" = "1" ]; then
  record "dbus.inner.session_address" "${DBUS_SESSION_BUS_ADDRESS:-unset}"
  probe_run env-in-dbus-session 30 "$PROBE_EXE" env in_dbus_session
  if timeout 20 gdbus call --session --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus \
    --method org.freedesktop.DBus.GetId > "$OUT/dbus-getid.txt" 2>&1; then
    record "dbus.inner.session_bus" "reachable, id $(tr -d "()',\n" < "$OUT/dbus-getid.txt")"
  else
    fail "dbus.inner.session_bus" "$(tr '\n' ' ' < "$OUT/dbus-getid.txt")"
  fi
  # org.a11y.Bus is started on demand by the session bus from at-spi2-core's service file.
  if timeout 30 gdbus call --session --dest org.a11y.Bus --object-path /org/a11y/bus --method org.a11y.Bus.GetAddress \
    > "$OUT/a11y-getaddress.txt" 2>&1; then
    a11y_address="$(sed -E "s/^\('(.*)',\)$/\1/" "$OUT/a11y-getaddress.txt")"
    record "dbus.inner.a11y_bus_address" "$a11y_address"
    if timeout 20 gdbus call --address "$a11y_address" --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus \
      --method org.freedesktop.DBus.ListNames > "$OUT/a11y-listnames.txt" 2>&1; then
      record "dbus.inner.a11y_bus_reachable" "yes, names: $(tr '\n' ' ' < "$OUT/a11y-listnames.txt")"
    else
      fail "dbus.inner.a11y_bus_reachable" "$(tr '\n' ' ' < "$OUT/a11y-listnames.txt")"
    fi
  else
    fail "dbus.inner.a11y_bus_address" "$(tr '\n' ' ' < "$OUT/a11y-getaddress.txt")"
  fi
  if timeout 20 gdbus call --session --dest org.a11y.Bus --object-path /org/a11y/bus \
    --method org.freedesktop.DBus.Properties.Get org.a11y.Status IsEnabled > "$OUT/a11y-isenabled.txt" 2>&1; then
    record "dbus.inner.a11y_is_enabled" "$(tr '\n' ' ' < "$OUT/a11y-isenabled.txt")"
  else
    fail "dbus.inner.a11y_is_enabled" "$(tr '\n' ' ' < "$OUT/a11y-isenabled.txt")"
  fi
  if timeout 20 busctl --user list --no-pager > "$OUT/busctl-user-list.txt" 2>&1; then
    record "dbus.inner.busctl_user" "ok ($(wc -l < "$OUT/busctl-user-list.txt") lines in busctl-user-list.txt)"
  else
    fail "dbus.inner.busctl_user" "$(head -c 300 "$OUT/busctl-user-list.txt" | tr '\n' ' ')"
  fi
  exit 0
fi

# ---------------------------------------------------------------------------------------------------------------------------
group "Runner"
record "runner.rid" "$RID"
record "runner.uname" "$(uname -srvm)"
record "runner.os" "$(. /etc/os-release && echo "$PRETTY_NAME")"
record "runner.glibc" "$(ldd --version 2>&1 | head -n 1)"
record "runner.cpus" "$(nproc)"
record "runner.memory" "$(free -m | awk '/^Mem:/ {print $2 " MB total, " $7 " MB available"}')"
record "runner.dotnet" "$(dotnet --version 2>&1)"
record "runner.clang" "$(clang --version 2>&1 | head -n 1 || echo none)"
record "runner.gcc" "$(gcc --version 2>&1 | head -n 1 || echo none)"
record "runner.ld" "$(ld --version 2>&1 | head -n 1 || echo none); lld: $(command -v ld.lld || echo none)"
record "runner.preinstalled.mesa-vulkan-drivers" "$(package_state mesa-vulkan-drivers)"
record "runner.preinstalled.libvulkan1" "$(package_state libvulkan1)"
record "runner.preinstalled.libegl1" "$(package_state libegl1)"
record "runner.preinstalled.weston" "$(package_state weston)"
record "runner.preinstalled.xvfb" "$(package_state xvfb)"
record "runner.preinstalled.fontconfig" "$(package_state fontconfig)"
record "runner.preinstalled.at-spi2-core" "$(package_state at-spi2-core)"
record "runner.vulkan_icds" "$(ls /usr/share/vulkan/icd.d /etc/vulkan/icd.d 2> /dev/null | tr '\n' ' ')"
endgroup

# ---------------------------------------------------------------------------------------------------------------------------
group "Build tools"
capture "apt.update" 300 apt-update.log sudo apt-get update
apt_install build clang lld zlib1g-dev binutils file unzip curl
endgroup

# ---------------------------------------------------------------------------------------------------------------------------
group "wgpu-native archive"
WGPU_ZIP="wgpu-linux-$WGPU_ARCH-release.zip"
WGPU_URL="https://github.com/gfx-rs/wgpu-native/releases/download/$WGPU_NATIVE_TAG/$WGPU_ZIP"
WGPU_DIR="$WORK/wgpu"
record "wgpu.archive.url" "$WGPU_URL"
if curl -fsSL --retry 3 --retry-delay 5 -o "$WORK/$WGPU_ZIP" "$WGPU_URL"; then
  actual="$(sha256sum "$WORK/$WGPU_ZIP" | cut -d' ' -f1)"
  if [ "$actual" = "$WGPU_NATIVE_SHA256" ]; then
    record "wgpu.archive.sha256" "$actual (matches the pin)"
    rm -rf "$WGPU_DIR" && mkdir -p "$WGPU_DIR" && unzip -q "$WORK/$WGPU_ZIP" -d "$WGPU_DIR"
    record "wgpu.archive.contents" "$(cd "$WGPU_DIR" && find . -type f -printf '%p=%s ' | sort)"
    record "wgpu.archive.git_tag" "$(cat "$WGPU_DIR/wgpu-native-meta/wgpu-native-git-tag" 2> /dev/null || echo unknown)"
  else
    fail "wgpu.archive.sha256" "got $actual, pinned $WGPU_NATIVE_SHA256"
  fi
else
  fail "wgpu.archive.download" "curl could not download $WGPU_URL"
fi
endgroup

# ---------------------------------------------------------------------------------------------------------------------------
group "Shim and archive"
ARCHIVE="$WORK/libxylem_probe.a"
if [ -f "$WGPU_DIR/lib/libwgpu_native.a" ]; then
  if gcc -std=gnu17 -O2 -fPIC -Wall -Wextra -I"$WGPU_DIR/include" -c "$PROJECT/native/wgpu_probe.c" -o "$WORK/wgpu_probe.o" \
    > "$OUT/shim-compile.log" 2>&1; then
    cat "$OUT/shim-compile.log"
    record "shim.compile" "ok ($(grep -c warning "$OUT/shim-compile.log") warnings)"
    # One archive with wgpu-native's objects and the shim's: the linker then resolves the shim's references to wgpu-native
    # whatever order it is given the NativeLibrary items in.
    cp "$WGPU_DIR/lib/libwgpu_native.a" "$ARCHIVE" && ar rs "$ARCHIVE" "$WORK/wgpu_probe.o" \
      && record "shim.archive" "$(stat -c %s "$ARCHIVE") bytes (libwgpu_native.a $(stat -c %s "$WGPU_DIR/lib/libwgpu_native.a") bytes + wgpu_probe.o)" \
      || fail "shim.archive" "ar failed"
  else
    cat "$OUT/shim-compile.log"
    fail "shim.compile" "gcc failed (shim-compile.log)"
  fi
else
  fail "shim.compile" "no libwgpu_native.a"
fi
endgroup

# ---------------------------------------------------------------------------------------------------------------------------
# Question 1: the NativeAOT baseline and the same executable with wgpu-native linked statically.

publish() { # publish <name> <output folder> <extra msbuild arguments...>
  local name="$1" folder="$2"
  shift 2
  rm -rf "$PROJECT/bin" "$PROJECT/obj" "$folder"
  local start=$SECONDS
  timeout 900 dotnet publish "$PROJECT/LinuxProbe.csproj" -c Release -r "$RID" -o "$folder" -nologo -v:n "$@" > "$OUT/publish-$name.log" 2>&1
  local code=$?
  tail -n 40 "$OUT/publish-$name.log"
  record "$name.publish_seconds" "$((SECONDS - start))"
  record "$name.warnings" "$(grep -oE 'warning (IL|CS)[0-9]+' "$OUT/publish-$name.log" | sort | uniq -c | tr -s ' \n' ' ' || true) (IL warnings: $(grep -cE 'warning IL[0-9]+' "$OUT/publish-$name.log"))"
  if [ $code -eq 0 ]; then
    record "$name.publish" "ok"
  else
    fail "$name.publish" "exit $code; first errors: $(grep -E 'error|undefined reference|multiple definition' "$OUT/publish-$name.log" | head -n 8 | tr '\n' ' ')"
  fi
  return $code
}

group "Publish: NativeAOT baseline"
BASE_DIR="$WORK/publish-base"
publish aot.base "$BASE_DIR" -p:ProbeLinkWgpu=false
elf_facts aot.base "$BASE_DIR/LinuxProbe"
probe_run aot-base 60 "$BASE_DIR/LinuxProbe" aot base
endgroup

group "Publish: NativeAOT with libwgpu_native.a"
WGPU_EXE_DIR="$WORK/publish-wgpu"
if [ -f "$ARCHIVE" ]; then
  if ! publish aot.wgpu "$WGPU_EXE_DIR" -p:ProbeLinkWgpu=true "-p:ProbeNativeArchive=$ARCHIVE"; then
    if grep -q 'multiple definition' "$OUT/publish-aot.wgpu.log"; then
      publish aot.wgpu_allow_multiple_definition "$WGPU_EXE_DIR" -p:ProbeLinkWgpu=true "-p:ProbeNativeArchive=$ARCHIVE" \
        -p:ProbeAllowMultipleDefinition=true
    fi
  fi
  # The link line NativeAOT ran (printed at -v:n), for the order of the libraries and the system libraries it passed.
  grep -E 'LinuxProbe\.o' "$OUT/publish-aot.wgpu.log" | grep -E -- ' -o ' | tail -n 1 > "$OUT/link-line-wgpu.txt" || true
  record "aot.wgpu.link_libraries" "$(grep -oE -- '(-l[A-Za-z0-9_+.-]+|[^ ]+\.a)( |$)' "$OUT/link-line-wgpu.txt" | tr -s ' \n' ' ')"
  elf_facts aot.wgpu "$WGPU_EXE_DIR/LinuxProbe"
  if [ -f "$BASE_DIR/LinuxProbe" ] && [ -f "$WGPU_EXE_DIR/LinuxProbe" ]; then
    record "aot.wgpu.size_growth" "$(($(stat -c %s "$WGPU_EXE_DIR/LinuxProbe") - $(stat -c %s "$BASE_DIR/LinuxProbe"))) bytes"
  fi
  probe_run aot-wgpu 60 "$WGPU_EXE_DIR/LinuxProbe" aot wgpu
else
  fail "aot.wgpu.publish" "skipped: no archive"
fi
endgroup
PROBE_EXE="$WGPU_EXE_DIR/LinuxProbe"
[ -x "$PROBE_EXE" ] || PROBE_EXE="$BASE_DIR/LinuxProbe"
record "probe.executable" "$PROBE_EXE"
export PROBE_EXE

# ---------------------------------------------------------------------------------------------------------------------------
# Question 2: wgpu on the bare runner first (what exists before anything is installed), then with Mesa's lavapipe.

group "wgpu on the bare runner"
probe_run wgpu-bare 120 "$PROBE_EXE" wgpu bare all
endgroup

group "Packages for the rest"
apt_install graphics mesa-vulkan-drivers libvulkan1 vulkan-tools libegl1 libegl-mesa0 libgles2 mesa-utils
apt_install display weston xvfb wayland-utils libwayland-client0 libx11-6 libxcb1 xauth
apt_install fonts fontconfig fonts-noto-cjk
apt_install dbus dbus dbus-user-session at-spi2-core libglib2.0-bin
apt_install sway sway
record "packages.mesa-vulkan-drivers" "$(package_state mesa-vulkan-drivers)"
record "packages.weston" "$(package_state weston)"
record "packages.sway" "$(package_state sway)"
record "packages.fontconfig" "$(package_state fontconfig)"
record "packages.at-spi2-core" "$(package_state at-spi2-core)"
endgroup

group "Vulkan"
record "vulkan.icds" "$(ls /usr/share/vulkan/icd.d /etc/vulkan/icd.d 2> /dev/null | tr '\n' ' ')"
if timeout 60 vulkaninfo --summary > "$OUT/vulkaninfo-summary.txt" 2>&1; then
  record "vulkan.devices" "$(grep -E 'deviceName|deviceType|driverName|apiVersion' "$OUT/vulkaninfo-summary.txt" | tr -s ' \n' ' ')"
else
  fail "vulkan.devices" "vulkaninfo failed: $(tail -n 5 "$OUT/vulkaninfo-summary.txt" | tr '\n' ' ')"
fi
endgroup

group "wgpu with lavapipe"
probe_run wgpu-lavapipe 120 "$PROBE_EXE" wgpu lavapipe all
probe_run wgpu-vulkan-only 120 "$PROBE_EXE" wgpu vulkan_only vulkan
probe_run wgpu-gl-only 120 "$PROBE_EXE" wgpu gl_only gl
endgroup

# ---------------------------------------------------------------------------------------------------------------------------
# Question 4: fontconfig, from the command line and from the NativeAOT process.

group "fontconfig"
record "fontconfig.cli.sans" "$(fc-match sans 2>&1)"
record "fontconfig.cli.lang_ja" "$(fc-match :lang=ja 2>&1)"
record "fontconfig.cli.font_count" "$(fc-list 2> /dev/null | wc -l)"
probe_run fontconfig 120 "$PROBE_EXE" fontconfig
endgroup

# ---------------------------------------------------------------------------------------------------------------------------
# Question 3: display servers. First what the runner has on its own, then weston (headless), sway (headless), Xvfb.

export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-$WORK/xdg-runtime}"
mkdir -p "$XDG_RUNTIME_DIR" && chmod 700 "$XDG_RUNTIME_DIR"

group "Display servers on the bare runner"
record "display.preexisting.env" "WAYLAND_DISPLAY=${WAYLAND_DISPLAY:-unset} DISPLAY=${DISPLAY:-unset} XDG_SESSION_TYPE=${XDG_SESSION_TYPE:-unset} XDG_RUNTIME_DIR=$XDG_RUNTIME_DIR"
record "display.preexisting.processes" "$(pgrep -a -f 'weston|Xorg|Xvfb|Xwayland|sway|mutter|kwin|gnome-shell' | tr '\n' ';' || true)"
record "display.preexisting.x11_sockets" "$(ls /tmp/.X11-unix 2> /dev/null | tr '\n' ' ')"
record "display.preexisting.wayland_sockets" "$(ls "$XDG_RUNTIME_DIR" 2> /dev/null | grep -E '^wayland-' | tr '\n' ' ')"
timeout 30 env -u WAYLAND_DISPLAY -u DISPLAY "$PROBE_EXE" wayland none > "$OUT/run-wayland-none.log" 2>&1
cat "$OUT/run-wayland-none.log" && grep '^PROBE ' "$OUT/run-wayland-none.log" >> "$SUMMARY"
timeout 30 env -u WAYLAND_DISPLAY -u DISPLAY "$PROBE_EXE" x11 none > "$OUT/run-x11-none.log" 2>&1
cat "$OUT/run-x11-none.log" && grep '^PROBE ' "$OUT/run-x11-none.log" >> "$SUMMARY"
endgroup

group "weston (headless)"
if command -v weston > /dev/null; then
  record "weston.version" "$(weston --version 2>&1 | head -n 1)"
  WESTON_PID=""
  # Weston 10 and later take --backend=headless; --renderer=pixman needs a newer weston, so it is tried first and dropped if
  # weston does not start with it.
  for renderer in "--renderer=pixman" ""; do
    rm -f "$XDG_RUNTIME_DIR/wayland-probe-weston"
    # shellcheck disable=SC2086
    weston --backend=headless $renderer --socket=wayland-probe-weston --idle-time=0 --log="$OUT/weston.log" > "$OUT/weston-stdout.log" 2>&1 &
    WESTON_PID=$!
    if wait_for_path "$XDG_RUNTIME_DIR/wayland-probe-weston" 15; then
      record "weston.start" "ok (pid $WESTON_PID, options: --backend=headless ${renderer:-(default renderer)})"
      break
    fi
    stop_pid "$WESTON_PID"
    WESTON_PID=""
    fail "weston.start${renderer:+.pixman}" "no socket after 15 s: $(tail -n 5 "$OUT/weston.log" "$OUT/weston-stdout.log" 2> /dev/null | tr '\n' ' ')"
  done
  if [ -n "$WESTON_PID" ]; then
    WAYLAND_DISPLAY=wayland-probe-weston probe_run wayland-weston 60 "$PROBE_EXE" wayland weston
    WAYLAND_DISPLAY=wayland-probe-weston wayland_globals weston
    stop_pid "$WESTON_PID"
  fi
else
  fail "weston.start" "weston not installed"
fi
endgroup

group "sway (headless; implements text-input-v3, unlike weston)"
if command -v sway > /dev/null; then
  record "sway.version" "$(sway --version 2>&1 | head -n 1)"
  before="$(ls "$XDG_RUNTIME_DIR" | grep -E '^wayland-[0-9]+$' | sort)"
  WLR_BACKENDS=headless WLR_RENDERER=pixman WLR_LIBINPUT_NO_DEVICES=1 sway -c /dev/null -d > "$OUT/sway.log" 2>&1 &
  SWAY_PID=$!
  socket=""
  for ((i = 0; i < 150; i++)); do
    socket="$(comm -13 <(echo "$before") <(ls "$XDG_RUNTIME_DIR" | grep -E '^wayland-[0-9]+$' | sort) | head -n 1)"
    [ -n "$socket" ] && break
    sleep 0.1
  done
  if [ -n "$socket" ]; then
    record "sway.start" "ok (pid $SWAY_PID, socket $socket)"
    WAYLAND_DISPLAY="$socket" probe_run wayland-sway 60 "$PROBE_EXE" wayland sway
    WAYLAND_DISPLAY="$socket" wayland_globals sway
  else
    fail "sway.start" "no socket after 15 s: $(grep -iE 'error|fail' "$OUT/sway.log" | tail -n 5 | tr '\n' ' ')"
  fi
  stop_pid "$SWAY_PID"
else
  fail "sway.start" "sway not installed"
fi
endgroup

group "Xvfb"
if command -v Xvfb > /dev/null; then
  rm -f /tmp/.X11-unix/X99
  Xvfb :99 -screen 0 1280x720x24 -nolisten tcp > "$OUT/xvfb.log" 2>&1 &
  XVFB_PID=$!
  if wait_for_path /tmp/.X11-unix/X99 15; then
    record "xvfb.start" "ok (pid $XVFB_PID, :99)"
    DISPLAY=:99 probe_run x11-xvfb 60 "$PROBE_EXE" x11 xvfb
  else
    fail "xvfb.start" "no socket after 15 s: $(tail -n 5 "$OUT/xvfb.log" | tr '\n' ' ')"
  fi
  stop_pid "$XVFB_PID"
else
  fail "xvfb.start" "Xvfb not installed"
fi
endgroup

# ---------------------------------------------------------------------------------------------------------------------------
# Question 5: D-Bus. The runner's own session bus first, then a private one from dbus-run-session.

group "D-Bus"
record "dbus.runner.session_address" "${DBUS_SESSION_BUS_ADDRESS:-unset}"
record "dbus.runner.system_bus_socket" "$( [ -S /run/dbus/system_bus_socket ] && echo present || echo absent)"
if timeout 20 busctl --user list --no-pager > "$OUT/busctl-user-runner.txt" 2>&1; then
  record "dbus.runner.user_bus" "reachable"
else
  fail "dbus.runner.user_bus" "$(head -c 300 "$OUT/busctl-user-runner.txt" | tr '\n' ' ')"
fi
probe_run env-runner 30 "$PROBE_EXE" env runner
if command -v dbus-run-session > /dev/null; then
  PROBE_DBUS_INNER=1 timeout 120 dbus-run-session -- bash "$0" "$RID" "$OUT"
  code=$?
  [ $code -eq 0 ] && record "dbus.run_session" "ok" || fail "dbus.run_session" "exit $code"
else
  fail "dbus.run_session" "dbus-run-session not installed"
fi
endgroup

# ---------------------------------------------------------------------------------------------------------------------------
group "Summary"
cat "$SUMMARY"
endgroup
failed="$(grep -c ': FAILED' "$SUMMARY" || true)"
echo "::notice title=Linux probe ($RID)::$(wc -l < "$SUMMARY") PROBE lines, $failed FAILED"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "## Linux probe ($RID): $failed FAILED"
    echo '```text'
    cat "$SUMMARY"
    echo '```'
  } >> "$GITHUB_STEP_SUMMARY"
fi
exit 0

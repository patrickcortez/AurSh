#!/bin/sh
# termux-compat.sh — Create glibc-compatibility symlinks for Android Bionic
#
# .NET's linux-arm64 self-contained binaries are linked against glibc shared
# libraries (libpthread.so.0, libdl.so.2, etc.) that don't exist on Android.
# On Bionic, the functionality of these libraries is merged into libc.so,
# libm.so, and the NDK's libc++_shared.so.
#
# This script creates lightweight symlinks in $PREFIX/lib so the dynamic
# linker can resolve the glibc library names to Bionic equivalents.
#
# Usage:  ./termux-compat.sh
# Deps:   Termux environment with $PREFIX set

set -e

info() { echo "[termux-compat] $*"; }
warn() { echo "[termux-compat] WARNING: $*" >&2; }

# Ensure we're on Termux
if [ -z "$PREFIX" ]; then
    if [ -d "/data/data/com.termux/files/usr" ]; then
        PREFIX="/data/data/com.termux/files/usr"
    else
        echo "[termux-compat] Not running on Termux (no \$PREFIX). Skipping." >&2
        exit 0
    fi
fi

LIB_DIR="$PREFIX/lib"

if [ ! -d "$LIB_DIR" ]; then
    echo "[termux-compat] ERROR: $LIB_DIR does not exist." >&2
    exit 1
fi

# ── Find the system libc.so ──────────────────────────────────────
# On ARM64 Android, Bionic's libc lives at /system/lib64/libc.so.
# The linker will resolve symbols from it regardless of which .so
# triggered the DT_NEEDED load — ELF uses a global symbol scope.
BIONIC_LIBC=""
for _candidate in /system/lib64/libc.so /system/lib/libc.so "$LIB_DIR/libc.so"; do
    if [ -f "$_candidate" ]; then
        BIONIC_LIBC="$_candidate"
        break
    fi
done

if [ -z "$BIONIC_LIBC" ]; then
    warn "Could not find Bionic libc.so — symlinks may not resolve correctly."
    BIONIC_LIBC="/system/lib64/libc.so"
fi

BIONIC_LIBM=""
for _candidate in /system/lib64/libm.so /system/lib/libm.so "$LIB_DIR/libm.so"; do
    if [ -f "$_candidate" ]; then
        BIONIC_LIBM="$_candidate"
        break
    fi
done
if [ -z "$BIONIC_LIBM" ]; then
    BIONIC_LIBM="/system/lib64/libm.so"
fi

info "Using Bionic libc: $BIONIC_LIBC"
info "Using Bionic libm: $BIONIC_LIBM"

# ── Create symlinks ──────────────────────────────────────────────
# Each entry: <glibc_name> <bionic_target>
#
# libpthread.so.0  → libc.so   (pthread functions live in Bionic's libc)
# libdl.so.2       → libdl.so  (dlopen/dlsym; merged into libc on API 34+)
# librt.so.1       → libc.so   (clock_gettime etc. are in Bionic's libc)
# libc.so.6        → libc.so   (glibc versioned name → Bionic's libc)
# libm.so.6        → libm.so   (glibc versioned name → Bionic's libm)

create_link() {
    _name="$1"
    _target="$2"
    _path="$LIB_DIR/$_name"

    if [ -e "$_path" ] || [ -L "$_path" ]; then
        info "  $_name already exists, skipping."
        return
    fi

    if [ -f "$_target" ]; then
        ln -sf "$_target" "$_path"
        info "  $_name -> $_target"
    elif [ -f "$LIB_DIR/$(basename "$_target")" ]; then
        ln -sf "$(basename "$_target")" "$_path"
        info "  $_name -> $(basename "$_target") (relative)"
    else
        warn "  $_name: target '$_target' not found, creating anyway."
        ln -sf "$_target" "$_path"
    fi
}

info "Creating glibc-compatibility symlinks in $LIB_DIR..."

create_link "libpthread.so.0"  "$BIONIC_LIBC"
create_link "librt.so.1"       "$BIONIC_LIBC"
create_link "libc.so.6"        "$BIONIC_LIBC"
create_link "libm.so.6"        "$BIONIC_LIBM"

# libdl — on newer Android (API 34+) dl functions moved into libc.
# Termux may already ship libdl.so; we just need the versioned name.
_dl_target="$BIONIC_LIBC"
for _candidate in "$LIB_DIR/libdl.so" /system/lib64/libdl.so /system/lib/libdl.so; do
    if [ -f "$_candidate" ]; then
        _dl_target="$_candidate"
        break
    fi
done
create_link "libdl.so.2" "$_dl_target"

# libgcc_s — .NET may link against it for unwinding. Termux's clang
# provides libunwind, and gcc provides libgcc_s. Check if available.
if [ ! -e "$LIB_DIR/libgcc_s.so.1" ] && [ ! -L "$LIB_DIR/libgcc_s.so.1" ]; then
    # Look for Termux's gcc-provided libgcc_s
    _gcc_lib=$(find "$PREFIX/lib" -name "libgcc_s.so" -type f 2>/dev/null | head -1)
    if [ -n "$_gcc_lib" ]; then
        create_link "libgcc_s.so.1" "$_gcc_lib"
    else
        # Try the system's libgcc if available
        for _candidate in /system/lib64/libgcc.so "$LIB_DIR/libgcc.so"; do
            if [ -f "$_candidate" ]; then
                create_link "libgcc_s.so.1" "$_candidate"
                break
            fi
        done
    fi
fi

# libstdc++ — .NET links against libstdc++.so.6. Termux uses libc++.
# If Termux has libc++_shared.so, symlink libstdc++.so.6 to it.
if [ ! -e "$LIB_DIR/libstdc++.so.6" ] && [ ! -L "$LIB_DIR/libstdc++.so.6" ]; then
    _cxx_target=""
    for _candidate in "$LIB_DIR/libc++_shared.so" "$LIB_DIR/libc++.so" /system/lib64/libc++.so; do
        if [ -f "$_candidate" ]; then
            _cxx_target="$_candidate"
            break
        fi
    done
    if [ -n "$_cxx_target" ]; then
        create_link "libstdc++.so.6" "$_cxx_target"
    else
        warn "  No C++ stdlib found for libstdc++.so.6 — .NET may fail if it needs C++ symbols."
    fi
fi

info "Done. Glibc compatibility symlinks are in place."

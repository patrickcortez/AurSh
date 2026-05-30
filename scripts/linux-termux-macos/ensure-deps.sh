#!/bin/sh
# ensure-deps.sh — Check and install NativeAOT build dependencies
#
# .NET NativeAOT publish requires a C compiler/linker and several native
# development libraries to produce a statically-linked executable. This
# script detects the Linux distribution (or macOS / Termux) and ensures
# every required package is present before the build begins.
#
# Usage:  ./ensure-deps.sh [--auto-install]
#         --auto-install    Install missing packages without prompting.
#                           Without this flag the script lists what's
#                           missing and exits non-zero so the caller
#                           can decide.
#
# Exit codes:
#   0  All dependencies satisfied
#   1  Missing dependencies (printed to stderr)
#   2  Unsupported distro / cannot determine package manager
#
# Deps:   POSIX sh

set -e

# ── Globals ───────────────────────────────────────────────────────

AUTO_INSTALL=0
MISSING_PKGS=""
MISSING_CMDS=""
PKG_MANAGER=""
DISTRO=""
INSTALL_CMD=""

info()  { echo "[ensure-deps] $*"; }
warn()  { echo "[ensure-deps] WARNING: $*" >&2; }
die()   { echo "[ensure-deps] ERROR: $*" >&2; exit 1; }

# ── Parse arguments ───────────────────────────────────────────────

for _arg in "$@"; do
    case "$_arg" in
        --auto-install) AUTO_INSTALL=1 ;;
        *) ;;
    esac
done

# ── OS detection ──────────────────────────────────────────────────

detect_os() {
    _uname=$(uname -s 2>/dev/null || echo "Unknown")

    case "$_uname" in
        Darwin)
            DISTRO="macOS"
            PKG_MANAGER="brew"
            return
            ;;
        Linux)
            ;;
        *)
            die "Unsupported OS: $_uname"
            ;;
    esac

    # Termux check
    if [ -d "/data/data/com.termux" ] || [ -n "$ANDROID_ROOT" ]; then
        DISTRO="Termux"
        PKG_MANAGER="pkg"
        return
    fi

    # Standard Linux — read /etc/os-release
    if [ -f /etc/os-release ]; then
        . /etc/os-release
        _id=$(echo "$ID" | tr '[:upper:]' '[:lower:]')
        _id_like=$(echo "${ID_LIKE:-}" | tr '[:upper:]' '[:lower:]')
    else
        _id=""
        _id_like=""
    fi

    # Map distro ID to a canonical family
    case "$_id" in
        ubuntu|debian|linuxmint|pop|elementary|zorin|kali|raspbian|neon)
            DISTRO="debian"
            PKG_MANAGER="apt"
            ;;
        fedora|rhel|centos|rocky|alma|ol|nobara)
            DISTRO="fedora"
            if command -v dnf >/dev/null 2>&1; then
                PKG_MANAGER="dnf"
            else
                PKG_MANAGER="yum"
            fi
            ;;
        arch|manjaro|endeavouros|garuda|artix|cachyos)
            DISTRO="arch"
            PKG_MANAGER="pacman"
            ;;
        alpine)
            DISTRO="alpine"
            PKG_MANAGER="apk"
            ;;
        opensuse*|sles|suse)
            DISTRO="suse"
            PKG_MANAGER="zypper"
            ;;
        void)
            DISTRO="void"
            PKG_MANAGER="xbps"
            ;;
        gentoo)
            DISTRO="gentoo"
            PKG_MANAGER="emerge"
            ;;
        nixos|nix)
            DISTRO="nix"
            PKG_MANAGER="nix-env"
            ;;
        *)
            # Fallback: check ID_LIKE
            case "$_id_like" in
                *debian*|*ubuntu*)
                    DISTRO="debian"
                    PKG_MANAGER="apt"
                    ;;
                *fedora*|*rhel*)
                    DISTRO="fedora"
                    if command -v dnf >/dev/null 2>&1; then
                        PKG_MANAGER="dnf"
                    else
                        PKG_MANAGER="yum"
                    fi
                    ;;
                *arch*)
                    DISTRO="arch"
                    PKG_MANAGER="pacman"
                    ;;
                *suse*)
                    DISTRO="suse"
                    PKG_MANAGER="zypper"
                    ;;
                *)
                    DISTRO="unknown"
                    PKG_MANAGER=""
                    ;;
            esac
            ;;
    esac
}

# ── Library presence check ────────────────────────────────────────
# We check for headers/pkg-config/ldconfig rather than relying on
# dpkg/rpm queries, because the user might have installed from
# source or via a different mechanism.

has_header() {
    # Returns 0 if the header file exists in any standard include path
    for _dir in /usr/include /usr/local/include /usr/include/x86_64-linux-gnu \
                /usr/include/aarch64-linux-gnu /data/data/com.termux/files/usr/include; do
        if [ -f "$_dir/$1" ]; then
            return 0
        fi
    done
    return 1
}

has_lib() {
    # Returns 0 if the shared or static library can be found
    _name="$1"
    # Check ldconfig cache
    if command -v ldconfig >/dev/null 2>&1; then
        if ldconfig -p 2>/dev/null | grep -q "$_name"; then
            return 0
        fi
    fi
    # Check pkg-config
    _pc_name=$(echo "$_name" | sed 's/^lib//;s/\.so.*//;s/\.a$//')
    if command -v pkg-config >/dev/null 2>&1; then
        if pkg-config --exists "$_pc_name" 2>/dev/null; then
            return 0
        fi
    fi
    # Brute-force check common lib paths
    for _dir in /usr/lib /usr/lib64 /usr/local/lib /usr/local/lib64 \
                /usr/lib/x86_64-linux-gnu /usr/lib/aarch64-linux-gnu \
                /data/data/com.termux/files/usr/lib; do
        for _f in "$_dir/$_name" "$_dir/${_name}".*; do
            if [ -f "$_f" ] 2>/dev/null; then
                return 0
            fi
        done
    done
    return 1
}

has_command() {
    command -v "$1" >/dev/null 2>&1
}

# ── Dependency definitions per distro ─────────────────────────────
# Each check: (description, detection method, package name per distro)

check_compiler() {
    if has_command gcc || has_command clang || has_command cc; then
        info "  C compiler ......... found"
        return 0
    fi
    info "  C compiler ......... MISSING"
    return 1
}

check_zlib() {
    if has_header "zlib.h"; then
        info "  zlib (dev) ......... found"
        return 0
    fi
    info "  zlib (dev) ......... MISSING"
    return 1
}

check_openssl() {
    if has_header "openssl/ssl.h"; then
        info "  OpenSSL (dev) ...... found"
        return 0
    fi
    info "  OpenSSL (dev) ...... MISSING"
    return 1
}

check_krb5() {
    if has_header "krb5.h" || has_header "krb5/krb5.h"; then
        info "  Kerberos (dev) ..... found"
        return 0
    fi
    info "  Kerberos (dev) ..... MISSING"
    return 1
}

check_icu() {
    if has_header "unicode/utypes.h"; then
        info "  ICU (dev) .......... found"
        return 0
    fi
    info "  ICU (dev) .......... MISSING"
    return 1
}

check_libunwind() {
    if has_header "libunwind.h" || has_lib "libunwind"; then
        info "  libunwind .......... found"
        return 0
    fi
    info "  libunwind .......... MISSING"
    return 1
}

check_lld() {
    if has_command lld || has_command ld.lld; then
        info "  lld (linker) ....... found"
        return 0
    fi
    info "  lld (linker) ....... MISSING"
    return 1
}

# ── Package name mapping ─────────────────────────────────────────
# Returns the correct package name for each distro family.

pkg_compiler() {
    case "$DISTRO" in
        debian)  echo "clang" ;;
        fedora)  echo "clang" ;;
        arch)    echo "clang" ;;
        alpine)  echo "clang" ;;
        suse)    echo "clang" ;;
        void)    echo "clang" ;;
        gentoo)  echo "sys-devel/clang" ;;
        Termux)  echo "clang" ;;
        *)       echo "clang" ;;
    esac
}

pkg_zlib() {
    case "$DISTRO" in
        debian)  echo "zlib1g-dev" ;;
        fedora)  echo "zlib-devel" ;;
        arch)    echo "zlib" ;;
        alpine)  echo "zlib-dev" ;;
        suse)    echo "zlib-devel" ;;
        void)    echo "zlib-devel" ;;
        gentoo)  echo "sys-libs/zlib" ;;
        Termux)  echo "zlib" ;;
        *)       echo "zlib-dev" ;;
    esac
}

pkg_openssl() {
    case "$DISTRO" in
        debian)  echo "libssl-dev" ;;
        fedora)  echo "openssl-devel" ;;
        arch)    echo "openssl" ;;
        alpine)  echo "openssl-dev" ;;
        suse)    echo "libopenssl-devel" ;;
        void)    echo "openssl-devel" ;;
        gentoo)  echo "dev-libs/openssl" ;;
        Termux)  echo "openssl" ;;
        *)       echo "openssl-dev" ;;
    esac
}

pkg_krb5() {
    case "$DISTRO" in
        debian)  echo "libkrb5-dev" ;;
        fedora)  echo "krb5-devel" ;;
        arch)    echo "krb5" ;;
        alpine)  echo "krb5-dev" ;;
        suse)    echo "krb5-devel" ;;
        void)    echo "mit-krb5-devel" ;;
        gentoo)  echo "app-crypt/mit-krb5" ;;
        macOS)   echo "" ;;
        Termux)  echo "" ;;
        *)       echo "krb5-dev" ;;
    esac
}

pkg_icu() {
    case "$DISTRO" in
        debian)  echo "libicu-dev" ;;
        fedora)  echo "libicu-devel" ;;
        arch)    echo "icu" ;;
        alpine)  echo "icu-dev" ;;
        suse)    echo "libicu-devel" ;;
        void)    echo "icu-devel" ;;
        gentoo)  echo "dev-libs/icu" ;;
        Termux)  echo "libicu" ;;
        *)       echo "icu-dev" ;;
    esac
}

pkg_libunwind() {
    case "$DISTRO" in
        debian)  echo "libunwind-dev" ;;
        fedora)  echo "libunwind-devel" ;;
        arch)    echo "libunwind" ;;
        alpine)  echo "libunwind-dev" ;;
        suse)    echo "libunwind-devel" ;;
        void)    echo "libunwind-devel" ;;
        gentoo)  echo "sys-libs/libunwind" ;;
        Termux)  echo "libunwind" ;;
        *)       echo "libunwind-dev" ;;
    esac
}

pkg_lld() {
    case "$DISTRO" in
        debian)  echo "lld" ;;
        fedora)  echo "lld" ;;
        arch)    echo "lld" ;;
        alpine)  echo "lld" ;;
        suse)    echo "lld" ;;
        void)    echo "lld" ;;
        gentoo)  echo "sys-devel/lld" ;;
        Termux)  echo "lld" ;;
        *)       echo "lld" ;;
    esac
}

# ── Build the install command ─────────────────────────────────────

build_install_cmd() {
    _pkgs="$1"
    case "$PKG_MANAGER" in
        apt)    INSTALL_CMD="sudo apt-get install -y $_pkgs" ;;
        dnf)    INSTALL_CMD="sudo dnf install -y $_pkgs" ;;
        yum)    INSTALL_CMD="sudo yum install -y $_pkgs" ;;
        pacman) INSTALL_CMD="sudo pacman -S --noconfirm $_pkgs" ;;
        apk)    INSTALL_CMD="sudo apk add $_pkgs" ;;
        zypper) INSTALL_CMD="sudo zypper install -y $_pkgs" ;;
        xbps)   INSTALL_CMD="sudo xbps-install -Sy $_pkgs" ;;
        emerge) INSTALL_CMD="sudo emerge $_pkgs" ;;
        brew)   INSTALL_CMD="brew install $_pkgs" ;;
        pkg)    INSTALL_CMD="pkg install -y $_pkgs" ;;
        *)      INSTALL_CMD="" ;;
    esac
}

# ── macOS: check Xcode CLT ────────────────────────────────────────

check_xcode_clt() {
    if xcode-select -p >/dev/null 2>&1; then
        info "  Xcode CLT .......... found"
        return 0
    fi
    info "  Xcode CLT .......... MISSING"
    return 1
}

# ── macOS: verify SDK sysroot contains expected libs ──────────────
# After confirming CLT is installed, do a quick sanity check that
# the SDK sysroot actually has the headers .NET NativeAOT needs.
# These are always present in a healthy CLT install, so a failure
# here means a corrupt or partial install.

check_macos_sdk() {
    _sdk_root=$(xcrun --show-sdk-path 2>/dev/null || echo "")
    if [ -z "$_sdk_root" ]; then
        warn "Could not determine macOS SDK path (xcrun failed)."
        return 1
    fi

    info "  SDK path ........... $_sdk_root"
    _ok=0

    if [ -f "$_sdk_root/usr/include/zlib.h" ]; then
        info "  zlib ............... found (SDK)"
    else
        info "  zlib ............... MISSING from SDK"
        _ok=1
    fi

    # macOS uses Security.framework, not OpenSSL — no check needed
    info "  Crypto ............. Security.framework (system)"

    # Kerberos via GSS.framework
    info "  Kerberos ........... GSS.framework (system)"

    # ICU — Apple ships libicucore
    if [ -f "/usr/lib/libicucore.dylib" ] || [ -f "$_sdk_root/usr/lib/libicucore.tbd" ]; then
        info "  ICU ................ found (system)"
    else
        info "  ICU ................ MISSING from system"
        _ok=1
    fi

    return $_ok
}

# ── Main ──────────────────────────────────────────────────────────

detect_os
info "Detected: $DISTRO (package manager: ${PKG_MANAGER:-none})"
info ""
info "Checking NativeAOT build dependencies..."

# ── macOS fast path ───────────────────────────────────────────────
# On macOS, NativeAOT only needs Xcode Command Line Tools. The SDK
# bundles clang, the linker, zlib, ICU (libicucore), Kerberos
# (GSS.framework), and Apple's Security framework (replaces OpenSSL).
# There are no separate -dev packages to install, and the headers
# live inside the SDK sysroot — not in /usr/include — so the Linux
# detection functions would false-positive. Handle macOS separately.

if [ "$DISTRO" = "macOS" ]; then
    _macos_ok=0

    if ! check_xcode_clt; then
        _macos_ok=1
    fi

    if ! check_compiler; then
        _macos_ok=1
    fi

    if [ "$_macos_ok" -ne 0 ]; then
        info ""
        info "Xcode Command Line Tools are required for NativeAOT on macOS."
        info "They provide clang, the linker, and all system libraries."
        info ""

        if [ "$AUTO_INSTALL" -eq 1 ]; then
            info "Installing Xcode Command Line Tools..."
            info "  A system dialog may appear — follow the prompts to install."
            xcode-select --install 2>/dev/null || true
            info ""
            info "After installation completes, re-run this script or 'make publish'."
            exit 1
        else
            info "To install, run:"
            info "  xcode-select --install"
            info ""
            exit 1
        fi
    fi

    # CLT present — do a quick SDK sanity check
    if ! check_macos_sdk; then
        warn "Some SDK components appear missing. Try reinstalling CLT:"
        warn "  sudo rm -rf /Library/Developer/CommandLineTools"
        warn "  xcode-select --install"
        exit 1
    fi

    info ""
    info "All NativeAOT build dependencies are satisfied."
    exit 0
fi

# ── Linux / Termux path ──────────────────────────────────────────

_any_missing=0

# -- Compiler --
if ! check_compiler; then
    _any_missing=1
    _pkg=$(pkg_compiler)
    if [ -n "$_pkg" ]; then
        MISSING_PKGS="$MISSING_PKGS $_pkg"
    fi
fi

# -- zlib --
if ! check_zlib; then
    _any_missing=1
    _pkg=$(pkg_zlib)
    if [ -n "$_pkg" ]; then
        MISSING_PKGS="$MISSING_PKGS $_pkg"
    fi
fi

# -- OpenSSL --
if ! check_openssl; then
    _any_missing=1
    _pkg=$(pkg_openssl)
    if [ -n "$_pkg" ]; then
        MISSING_PKGS="$MISSING_PKGS $_pkg"
    fi
fi

# -- Kerberos (not needed on Termux — ships its own) --
_krb_pkg=$(pkg_krb5)
if [ -n "$_krb_pkg" ]; then
    if ! check_krb5; then
        _any_missing=1
        MISSING_PKGS="$MISSING_PKGS $_krb_pkg"
    fi
else
    info "  Kerberos (dev) ..... skipped (not required on $DISTRO)"
fi

# -- ICU --
if ! check_icu; then
    _any_missing=1
    _pkg=$(pkg_icu)
    if [ -n "$_pkg" ]; then
        MISSING_PKGS="$MISSING_PKGS $_pkg"
    fi
fi

# -- libunwind (NativeAOT needs it for exception handling and stack walking) --
if ! check_libunwind; then
    _any_missing=1
    _pkg=$(pkg_libunwind)
    if [ -n "$_pkg" ]; then
        MISSING_PKGS="$MISSING_PKGS $_pkg"
    fi
fi

# -- lld (preferred linker for NativeAOT, much better on arm64 than GNU ld) --
if ! check_lld; then
    _any_missing=1
    _pkg=$(pkg_lld)
    if [ -n "$_pkg" ]; then
        MISSING_PKGS="$MISSING_PKGS $_pkg"
    fi
fi

# ── Results ───────────────────────────────────────────────────────

info ""

if [ "$_any_missing" -eq 0 ]; then
    info "All NativeAOT build dependencies are satisfied."
    exit 0
fi

# Trim leading whitespace from the package list
MISSING_PKGS=$(echo "$MISSING_PKGS" | sed 's/^ *//')

info "Missing packages: $MISSING_PKGS"

if [ -z "$PKG_MANAGER" ]; then
    warn "Could not determine package manager for distro '$DISTRO'."
    warn "Please install the following packages manually:"
    warn "  $MISSING_PKGS"
    exit 2
fi

build_install_cmd "$MISSING_PKGS"

if [ -z "$INSTALL_CMD" ]; then
    warn "Could not build install command for '$PKG_MANAGER'."
    warn "Please install manually: $MISSING_PKGS"
    exit 2
fi

if [ "$AUTO_INSTALL" -eq 1 ]; then
    info "Installing missing packages..."
    info "  $INSTALL_CMD"

    eval "$INSTALL_CMD"
    _rc=$?

    if [ $_rc -eq 0 ]; then
        info "Dependencies installed successfully."
        exit 0
    else
        die "Package installation failed (exit code $_rc)."
    fi
else
    info ""
    info "To install missing dependencies, run:"
    info ""
    info "  $INSTALL_CMD"
    info ""
    info "Or re-run with --auto-install to install automatically:"
    info "  sh scripts/linux-termux-macos/ensure-deps.sh --auto-install"
    info ""
    exit 1
fi

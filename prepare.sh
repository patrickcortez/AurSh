#!/bin/bash
# prepare.sh — One-shot setup for building AurShell from source
#
# Installs the .NET SDK, make, and NativeAOT build dependencies,
# then runs `make install`.
#
# Usage:  ./prepare.sh          (build + install)
#         ./prepare.sh --deps   (only install dependencies, don't build)

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DEPS_ONLY=0

for _arg in "$@"; do
    case "$_arg" in
        --deps) DEPS_ONLY=1 ;;
        *) ;;
    esac
done

echo ""
echo "  AurShell — Automated Setup"
echo "  ─────────────────────────────────────"
echo ""

# ── .NET SDK ──────────────────────────────────────────────────────

echo "Checking for .NET 8.0 SDK..."
if command -v dotnet >/dev/null 2>&1; then
    _dotnet_ver=$(dotnet --version 2>/dev/null || echo "unknown")
    echo "  .NET SDK found: $_dotnet_ver"
else
    echo "  .NET SDK not found. Installing..."

    # Detect package manager for dotnet
    if command -v apt-get >/dev/null 2>&1; then
        sudo apt-get update
        sudo apt-get install -y dotnet-sdk-8.0
    elif command -v dnf >/dev/null 2>&1; then
        sudo dnf install -y dotnet-sdk-8.0
    elif command -v pacman >/dev/null 2>&1; then
        sudo pacman -S --noconfirm dotnet-sdk-8.0
    elif command -v apk >/dev/null 2>&1; then
        sudo apk add dotnet8-sdk
    elif command -v zypper >/dev/null 2>&1; then
        sudo zypper install -y dotnet-sdk-8.0
    elif command -v brew >/dev/null 2>&1; then
        brew install dotnet-sdk
    elif command -v pkg >/dev/null 2>&1; then
        pkg install -y dotnet-sdk-8.0
    else
        echo "  ERROR: Could not determine package manager."
        echo "  Please install the .NET 8.0 SDK manually:"
        echo "    https://dotnet.microsoft.com/download/dotnet/8.0"
        exit 1
    fi

    echo "  .NET SDK installed."
fi

# ── make ──────────────────────────────────────────────────────────

echo "Checking for make..."
if command -v make >/dev/null 2>&1; then
    echo "  make found: $(make --version | head -1)"
else
    echo "  make not found. Installing..."

    if command -v apt-get >/dev/null 2>&1; then
        sudo apt-get install -y make
    elif command -v dnf >/dev/null 2>&1; then
        sudo dnf install -y make
    elif command -v pacman >/dev/null 2>&1; then
        sudo pacman -S --noconfirm make
    elif command -v apk >/dev/null 2>&1; then
        sudo apk add make
    elif command -v zypper >/dev/null 2>&1; then
        sudo zypper install -y make
    elif command -v brew >/dev/null 2>&1; then
        brew install make
    elif command -v pkg >/dev/null 2>&1; then
        pkg install -y make
    else
        echo "  ERROR: Could not determine package manager."
        echo "  Please install 'make' manually."
        exit 1
    fi

    echo "  make installed."
fi

# ── NativeAOT build dependencies ─────────────────────────────────

echo "Checking NativeAOT build dependencies..."
sh "$SCRIPT_DIR/scripts/linux-termux-macos/ensure-deps.sh" --auto-install

if [ "$DEPS_ONLY" -eq 1 ]; then
    echo ""
    echo "  Dependencies installed. Run 'make install' or 'make install-user' to build."
    echo ""
    exit 0
fi

# ── Build and install ─────────────────────────────────────────────

echo ""
echo "Building and installing AurSh..."
sudo make install

echo ""
echo "AurSh has been built and installed."
echo "Run 'aursh' to start."
echo ""

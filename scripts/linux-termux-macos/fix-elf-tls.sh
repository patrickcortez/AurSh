#!/bin/sh
# fix-elf-tls.sh — Patch ELF TLS segment alignment for Android 15+ (Bionic)
#
# Android 15 tightened the dynamic linker to enforce a minimum 64-byte
# alignment on the PT_TLS program header for ARM64 Bionic. .NET 8's
# PublishSingleFile apphost ships with 8-byte alignment, which causes:
#
#   error: "…": executable's TLS segment is underaligned:
#          alignment is 8 (skew 0), needs to be at least 64 for ARM64 Bionic
#
# This script locates the PT_TLS entry in the ELF program header table
# and overwrites its p_align field with 64 (0x40). It only patches when
# the current alignment is less than the required minimum.
#
# Usage:  ./fix-elf-tls.sh <binary> [<binary2> ...]
# Deps:   POSIX sh, dd, od, printf  (all present in Termux by default)

set -e

REQUIRED_ALIGN=64

die() { echo "[fix-elf-tls] ERROR: $*" >&2; exit 1; }
info() { echo "[fix-elf-tls] $*"; }

# ── read_bytes file offset count ──────────────────────────────────
# Read `count` bytes from `file` at `offset` and print as a decimal
# integer (little-endian).
read_bytes() {
    _file="$1"; _off="$2"; _cnt="$3"
    _hex=$(dd if="$_file" bs=1 skip="$_off" count="$_cnt" 2>/dev/null | od -A n -t x1 | tr -d ' \n')
    # Reverse bytes for little-endian → big-endian conversion
    _rev=""
    _i=$(( ${#_hex} - 2 ))
    while [ "$_i" -ge 0 ]; do
        _rev="${_rev}$(echo "$_hex" | cut -c$((_i+1))-$((_i+2)))"
        _i=$((_i - 2))
    done
    printf "%d" "0x${_rev}"
}

# ── write_u64_le file offset value ────────────────────────────────
# Write a 64-bit little-endian integer `value` into `file` at `offset`.
write_u64_le() {
    _file="$1"; _off="$2"; _val="$3"
    # Build 8-byte little-endian sequence
    _bytes=""
    _v=$_val
    _b=0
    while [ $_b -lt 8 ]; do
        _byte=$(( _v & 0xFF ))
        _bytes="${_bytes}$(printf '\\x%02x' "$_byte")"
        _v=$(( _v >> 8 ))
        _b=$((_b + 1))
    done
    # Use printf to generate raw bytes, then dd to overwrite in-place
    printf "$_bytes" | dd of="$_file" bs=1 seek="$_off" count=8 conv=notrunc 2>/dev/null
}

# ── patch_binary file ─────────────────────────────────────────────
patch_binary() {
    _bin="$1"

    [ -f "$_bin" ] || die "File not found: $_bin"

    # Verify ELF magic: 0x7F 'E' 'L' 'F'
    _magic=$(dd if="$_bin" bs=1 count=4 2>/dev/null | od -A n -t x1 | tr -d ' \n')
    [ "$_magic" = "7f454c46" ] || die "Not an ELF file: $_bin"

    # Verify 64-bit ELF (EI_CLASS == 2 at offset 4)
    _class=$(read_bytes "$_bin" 4 1)
    [ "$_class" -eq 2 ] || die "Not a 64-bit ELF: $_bin (class=$_class)"

    # Verify little-endian (EI_DATA == 1 at offset 5)
    _endian=$(read_bytes "$_bin" 5 1)
    [ "$_endian" -eq 1 ] || die "Not little-endian ELF: $_bin (endian=$_endian)"

    # Read program header table offset (e_phoff at offset 0x20, 8 bytes)
    _phoff=$(read_bytes "$_bin" 32 8)

    # Read program header entry size (e_phentsize at offset 0x36, 2 bytes)
    _phentsize=$(read_bytes "$_bin" 54 2)

    # Read number of program headers (e_phnum at offset 0x38, 2 bytes)
    _phnum=$(read_bytes "$_bin" 56 2)

    info "Scanning $_bin: phoff=$_phoff phentsize=$_phentsize phnum=$_phnum"

    # PT_TLS type = 7 in the p_type field (first 4 bytes of each phdr)
    _found=0
    _idx=0
    while [ $_idx -lt "$_phnum" ]; do
        _entry_off=$(( _phoff + _idx * _phentsize ))
        _ptype=$(read_bytes "$_bin" "$_entry_off" 4)

        if [ "$_ptype" -eq 7 ]; then
            _found=1
            # In ELF64, p_align is at offset 48 (0x30) within the phdr entry,
            # and is 8 bytes wide.
            _palign_off=$(( _entry_off + 48 ))
            _cur_align=$(read_bytes "$_bin" "$_palign_off" 8)

            info "Found PT_TLS at phdr[$_idx]: current p_align=$_cur_align"

            if [ "$_cur_align" -lt "$REQUIRED_ALIGN" ]; then
                info "Patching p_align from $_cur_align to $REQUIRED_ALIGN"
                write_u64_le "$_bin" "$_palign_off" "$REQUIRED_ALIGN"

                # Verify the write
                _new_align=$(read_bytes "$_bin" "$_palign_off" 8)
                if [ "$_new_align" -eq "$REQUIRED_ALIGN" ]; then
                    info "Successfully patched: p_align=$_new_align"
                else
                    die "Verification failed: expected $REQUIRED_ALIGN, got $_new_align"
                fi
            else
                info "Alignment already sufficient ($_cur_align >= $REQUIRED_ALIGN), skipping."
            fi
        fi

        _idx=$((_idx + 1))
    done

    if [ "$_found" -eq 0 ]; then
        info "No PT_TLS segment found in $_bin — nothing to patch."
    fi
}

# ── Main ──────────────────────────────────────────────────────────

if [ $# -eq 0 ]; then
    echo "Usage: $0 <elf-binary> [<elf-binary2> ...]"
    echo ""
    echo "Patches the PT_TLS program header alignment in ELF64 LE binaries"
    echo "to satisfy Android 15+ Bionic's 64-byte minimum requirement."
    exit 1
fi

for _f in "$@"; do
    patch_binary "$_f"
done

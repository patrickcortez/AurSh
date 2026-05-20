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
# Uses POSIX octal escapes (\NNN) and writes byte-by-byte to avoid
# null-byte-in-variable issues that break on dash/mksh/ash.
write_u64_le() {
    _file="$1"; _off="$2"; _val="$3"
    _v=$_val
    _b=0
    while [ $_b -lt 8 ]; do
        _byte=$(( _v & 0xFF ))
        _seek=$(( _off + _b ))
        _oct=$(printf '%03o' "$_byte")
        printf "\\${_oct}" | dd of="$_file" bs=1 seek="$_seek" count=1 conv=notrunc 2>/dev/null
        _v=$(( _v >> 8 ))
        _b=$((_b + 1))
    done
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

    # ELF64 Phdr layout (56 bytes):
    #   Offset  Size  Field
    #   0       4     p_type
    #   4       4     p_flags
    #   8       8     p_offset
    #   16      8     p_vaddr
    #   24      8     p_paddr
    #   32      8     p_filesz
    #   40      8     p_memsz
    #   48      8     p_align

    # PT_TLS type = 7
    _found=0
    _idx=0
    while [ $_idx -lt "$_phnum" ]; do
        _entry_off=$(( _phoff + _idx * _phentsize ))
        _ptype=$(read_bytes "$_bin" "$_entry_off" 4)

        if [ "$_ptype" -eq 7 ]; then
            _found=1

            _poffset_off=$(( _entry_off + 8 ))
            _pvaddr_off=$(( _entry_off + 16 ))
            _ppaddr_off=$(( _entry_off + 24 ))
            _pfilesz_off=$(( _entry_off + 32 ))
            _pmemsz_off=$(( _entry_off + 40 ))
            _palign_off=$(( _entry_off + 48 ))

            _cur_align=$(read_bytes "$_bin" "$_palign_off" 8)
            _cur_vaddr=$(read_bytes "$_bin" "$_pvaddr_off" 8)
            _cur_paddr=$(read_bytes "$_bin" "$_ppaddr_off" 8)
            _cur_offset=$(read_bytes "$_bin" "$_poffset_off" 8)
            _cur_filesz=$(read_bytes "$_bin" "$_pfilesz_off" 8)
            _cur_memsz=$(read_bytes "$_bin" "$_pmemsz_off" 8)

            _skew=$(( _cur_vaddr % REQUIRED_ALIGN ))

            info "Found PT_TLS at phdr[$_idx]:"
            info "  p_align=$_cur_align p_vaddr=$_cur_vaddr p_offset=$_cur_offset"
            info "  p_filesz=$_cur_filesz p_memsz=$_cur_memsz skew=$_skew"

            _need_patch=0

            if [ "$_cur_align" -lt "$REQUIRED_ALIGN" ]; then
                _need_patch=1
            fi

            if [ "$_skew" -ne 0 ]; then
                _need_patch=1
            fi

            if [ "$_need_patch" -eq 0 ]; then
                info "TLS segment already correctly aligned (align=$_cur_align, skew=0), skipping."
            else
                # Step 1: Set p_align to REQUIRED_ALIGN
                info "Setting p_align=$REQUIRED_ALIGN"
                write_u64_le "$_bin" "$_palign_off" "$REQUIRED_ALIGN"

                if [ "$_skew" -ne 0 ]; then
                    # Step 2: Align p_vaddr, p_paddr, and p_offset downward to
                    # the nearest REQUIRED_ALIGN boundary, then extend p_filesz
                    # and p_memsz by the same delta so the TLS data range is preserved.
                    _new_vaddr=$(( _cur_vaddr - _skew ))
                    _new_paddr=$(( _cur_paddr - _skew ))
                    _new_offset=$(( _cur_offset - _skew ))
                    _new_filesz=$(( _cur_filesz + _skew ))
                    _new_memsz=$(( _cur_memsz + _skew ))

                    info "Adjusting addresses by -$_skew to fix skew:"
                    info "  p_vaddr=$_cur_vaddr -> $_new_vaddr"
                    info "  p_paddr=$_cur_paddr -> $_new_paddr"
                    info "  p_offset=$_cur_offset -> $_new_offset"
                    info "  p_filesz=$_cur_filesz -> $_new_filesz"
                    info "  p_memsz=$_cur_memsz -> $_new_memsz"

                    write_u64_le "$_bin" "$_pvaddr_off" "$_new_vaddr"
                    write_u64_le "$_bin" "$_ppaddr_off" "$_new_paddr"
                    write_u64_le "$_bin" "$_poffset_off" "$_new_offset"
                    write_u64_le "$_bin" "$_pfilesz_off" "$_new_filesz"
                    write_u64_le "$_bin" "$_pmemsz_off" "$_new_memsz"
                fi

                # Verify
                _ver_align=$(read_bytes "$_bin" "$_palign_off" 8)
                _ver_vaddr=$(read_bytes "$_bin" "$_pvaddr_off" 8)
                _ver_skew=$(( _ver_vaddr % REQUIRED_ALIGN ))

                if [ "$_ver_align" -eq "$REQUIRED_ALIGN" ] && [ "$_ver_skew" -eq 0 ]; then
                    info "Successfully patched: p_align=$_ver_align, skew=0"
                else
                    die "Verification failed: p_align=$_ver_align, skew=$_ver_skew"
                fi
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

#!/usr/bin/env bash
set -euo pipefail
binary=${1:?Usage: check-static-link.sh EXECUTABLE}
readelf -h "$binary" | grep -q 'Advanced Micro Devices X86-64'
dependencies=$(readelf -d "$binary")
if grep -E 'NEEDED.*(libdave|libsodium|libopus|libcoreclr|libhostfxr)' <<< "$dependencies"; then
    echo 'The executable still depends on shared voice libraries or the .NET runtime.' >&2
    exit 1
fi
# StripSymbols moves the full symbol table into the adjacent .dbg file.
symbol_file="$binary"
if [[ -f "$binary.dbg" ]]; then symbol_file="$binary.dbg"; fi
symbols=$(nm --defined-only "$symbol_file")
for symbol in daveEncryptorCreate opus_encode crypto_aead_xchacha20poly1305_ietf_encrypt; do
    if ! grep -Eq "[[:space:]]${symbol}$" <<< "$symbols"; then
        echo "Missing statically linked symbol: $symbol" >&2
        exit 1
    fi
done
printf '%s\n' 'Static linkage verified for libdave, Opus and libsodium.'

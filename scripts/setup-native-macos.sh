#!/usr/bin/env bash
set -euo pipefail

# Avoid mixing Homebrew's OpenSSL headers with BoringSSL.
unset CPATH C_INCLUDE_PATH CPLUS_INCLUDE_PATH LIBRARY_PATH CFLAGS CXXFLAGS LDFLAGS
opus_prefix=$(brew --prefix opus)

repo_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
output="$repo_dir/BopNet/Native/osx-arm64"
work=$(mktemp -d)
cleanup() {
    result=$?
    if [[ $result == 0 ]]; then
        rm -rf "$work"
    else
        echo "Build failed. Files and logs kept at $work" >&2
    fi
}
trap cleanup EXIT

# Use the same libdave version as Linux.
dave_commit=9686fbaea864aa19f0675e486672b6a77811b6a1
git clone --depth 1 --branch v1.2.0/cpp https://github.com/discord/libdave.git "$work/libdave"
test "$(git -C "$work/libdave" rev-parse HEAD)" = "$dave_commit"
# vcpkg needs history to resolve older port versions.
git -C "$work/libdave" submodule update --init --recursive
source_dir="$work/libdave/cpp"
export VCPKG_DEFAULT_BINARY_CACHE="$work/cache"
export VCPKG_DOWNLOADS="$work/downloads"
export VCPKG_MAX_CONCURRENCY=4
mkdir -p "$VCPKG_DEFAULT_BINARY_CACHE" "$VCPKG_DOWNLOADS"
"$source_dir/vcpkg/bootstrap-vcpkg.sh" -disableMetrics
cmake -S "$source_dir" -B "$work/build" -G Ninja \
    -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES=arm64 \
    -DCMAKE_TOOLCHAIN_FILE="$source_dir/vcpkg/scripts/buildsystems/vcpkg.cmake" \
    -DVCPKG_TARGET_TRIPLET=arm64-osx \
    -DVCPKG_MANIFEST_DIR="$source_dir/vcpkg-alts/boringssl" \
    -DBUILD_SHARED_LIBS=ON -DREQUIRE_BORINGSSL=ON \
    -DPERSISTENT_KEYS=OFF -DTESTING=OFF -DINSTALL_VCPKG_LICENSES=ON \
    -DCMAKE_INSTALL_PREFIX="$work/install"
cmake --build "$work/build" --target libdave --parallel 4
cmake --install "$work/build"

mkdir -p "$work/stage/licenses"
cp "$work/install/lib/libdave.dylib" "$work/stage/libdave.dylib"
cp -L "$opus_prefix/lib/libopus.dylib" "$work/stage/libopus.dylib"
cp -R "$work/install/licenses/." "$work/stage/licenses/"
cp "$opus_prefix/COPYING" "$work/stage/licenses/opus"
for library in "$work/stage/"*.dylib; do
    lipo -verify_arch arm64 "$library"
    install_name_tool -id "@rpath/$(basename "$library")" "$library"
    # vcpkg dependencies are static; only system libraries should remain.
    while IFS= read -r dependency; do
        case "$dependency" in
            /usr/lib/*|/System/Library/*) ;;
            *) echo "Unexpected dependency in $library: $dependency" >&2; exit 1 ;;
        esac
    done < <(otool -L "$library" | tail -n +3 | sed 's/^[[:space:]]*//; s/ (compatibility version.*//')
    codesign --force --sign - "$library"
done
printf 'libdave 1.2.0 %s\nOpus %s\n' "$dave_commit" "$(brew list --versions opus)" > "$work/stage/versions.txt"
mkdir -p "$output"
cp -R "$work/stage/." "$output/"
echo "Libraries copied to $output. Rebuild BopNet and run --self-test."

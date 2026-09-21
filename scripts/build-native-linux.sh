#!/usr/bin/env bash
set -euo pipefail

output=${1:?Usage: build-native-linux.sh OUTPUT_DIRECTORY}
mkdir -p "$output/lib" "$output/licenses"
output=$(realpath "$output")
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# libdave 1.2.0.
dave_commit=9686fbaea864aa19f0675e486672b6a77811b6a1
git clone --depth 1 --branch v1.2.0/cpp https://github.com/discord/libdave.git "$work/libdave"
test "$(git -C "$work/libdave" rev-parse HEAD)" = "$dave_commit"
# vcpkg needs history to resolve older port versions.
git -C "$work/libdave" submodule update --init --recursive
source_dir="$work/libdave/cpp"
"$source_dir/vcpkg/bootstrap-vcpkg.sh" -disableMetrics
cmake -S "$source_dir" -B "$work/build" -G Ninja \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
    -DCMAKE_TOOLCHAIN_FILE="$source_dir/vcpkg/scripts/buildsystems/vcpkg.cmake" \
    -DVCPKG_TARGET_TRIPLET=x64-linux \
    -DVCPKG_MANIFEST_DIR="$source_dir/vcpkg-alts/boringssl" \
    -DBUILD_SHARED_LIBS=OFF -DREQUIRE_BORINGSSL=ON \
    -DPERSISTENT_KEYS=OFF -DTESTING=OFF -DINSTALL_VCPKG_LICENSES=ON \
    -DCMAKE_INSTALL_PREFIX="$work/install"
cmake --build "$work/build" --target libdave --parallel 2
cmake --install "$work/build"

# Merge MLS/BoringSSL into libdave and index the combined archive for the linker.
{
    printf 'CREATE %s/lib/libdave.a\n' "$output"
    printf 'ADDLIB %s/lib/libdave.a\n' "$work/install"
    for archive in "$work/build/vcpkg_installed/x64-linux/lib/"*.a; do
        case $(basename "$archive") in libgtest*|libgmock*) continue ;; esac
        printf 'ADDLIB %s\n' "$archive"
    done
    printf 'SAVE\nEND\n'
} | ar -M
ranlib "$output/lib/libdave.a"
cp /usr/lib/x86_64-linux-gnu/libopus.a /usr/lib/x86_64-linux-gnu/libsodium.a "$output/lib/"
cp -R "$work/install/licenses/." "$output/licenses/"
for copyright in "$work/build/vcpkg_installed/x64-linux/share/"*/copyright; do
    package=$(basename "$(dirname "$copyright")")
    cp "$copyright" "$output/licenses/vcpkg-$package"
done
cp /usr/share/doc/libopus-dev/copyright "$output/licenses/opus"
cp /usr/share/doc/libsodium-dev/copyright "$output/licenses/libsodium"
{
    printf 'libdave 1.2.0 %s\n' "$dave_commit"
    dpkg-query -W libopus-dev libsodium-dev
    git -C "$source_dir/vcpkg" rev-parse HEAD
} > "$output/versions.txt"

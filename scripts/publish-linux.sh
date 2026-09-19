#!/usr/bin/env bash
set -euo pipefail
repo_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
command -v docker >/dev/null || { echo 'Docker is required for Linux Native AOT publishing.' >&2; exit 1; }
cd "$repo_dir"
publish_dir=$(mktemp -d)
trap 'rm -rf "$publish_dir"' EXIT
# Both targets share the native build, publish, and offline validation layers.
docker build --platform linux/amd64 -f BopNet/Dockerfile --target artifacts \
    --output "type=local,dest=$publish_dir" .
docker build --platform linux/amd64 -f BopNet/Dockerfile --target runtime -t bopnet-image .
# Replace generated output only after both builds pass. This also removes shared
# libraries left by older non-AOT publishes. Runtime data belongs outside Release.
rm -rf "$repo_dir/BopNet/Release"
mkdir -p "$repo_dir/BopNet/Release"
cp -R "$publish_dir/." "$repo_dir/BopNet/Release/"

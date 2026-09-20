#!/usr/bin/env bash
set -euo pipefail
repo_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
docker_path=$(command -v docker || true)
if [[ ! -f "$docker_path" || ! -x "$docker_path" ]]; then
    echo 'A Docker executable on PATH is required for Linux Native AOT publishing.' >&2
    exit 1
fi
docker info >/dev/null || { echo 'Start Docker with Linux container support before publishing.' >&2; exit 1; }
cd "$repo_dir"
publish_dir=$(mktemp -d)
trap 'rm -rf "$publish_dir"' EXIT
docker build --platform linux/amd64 -f BopNet/Dockerfile --target artifacts \
    --output "type=local,dest=$publish_dir" .
docker build --platform linux/amd64 -f BopNet/Dockerfile --target runtime -t bopnet-image .
bash scripts/check-container.sh bopnet-image
# Runtime data must live outside this generated directory.
rm -rf "$repo_dir/BopNet/Release"
mkdir -p "$repo_dir/BopNet/Release"
cp -R "$publish_dir/." "$repo_dir/BopNet/Release/"

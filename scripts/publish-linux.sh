#!/usr/bin/env bash
set -euo pipefail

repo_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_dir"

publish_dir=$(mktemp -d)
trap 'rm -rf "$publish_dir"' EXIT

docker build --platform linux/amd64 -f BopNet/Dockerfile --target artifacts \
    --output "type=local,dest=$publish_dir" .
docker build --platform linux/amd64 -f BopNet/Dockerfile --target runtime -t bopnet-image .

# Replace the release only after both builds succeed.
rm -rf BopNet/Release
mkdir -p BopNet/Release
cp -R "$publish_dir/." BopNet/Release/

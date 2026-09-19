#!/usr/bin/env bash
set -euo pipefail
image=${1:-bopnet-image}
volume=$(docker volume create)
trap 'docker volume rm "$volume" >/dev/null' EXIT
run=(docker run --rm --platform linux/amd64 --network none
    --mount "type=volume,source=$volume,target=/data")

# The SQLite self-test must also work on the mounted data directory as app.
"${run[@]}" --env TMPDIR=/data "$image" --self-test
"${run[@]}" --entrypoint sh "$image" -ec '
    test "$(id -u)" -ne 0
    test "$PWD" = /data
    test ! -w /app/BopNet
    ffmpeg -version
    ffmpeg -v error -f lavfi -i anullsrc=r=48000:cl=stereo -t 0.1 -f s16le -y /dev/null
    yt-dlp --version
    deno --version
    deno eval "if (1 + 1 !== 2) Deno.exit(1)"
    mkdir tracks
    printf persisted > tracks/smoke-test
'
"${run[@]}" --entrypoint sh "$image" -ec '
    test "$(cat tracks/smoke-test)" = persisted
'

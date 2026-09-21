# BopNet

A Discord music bot using .NET 10, NetCord, FFmpeg, and yt-dlp.

`/play` accepts YouTube watch, share (`youtu.be`), Shorts, live, and embed video
links. Playlist and tracking parameters are ignored; other hosts and malformed
video IDs are rejected before joining voice. Cache filenames use the video ID.
Concurrent downloads use separate temporary files; the first completed download
creates the shared cache entry. Skipping also works while playback is paused.
Existing database rows and cached files are preserved; old entries whose reference
included query parameters may be downloaded again under the normalized ID.

## Build and test

Use the .NET 10 SDK selected by `global.json`.

```sh
dotnet restore BopNet.sln
dotnet build BopNet.sln -c Release --no-restore
dotnet test BopNet.sln -c Release --no-build --no-restore
```

Voice playback also needs
[NetCord's native dependencies](https://netcord.dev/guides/installing-native-dependencies.html).
Managed builds get libsodium from NuGet.

### macOS ARM64 development (Rider or `dotnet run`)

Install Xcode command line tools (`xcode-select --install`) and Homebrew, then run
from the repository root in a native ARM64 shell:

```sh
brew install cmake ninja nasm opus
bash scripts/setup-native-macos.sh
dotnet build BopNet/BopNet.csproj
env -u DYLD_LIBRARY_PATH dotnet run --project BopNet --no-build -- --self-test
```

Setup downloads and builds libdave 1.2.0 with vcpkg/BoringSSL, then copies it and
Opus to `BopNet/Native/osx-arm64/` with their licenses and versions. Run it again
when native dependencies change.

Rebuild before running in Rider. macOS builds and publishes copy the shared
libraries beside the executable; remove any `DYLD_LIBRARY_PATH` override from
Rider. Configure `appsettings.json` and put FFmpeg and yt-dlp on PATH for playback.
The self-test needs no token or network connection.

### Linux release

With Docker running and on PATH, run from the repository root:

```sh
bash scripts/publish-linux.sh
```

This builds `bopnet-image` and replaces `BopNet/Release` after both builds succeed.
Keep configuration and data outside that directory. Builds on Apple Silicon use
slower AMD64 emulation.

Linux releases use Native AOT on Ubuntu 24.04 with libdave 1.2.0, Opus, and
libsodium statically linked. Keep `libe_sqlite3.so` beside the executable.
The .NET runtime is not required. Native licenses and versions are in `licenses/`.

## Run the container

Create a private `appsettings.json` outside the release directory:

```json
{
  "Discord": {
    "Token": "YOUR_BOT_TOKEN"
  }
}
```

```sh
docker volume create bopnet-data
docker run -d --name bopnet --restart unless-stopped --init --platform linux/amd64 \
  --mount type=volume,source=bopnet-data,target=/data \
  --mount "type=bind,source=$PWD/appsettings.json,target=/app/appsettings.json,readonly" \
  bopnet-image
docker logs -f bopnet
```

The container runs as `app` (UID/GID 1654) from `/data`. New named volumes inherit
its ownership. For existing volumes or bind mounts, grant that user write access
to `bot.db` and `tracks/`, and read access to configuration. Mount existing data
at `/data` to retain it; no schema migration is required.

Alternatively, supply `Discord__Token` through a private `--env-file`. Configuration
files are excluded from artifacts. Configuration lives beside the executable;
database and cache paths are relative to the working directory.

## Run standalone

Extract the `BopNet-linux-x64.tar.gz` release on an Ubuntu 24.04-compatible x64 host.
Keep the extracted files together and put `appsettings.json` beside `BopNet`.
Install FFmpeg, Python 3.10+, and the versions of yt-dlp and Deno pinned in
`BopNet/Dockerfile`, making both executables available on `PATH`. The official
yt-dlp Unix executable includes EJS; Deno runs its YouTube challenge scripts.
See [yt-dlp's dependency guide](https://github.com/yt-dlp/yt-dlp/wiki/EJS).

Run as an unprivileged user from a persistent data directory:

```sh
cd /path/to/bopnet-data
/path/to/bopnet-release/BopNet
```

Use a service manager with that user and working directory for unattended operation.

## Validate and release

CI runs unit tests and builds both Linux releases. The Docker build and extracted
standalone archive run the application self-test. `v*` tags publish both release
archives.

Run the self-test without a token or network connection:

```sh
docker run --rm --network none --platform linux/amd64 bopnet-image --self-test
```

The self-test checks SQLite, command registration, Opus, libsodium, and libdave
using a temporary database. Review AOT/trimming warnings before releasing.

Before deploying, test in a Discord server with **Connect** and **Speak** permissions:
play a fresh YouTube track through to completion, replay it from cache, check
pause/resume and skip/stop, then restart and verify persisted tracks still work.
Also skip a paused track with another track queued, and play the same uncached
video in two servers concurrently, skipping it in one while the other continues.

Update yt-dlp and Deno by changing their versions and SHA-256 checksums together
in the Dockerfile, then rebuild and repeat playback validation. Do not update tools
inside running containers. Rebuild periodically with refreshed .NET/Ubuntu base
images and packages for security fixes; OS packages are not snapshot-pinned.

Before upgrading, stop the bot and back up `bot.db` and `tracks/`. Keep the previous
release. To roll back, stop the new instance and run the previous release against
the same data directory. Never run both against it at once. Restore the backup if
the newer version changed the data incompatibly.

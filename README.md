# BopNet

A Discord music bot using .NET 10, NetCord, FFmpeg, and yt-dlp.

## Build and test

```sh
dotnet restore BopNet.sln
dotnet build BopNet.sln -c Release --no-restore
dotnet test BopNet.sln -c Release --no-build --no-restore
```

`global.json` selects a stable .NET 10 SDK. Managed voice playback also needs
[NetCord's native dependencies](https://netcord.dev/guides/installing-native-dependencies.html).
NetCord remains on `1.0.0-beta.21`; libsodium is supplied by NuGet for managed builds.

### macOS ARM64 development (Rider or `dotnet run`)

Normal builds run managed .NET and load shared voice libraries. Static linking is
enabled only for the Linux Native AOT release; it does not apply to Rider's Debug
run or the existing macOS folder publish configuration.

Install Xcode command line tools (`xcode-select --install`, unless Xcode is already
installed) and Homebrew, then run from the repository root:

```sh
brew install cmake ninja nasm opus
bash scripts/setup-native-macos.sh
dotnet build BopNet/BopNet.csproj
env -u DYLD_LIBRARY_PATH dotnet run --project BopNet --no-build -- --self-test
```

Setup needs network access and builds the pinned libdave 1.2.0 source with its
vcpkg/BoringSSL dependencies. It copies ARM64 libdave and Opus into the ignored
`BopNet/Native/osx-arm64/` directory, including licenses and version information.
Repeat setup after changing native dependencies. Ordinary builds do not download
or compile them; they warn if setup is missing.

Rebuild before starting the usual BopNet Rider run configuration. Build and macOS
publish outputs include the shared libraries beside the executable. Remove any
`DYLD_LIBRARY_PATH` override from Rider's environment variables, especially values
containing literal quotes or `$DYLD_LIBRARY_PATH`; no override is needed. The
existing NuGet package supplies libsodium. Keep `appsettings.json` configured as
usual, and ensure FFmpeg and yt-dlp are available on the run configuration's PATH.

The self-test uses no Discord token or network connection. After it passes, test
`/play` in a Discord voice channel to verify end-to-end playback.

### Linux release

Build the Linux x64 releases with Docker, from the repository root:

```sh
bash scripts/publish-linux.sh
```

Docker must be running and executable on `PATH`. The script builds `bopnet-image`
and replaces `BopNet/Release` only after both builds and container checks pass.
Keep configuration and data elsewhere. Apple Silicon uses slower AMD64 emulation;
Linux x64 CI validates releases.

Releases use Native AOT on Ubuntu 24.04, with libdave 1.2.0 (commit
`9686fbaea864aa19f0675e486672b6a77811b6a1`), Opus, and libsodium statically linked.
Keep the bundled SQLite library (`libe_sqlite3.so`) beside the executable. Standard
OS libraries are required; the .NET runtime and shared voice libraries are not.
Native licenses and versions are under `licenses/`.

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

Every PR, main push, and `v*` tag runs tests, native-link checks, and Linux builds.
Container checks cover non-root execution, SQLite writes on a disposable volume,
media tools, and file persistence. CI also tests the extracted standalone archive
without a .NET runtime or shared voice libraries. Tags publish both release archives.

Repeat the offline checks without a token or network connection:

```sh
docker run --rm --network none --platform linux/amd64 bopnet-image --self-test
bash scripts/check-container.sh bopnet-image
```

The self-test covers SQLite persistence, command registration, Opus, libsodium,
and libdave. It creates and removes a temporary database, never opening `bot.db`.
Managed tests cover the original EF-created schema. Review AOT/trimming warnings
before releasing.

Before deploying, test in a Discord server with **Connect** and **Speak** permissions:
play a fresh YouTube track through to completion, replay it from cache, check
pause/resume and skip/stop, then restart and verify persisted tracks still work.

Update yt-dlp and Deno by changing their versions and SHA-256 checksums together
in the Dockerfile, then rebuild and repeat playback validation. Do not update tools
inside running containers. Rebuild periodically with refreshed .NET/Ubuntu base
images and packages for security fixes; OS packages are not snapshot-pinned.

Before upgrading, stop the bot and back up `bot.db` and `tracks/`. Keep the previous
release. To roll back, stop the new instance and run the previous release against
the same data directory. Never run both against it at once. Restore the backup if
the newer version changed the data incompatibly.

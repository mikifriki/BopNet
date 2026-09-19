# BopNet

A Discord music bot using NetCord, FFmpeg, and yt-dlp.

## Linux static build

Linux x64 releases use .NET 10 Native AOT with **libdave, libsodium, and Opus statically linked**, following [NetCord's native dependency guide](https://netcord.dev/guides/installing-native-dependencies.html?tabs=static). No .NET runtime or separate NetCord voice libraries are required by the executable. SQLite ships as `libe_sqlite3.so`; keep it beside the executable. Operating-system libraries, FFmpeg, and yt-dlp remain external dependencies.

Install Docker with Linux container support, then run from the repository root:

```sh
bash scripts/publish-linux.sh
```

This builds a `linux/amd64` image, including when run on an Apple Silicon Mac using Docker's AMD64 emulation. Native AOT cannot compile a Linux executable directly on macOS. The Rider configuration **Publish BopNet to folder Linux** runs the same script (requires Rider's Shell Script support).

The script creates the `bopnet-image` Docker image and replaces the generated `BopNet/Release` directory with the executable, SQLite library, debug symbols, and native licenses. Keep configuration and runtime data outside that output directory. The build needs network access for NuGet, Ubuntu packages, native sources, and yt-dlp. The first native compilation can take several minutes; Docker caches it for subsequent application builds.

The multi-stage build uses Ubuntu Noble throughout. It compiles libdave **1.2.0** from commit `9686fbaea864aa19f0675e486672b6a77811b6a1`, with pinned submodules and the upstream BoringSSL manifest. Its MLS/BoringSSL static archives are merged into the libdave archive. Opus and libsodium static archives come from Noble development packages. The resolved native versions and licenses are included under `licenses` in the publish output.

Both GitHub workflows use this same build. Nothing needs to be copied into the build output manually.

### Run the container

Create `appsettings.json` with your Discord bot token, or supply it as the `Discord__Token` environment variable. Configuration files are excluded from the Docker build and release artifacts.

```json
{
  "Discord": {
    "Token": "YOUR_BOT_TOKEN"
  }
}
```

Run with a persistent working directory for `bot.db` and `tracks`:

```sh
docker volume create bopnet-data
docker run --rm --platform linux/amd64 \
  --mount type=volume,source=bopnet-data,target=/data \
  --mount "type=bind,source=$PWD/appsettings.json,target=/app/appsettings.json,readonly" \
  --workdir /data \
  bopnet-image
```

The configuration file is read from the executable directory. The database and track cache stay relative to the working directory. To retain an existing installation, mount the directory containing its `bot.db` and `tracks` as `/data` instead of using a new volume. The SQLite schema is unchanged; no migration is required.

For a standalone Linux x64 deployment, copy the entire `BopNet/Release` directory to an Ubuntu 24.04-compatible host, install FFmpeg and yt-dlp, and provide configuration. The binary still uses standard system libraries (including the C/C++ runtime); it is not a fully static musl executable.

### Validation

The Docker build verifies linked native symbols and rejects shared voice/.NET runtime dependencies. It then runs the actual published executable in a minimal runtime-deps image without FFmpeg or shared NetCord voice libraries. SQLite persistence, command registration, Opus encoding, libsodium encryption, and libdave initialization must pass before artifacts or the runtime image are produced.

You can repeat the offline check without a token or Discord connection:

```sh
docker run --rm --platform linux/amd64 bopnet-image --self-test
```

The self-test uses and removes a temporary database; it never opens `bot.db`. For live validation, give the bot **Connect** and **Speak** permissions in a test voice channel, then check `/play`, `/pause`, `/resume`, `/skip`, and `/stop`, including a second playback of a cached track. The bot requests the `Guilds` and `GuildVoiceStates` gateway intents.

## Managed development

`global.json` selects SDK 10.0.100 or a newer stable .NET 10 SDK. NetCord packages stay on `1.0.0-beta.21`. Ordinary managed builds and tests remain available:

```sh
dotnet restore BopNet.sln
dotnet build BopNet.sln -c Release --no-restore
dotnet test BopNet.sln -c Release --no-build
```

The database layer uses `Microsoft.Data.Sqlite` directly to avoid EF Core's experimental Native AOT path. Tests create temporary databases and include compatibility with the original EF-created schema.

For local managed voice playback, install the shared libdave and Opus libraries for your platform according to the NetCord guide. The libsodium NuGet package supplies libsodium for managed builds. macOS static publishing is not configured; use Docker for the Linux release build. The existing macOS managed publish configuration remains available.

On a Linux x64 machine where you have prepared the native archives, the equivalent publish command is:

```sh
dotnet publish BopNet/BopNet.csproj -c Release \
  -p:PublishProfile=LinuxStatic \
  -p:NativeLibraryDirectory=/absolute/path/to/native/lib \
  -o BopNet/Release
```

Prefer the Docker script: it prepares all archives and licenses, performs native-link checks, and runs the offline self-test automatically.

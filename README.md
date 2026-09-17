# BopNet The new generation BopBot

BopNet music bot is a new and simple Youtube music streaming bot for Discord using NetCord.

This Bot differs from my previous Music Bot (BopBot) by using C#, requiring no other major frameworks to run and is kept simple to expand on.

# Requirements

## For running the bot
FFmpeg and yt-dlp must be installed and available from the command line. Framework-dependent builds require the .NET 10 runtime; the self-contained publish commands below bundle it.

NetCord voice requires **libdave** for Discord's DAVE encryption and **Opus** for audio encoding. The `libsodium` NuGet package supplies the fallback transport encryption library. Install native libraries for the same architecture as the bot; see [NetCord's native dependency guide](https://netcord.dev/guides/installing-native-dependencies.html).

On macOS, install Opus with `brew install opus` and link `libopus.dylib` into the output directory as shown below. On Ubuntu, install `libopus0` and make the unversioned library name available with `sudo ln -sf /usr/lib/x86_64-linux-gnu/libopus.so.0 /usr/lib/x86_64-linux-gnu/libopus.so` for Linux x64. Install libdave using the instructions below. The Docker image installs these native dependencies automatically.

A Discord Token is also required in the appsettings.json for the bot to function. This can be gotten from the Discord Developer Portal.

## Building the Bot

The .NET 10 SDK is required to build both projects. `global.json` selects SDK 10.0.100 or a newer stable .NET 10 SDK. NetCord packages are kept on the same version (`1.0.0-beta.21`); [NetCord now requires .NET 10](https://netcord.dev/guides/getting-started/installation.html).

From the repository root, restore, build, and run the tests:

```sh
dotnet restore BopNet.sln
dotnet build BopNet.sln -c Release --no-restore
dotnet test BopNet.sln -c Release --no-build
```

The bot can be built by different means. Linux builds go to `BopNet/Release` and macOS builds go to `Release`, relative to the repository root.
If Jetbrains Rider is used, then Publish tasks can be run and two are provided.
* Publish BopNet to folder Linux
* Publish BopNet to folder OSX

The below command is for building for Linux environment
```
dotnet publish BopNet/BopNet.csproj -c Release \
    -r linux-x64 \
    --self-contained true \
    /p:IncludeNativeLibrariesForSelfExtract=true \
    /p:PublishSingleFile=true \
    -o ./BopNet/Release \
    --framework net10.0
```
And for building for OSX - ARM the following can be used
```
dotnet publish BopNet/BopNet.csproj -c Release \
  -r osx-arm64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:PublishReadyToRun=true \
  -o ./Release \
  --framework net10.0
```

## Installing libdave for local runs and published builds

Download the matching archive from [Discord's official libdave releases](https://github.com/discord/libdave/releases/tag/v1.2.0/cpp). Version 1.2.0 is used by the Docker image. Extract `lib/libdave.so` (Linux) or `lib/libdave.dylib` (macOS) next to the executable, and retain the archive's `licenses` directory when distributing the bot.

For a Linux x64 publish:

```sh
curl -fL 'https://github.com/discord/libdave/releases/download/v1.2.0/cpp/libdave-Linux-X64-boringssl.zip' -o /tmp/bopnet-libdave.zip
unzip -jo /tmp/bopnet-libdave.zip lib/libdave.so -d ./BopNet/Release
unzip -o /tmp/bopnet-libdave.zip 'licenses/*' -d ./BopNet/Release
cp appsettings.json ./BopNet/Release/
```

For a macOS ARM64 publish:

```sh
curl -fL 'https://github.com/discord/libdave/releases/download/v1.2.0/cpp/libdave-macOS-ARM64-boringssl.zip' -o /tmp/bopnet-libdave.zip
unzip -jo /tmp/bopnet-libdave.zip lib/libdave.dylib -d ./Release
unzip -o /tmp/bopnet-libdave.zip 'licenses/*' -d ./Release
ln -sf "$(brew --prefix opus)/lib/libopus.dylib" ./Release/libopus.dylib
cp appsettings.json ./Release/
```

For `dotnet run` or Rider debugging, place libdave (and the macOS Opus link) in the build output instead, for example `BopNet/bin/Debug/net10.0`. Repeat this after cleaning the output. Self-contained and single-file publishing do not bundle system-installed native voice libraries automatically.

# How to use?

Once the bot starts up an invitation link will be shown which will allow the bot to be added to a Discord server.

The bot requests the `Guilds` and `GuildVoiceStates` gateway intents. It needs **Connect** and **Speak** permissions in the target voice channel. After upgrading, check `/play`, `/pause`, `/resume`, `/skip`, and `/stop` in a Discord test server to verify native dependencies and voice playback.

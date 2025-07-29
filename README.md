# BopNet The new generation BopBot

BopNet music bot is a new and simple Youtube music streaming bot for Discord using NetCord.

This Bot differs from my previous Music Bot (BopBot) by using C#, requiring no other major frameworks to run and is kept simple to expand on.

# Requirements

## For running the bot
FFmpeg, yt-dlp and the dotnet 8 runtime must be installed on the machine where the bot will be running, and they must be available from the command line.

A Discord Token is also required in the appsettings.json for the bot to function. This can be gotten from the Discord Developer Portal.

## Building the Bot

dotnet 8 is required to build the music bot. But newer versions can be used, although it is not fully tested.

The bot can be built by different means. In all build cases the final product will be put in a Release directory in the project directory.
If Jetbrains Rider is used, then Publish tasks can be run and two are provided.
* Publish BopNet to folder Linux
* Publish BopNet to folder OSX

The below command is for building for Linux environment
```
dotnet publish -c Release \
    -r linux-x64 \
    --self-contained true \
    /p:IncludeNativeLibrariesForSelfExtract=true \
    /p:PublishSingleFile=true \
    -o ./Release \
    --framework net8.0
```
And for building for OSX - ARM the following can be used
```
dotnet publish -c Release \
  -r osx-arm64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:PublishReadyToRun=true \
  -o ./Release \
  --framework net8.0
```
# How to use?

Once the bot starts up an invitation link will be shown which will allow the bot to be added to a Discord server.

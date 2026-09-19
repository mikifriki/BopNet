# syntax=docker/dockerfile:1
# Always build with --platform linux/amd64 and the repository root as context.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS native
ARG TARGETARCH
RUN test "$TARGETARCH" = amd64 || (echo 'BopNet static publishing supports linux/amd64 only' >&2; exit 1)
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential clang cmake ninja-build pkg-config git curl zip unzip tar \
    python3 perl golang-go nasm autoconf automake libtool \
    libopus-dev libsodium-dev zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
COPY scripts/build-native-linux.sh /build-native-linux.sh
RUN bash /build-native-linux.sh /native

FROM native AS publish
WORKDIR /src
COPY global.json ./
COPY BopNet/BopNet.csproj BopNet/
COPY BopNet/Native/ BopNet/Native/
COPY BopNet/Properties/ BopNet/Properties/
RUN dotnet restore BopNet/BopNet.csproj -r linux-x64 -p:PublishProfile=LinuxStatic
COPY BopNet/ BopNet/
RUN dotnet publish BopNet/BopNet.csproj -c Release --no-restore \
    -p:PublishProfile=LinuxStatic -p:NativeLibraryDirectory=/native/lib \
    -o /publish
COPY scripts/check-static-link.sh /check-static-link.sh
RUN bash /check-static-link.sh /publish/BopNet \
    && cp -R /native/licenses /publish/licenses \
    && cp /native/versions.txt /publish/licenses/native-versions.txt

# No .NET runtime, media tools, or shared NetCord voice libraries in this stage.
# Running here also catches a lazy dynamic load that readelf cannot detect.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble AS validate
WORKDIR /app
COPY --from=publish /publish/ ./
RUN ./BopNet --self-test

FROM scratch AS artifacts
COPY --from=validate /app/ /

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg python3 curl ca-certificates \
    && curl -fL https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=validate /app/ ./
RUN ./BopNet --self-test && ffmpeg -version && yt-dlp --version
ENTRYPOINT ["/app/BopNet"]

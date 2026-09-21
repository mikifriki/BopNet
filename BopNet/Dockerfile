# syntax=docker/dockerfile:1
# Build from the repository root with --platform linux/amd64.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS native
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential clang cmake ninja-build pkg-config git curl zip unzip tar \
    python3 perl golang-go nasm autoconf automake libtool \
    libopus-dev libsodium-dev zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
COPY scripts/build-native-linux.sh /build-native-linux.sh
RUN bash /build-native-linux.sh /native

ARG YT_DLP_VERSION=2026.08.19
ARG YT_DLP_SHA256=1fa6733c37ea6fb51c99ad8fe785e7b7e5f3246c9b980230329d4fb72ed8d4d6
ARG DENO_VERSION=2.9.7
ARG DENO_SHA256=c6527f24f4b16031d3ae4fa9f658d5f11534c8d84ce7dc8502420280919c3490
RUN mkdir /tools \
    && curl -fsSL "https://github.com/yt-dlp/yt-dlp/releases/download/${YT_DLP_VERSION}/yt-dlp" -o /tools/yt-dlp \
    && echo "${YT_DLP_SHA256}  /tools/yt-dlp" | sha256sum -c - \
    && curl -fsSL "https://github.com/denoland/deno/releases/download/v${DENO_VERSION}/deno-x86_64-unknown-linux-gnu.zip" -o /tmp/deno.zip \
    && echo "${DENO_SHA256}  /tmp/deno.zip" | sha256sum -c - \
    && unzip /tmp/deno.zip -d /tools \
    && chmod 755 /tools/yt-dlp /tools/deno \
    && rm /tmp/deno.zip

FROM native AS publish
WORKDIR /src
COPY global.json ./
COPY BopNet/ BopNet/
RUN dotnet publish BopNet/BopNet.csproj -c Release \
    -p:PublishProfile=LinuxStatic -p:NativeLibraryDirectory=/native/lib \
    -o /publish
RUN cp -R /native/licenses /publish/licenses \
    && cp /native/versions.txt /publish/licenses/native-versions.txt

# Check native loading without shared voice libraries installed.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble AS validate
WORKDIR /app
COPY --from=publish /publish/ ./
USER $APP_UID
RUN ./BopNet --self-test

FROM scratch AS artifacts
COPY --from=validate /app/ /

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg python3 ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir /data && chown app:app /data
COPY --from=native /tools/ /usr/local/bin/
COPY --from=validate /app/ /app/
WORKDIR /data
USER $APP_UID
ENTRYPOINT ["/app/BopNet"]

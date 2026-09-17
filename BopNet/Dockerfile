FROM mcr.microsoft.com/dotnet/runtime:10.0-noble

ARG LIBDAVE_VERSION=1.2.0
ARG LIBDAVE_SHA256=cd77724d3fa90359f6430c294fcad7b8f7e248ec8e16db9031d7b6f622eb3451

RUN apt-get update && apt-get install -y \
    ffmpeg \
    wget \
    unzip \
    python3 \
    libopus0 \
    libstdc++6 \
    && wget -O /usr/local/bin/yt-dlp https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp \
    && rm -rf /var/lib/apt/lists/* 

# The publish workflows target Linux x64. NetCord voice requires libdave for DAVE encryption.
RUN wget -O /tmp/libdave.zip "https://github.com/discord/libdave/releases/download/v${LIBDAVE_VERSION}/cpp/libdave-Linux-X64-boringssl.zip" \
    && echo "${LIBDAVE_SHA256}  /tmp/libdave.zip" | sha256sum -c - \
    && unzip -j /tmp/libdave.zip lib/libdave.so -d /usr/local/lib \
    && unzip -j /tmp/libdave.zip 'licenses/*' -d /usr/local/share/licenses/libdave \
    && ldconfig \
    && rm /tmp/libdave.zip

WORKDIR /app

# Required symlink for the Bot to find unversioned libopus
RUN ln -s /usr/lib/x86_64-linux-gnu/libopus.so.0 /usr/lib/x86_64-linux-gnu/libopus.so

COPY Release/ .

ENTRYPOINT ["./BopNet"]

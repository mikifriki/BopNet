FROM mcr.microsoft.com/dotnet/runtime:8.0

RUN apt-get update && apt-get install -y \
    ffmpeg \
    wget \
    libopus0 \
    && wget -O /usr/local/bin/yt-dlp https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp \
    && rm -rf /var/lib/apt/lists/* 

WORKDIR /app

# Required symlink for the Bot to find unversioned libopus
RUN ln -s /usr/lib/x86_64-linux-gnu/libopus.so.0 /usr/lib/x86_64-linux-gnu/libopus.so

COPY Release/ .

ENTRYPOINT ["./BopNet"]
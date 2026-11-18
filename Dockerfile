# syntax=docker/dockerfile:1

# 1. Frontend: plain TypeScript, built by Vite into the API's wwwroot.
FROM node:24-alpine AS web
WORKDIR /repo/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# 2. Backend: Native AOT needs clang and zlib headers on top of the SDK.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
ARG TARGETARCH
RUN apt-get update \
 && apt-get install -y --no-install-recommends clang zlib1g-dev \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /repo
COPY src/Bookmarks.Api/Bookmarks.Api.csproj src/Bookmarks.Api/
RUN dotnet restore src/Bookmarks.Api -a $([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64)
COPY src/ src/
COPY --from=web /repo/src/Bookmarks.Api/wwwroot src/Bookmarks.Api/wwwroot
RUN dotnet publish src/Bookmarks.Api -c Release --no-restore -o /app \
      -a $([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64) \
 && rm -f /app/*.dbg \
 && mkdir /data

# 3. Runtime: chiseled image, no shell, runs as the non-root "app" user (uid 1654).
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
WORKDIR /app
COPY --from=api /app ./
COPY --from=api --chown=1654:1654 /data /data
ENV DataPath=/data
VOLUME /data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 CMD ["./Bookmarks.Api", "--healthcheck"]
ENTRYPOINT ["./Bookmarks.Api"]

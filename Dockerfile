ARG ALPINE_VERSION=3.23.3
ARG DOTNET_VERSION=9.0

################################################################################
# Builder stage – build from repository context
################################################################################
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-alpine AS build

ARG TARGETARCH
ARG JACRED_VERSION=dev

# Create output directory
RUN mkdir -p /dist

WORKDIR /src

# Copy repository source (no git clone)
COPY . .

# Restore and publish
RUN set -eu; \
    case "${TARGETARCH}" in \
    amd64) RID=linux-musl-x64 ;; \
    arm)   RID=linux-musl-arm ;; \
    arm64) RID=linux-musl-arm64 ;; \
    *) echo "Unsupported architecture: ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    dotnet restore --verbosity minimal && \
    dotnet publish . \
    --runtime "$RID" \
    --configuration Release \
    --self-contained true \
    --output /dist \
    --verbosity minimal \
    -p:PublishTrimmed=false \
    -p:PublishSingleFile=true \
    -p:DebugType=None \
    -p:EnableCompressionInSingleFile=true \
    -p:OptimizationPreference=Speed \
    -p:SuppressTrimAnalysisWarnings=true \
    -p:IlcOptimizationPreference=Speed \
    -p:IlcFoldIdenticalMethodBodies=true

################################################################################
# Runtime stage
################################################################################
FROM alpine:${ALPINE_VERSION} AS runtime

ARG JACRED_VERSION=dev

LABEL maintainer="Pavel Pikta <devops@pavelpikta.com>" \
    org.opencontainers.image.title="JacRed" \
    org.opencontainers.image.description="Jacred - Torrent tracker aggregator" \
    org.opencontainers.image.revision="${JACRED_VERSION}"

# Install runtime dependencies and create user
RUN set -eux; \
    apk add --no-cache --update \
    ca-certificates \
    curl \
    dumb-init \
    icu-libs \
    libgcc \
    libintl \
    libstdc++ \
    tzdata \
    && apk upgrade --no-cache \
    && rm -rf /var/cache/apk/* /tmp/* /var/tmp/* \
    && rm -rf /usr/share/man/* \
    /usr/share/doc/* \
    /usr/share/info/* \
    /usr/share/locale/* \
    && addgroup -g 1000 -S jacred \
    && adduser -u 1000 -S jacred -G jacred -s /sbin/nologin -h /app \
    && mkdir -p /app/Data /app/Data/fdb /app/Data/temp /app/Data/tracks /app/config \
    && touch /app/Data/temp/stats.json \
    && chown -R jacred:jacred /app \
    && chmod -R 750 /app

WORKDIR /app

# Copy publish output: /dist contains JacRed (binary), wwwroot/, Data/
COPY --from=build --chown=jacred:jacred --chmod=550 /dist/JacRed /app/JacRed
COPY --from=build --chown=jacred:jacred --chmod=550 /dist/wwwroot /app/wwwroot
COPY --from=build --chown=jacred:jacred --chmod=550 /dist/Data /app/Data
# Default config at /app/init.conf (app reads "init.conf" from current directory)
COPY --chown=jacred:jacred --chmod=640 Data/init.conf /app/init.conf
COPY --chown=jacred:jacred --chmod=550 entrypoint.sh /entrypoint.sh

# Environment variables
ENV JACRED_VERSION="${JACRED_VERSION}" \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0 \
    DOTNET_USE_POLLING_FILE_WATCHER=1 \
    ASPNETCORE_URLS=http://0.0.0.0:9117 \
    ASPNETCORE_ENVIRONMENT=Production \
    TZ=UTC \
    UMASK=0027

USER jacred:jacred

VOLUME ["/app/Data", "/app/config"]

EXPOSE 9117/tcp

HEALTHCHECK --interval=30s \
    --timeout=15s \
    --start-period=45s \
    --retries=3 \
    --start-interval=5s \
    CMD curl -f -s --max-time 10 http://127.0.0.1:9117/health || exit 1

ENTRYPOINT ["dumb-init", "--", "/entrypoint.sh"]
CMD ["./JacRed"]

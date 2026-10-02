# syntax=docker/dockerfile:1.7

ARG NODE_IMAGE=node:22-alpine
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim
ARG DOCKER_CLI_IMAGE=docker:29-cli
ARG MEM_PRODUCT_VERSION=0.2.0
ARG MEM_COMMIT_SHA=unknown
ARG BUILD_DATE=unknown
ARG SOURCE_DATE_EPOCH=0

# The migration worker talks to the host Docker daemon through the mounted
# socket. Ship only the Docker client binary in the final Control Plane image.
FROM ${DOCKER_CLI_IMAGE} AS docker-cli
RUN docker --version

FROM ${NODE_IMAGE} AS web-build
WORKDIR /src/Web

COPY src/Web/package.json src/Web/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm \
    npm ci

COPY src/Web/ ./
RUN npm run build

# MEM Migrate is a required Control Plane runtime dependency. Its source is
# supplied as a named BuildKit context named "migrate-source" so the normal
# installer build context remains narrow and keeps installer/.dockerignore.
FROM ${NODE_IMAGE} AS migrate-web-build
ARG MEM_PRODUCT_VERSION
WORKDIR /src/Mem.Migrate.Web/ClientApp

COPY --from=migrate-source src/Mem.Migrate.Web/ClientApp/package.json \
    src/Mem.Migrate.Web/ClientApp/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm \
    npm ci --no-audit --no-fund

COPY --from=migrate-source src/Mem.Migrate.Web/ClientApp/ ./
RUN MEM_MIGRATE_RELEASE_VERSION="${MEM_PRODUCT_VERSION}" npm run build

FROM ${DOTNET_SDK_IMAGE} AS migrate-build
ARG MEM_PRODUCT_VERSION
ARG MEM_COMMIT_SHA
ARG BUILD_DATE
ARG SOURCE_DATE_EPOCH
WORKDIR /src/Migrate

COPY --from=migrate-source Directory.Build.props Directory.Packages.props global.json ./
COPY --from=migrate-source src/ ./src/
RUN rm -rf ./src/Mem.Migrate.Web/wwwroot
COPY --from=migrate-web-build /src/Mem.Migrate.Web/wwwroot/ ./src/Mem.Migrate.Web/wwwroot/
RUN test -f ./src/Mem.Migrate.Web/wwwroot/index.html \
    && grep -RIql -- "${MEM_PRODUCT_VERSION}" ./src/Mem.Migrate.Web/wwwroot \
    && { [ "${MEM_PRODUCT_VERSION}" = "0.2.0-alpha.1" ] \
         || ! grep -RIql -- '0.2.0-alpha.1' ./src/Mem.Migrate.Web/wwwroot; }

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/Mem.Migrate.Cli/Mem.Migrate.Cli.csproj --runtime linux-x64 \
    && dotnet restore src/Mem.Migrate.Web/Mem.Migrate.Web.csproj --runtime linux-x64

RUN --mount=type=cache,target=/root/.nuget/packages \
    set -eux; \
    version_core="${MEM_PRODUCT_VERSION%%-*}"; \
    assembly_version="${version_core}.0"; \
    SOURCE_DATE_EPOCH="${SOURCE_DATE_EPOCH}" \
    dotnet publish src/Mem.Migrate.Cli/Mem.Migrate.Cli.csproj \
      --configuration Release \
      --runtime linux-x64 \
      --self-contained true \
      --no-restore \
      --output /out/cli \
      -p:Version="${MEM_PRODUCT_VERSION}" \
      -p:AssemblyVersion="${assembly_version}" \
      -p:FileVersion="${assembly_version}" \
      -p:InformationalVersion="${MEM_PRODUCT_VERSION}" \
      -p:IncludeSourceRevisionInInformationalVersion=false \
      -p:ContinuousIntegrationBuild=true \
      -p:Deterministic=true; \
    SOURCE_DATE_EPOCH="${SOURCE_DATE_EPOCH}" \
    dotnet publish src/Mem.Migrate.Web/Mem.Migrate.Web.csproj \
      --configuration Release \
      --runtime linux-x64 \
      --self-contained true \
      --no-restore \
      --output /out/web \
      -p:Version="${MEM_PRODUCT_VERSION}" \
      -p:AssemblyVersion="${assembly_version}" \
      -p:FileVersion="${assembly_version}" \
      -p:InformationalVersion="${MEM_PRODUCT_VERSION}" \
      -p:IncludeSourceRevisionInInformationalVersion=false \
      -p:ContinuousIntegrationBuild=true \
      -p:Deterministic=true

RUN set -eux; \
    mkdir -p /out/combined; \
    cp -a /out/cli/. /out/combined/; \
    find /out/web -mindepth 1 -print | LC_ALL=C sort | \
      while IFS= read -r source_path; do \
        relative_path="${source_path#/out/web/}"; \
        destination_path="/out/combined/${relative_path}"; \
        if [ -e "${destination_path}" ]; then \
          if [ -d "${source_path}" ] && [ -d "${destination_path}" ]; then \
            continue; \
          fi; \
          if [ -f "${source_path}" ] && [ -f "${destination_path}" ] \
             && cmp -s "${source_path}" "${destination_path}"; then \
            continue; \
          fi; \
          echo "MEM Migrate publish payload collision: ${relative_path}" >&2; \
          exit 1; \
        fi; \
        if [ -d "${source_path}" ]; then \
          mkdir -p "${destination_path}"; \
        else \
          mkdir -p "$(dirname "${destination_path}")"; \
          cp -a "${source_path}" "${destination_path}"; \
        fi; \
      done; \
    printf '%s\n' "${MEM_PRODUCT_VERSION}" > /out/combined/VERSION; \
    printf '{\n  "schemaVersion": 1,\n  "product": "MEM Migrate",\n  "productVersion": "%s",\n  "sourceAssistantVersion": "%s",\n  "releaseVersion": "%s",\n  "publishedAtUtc": "%s",\n  "runtime": "linux-x64",\n  "configuration": "Release",\n  "selfContained": true,\n  "sourceCommit": "%s",\n  "distribution": "embedded-control-plane"\n}\n' \
      "${MEM_PRODUCT_VERSION}" \
      "${MEM_PRODUCT_VERSION}" \
      "${MEM_PRODUCT_VERSION}" \
      "${BUILD_DATE}" \
      "${MEM_COMMIT_SHA}" \
      > /out/combined/BUILD-INFO.json; \
    test -x /out/combined/mem-migrate; \
    test -x /out/combined/mem-migrate-web; \
    test -f /out/combined/libe_sqlite3.so; \
    test -f /out/combined/wwwroot/index.html; \
    test "$(cat /out/combined/VERSION)" = "${MEM_PRODUCT_VERSION}"; \
    grep -Fq '"releaseVersion": "'"${MEM_PRODUCT_VERSION}"'"' /out/combined/BUILD-INFO.json; \
    grep -Fq '"sourceCommit": "'"${MEM_COMMIT_SHA}"'"' /out/combined/BUILD-INFO.json; \
    env -i PATH=/usr/bin:/bin HOME=/root /out/combined/mem-migrate version >/dev/null; \
    env -i PATH=/usr/bin:/bin HOME=/root /out/combined/mem-migrate-web --version >/dev/null; \
    env -i PATH=/usr/bin:/bin HOME=/root \
      /out/combined/mem-migrate worker convert --help 2>&1 \
      | grep -q 'Conversion worker options'

FROM ${DOTNET_SDK_IMAGE} AS api-build
ARG MEM_PRODUCT_VERSION
ARG SOURCE_DATE_EPOCH
WORKDIR /src

COPY src/ ./
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore Api/Api.csproj
RUN --mount=type=cache,target=/root/.nuget/packages \
    set -eux; \
    version_core="${MEM_PRODUCT_VERSION%%-*}"; \
    assembly_version="${version_core}.0"; \
    SOURCE_DATE_EPOCH="${SOURCE_DATE_EPOCH}" \
    dotnet publish Api/Api.csproj \
      --configuration Release \
      --no-restore \
      --output /out \
      /p:UseAppHost=false \
      -p:Version="${MEM_PRODUCT_VERSION}" \
      -p:AssemblyVersion="${assembly_version}" \
      -p:FileVersion="${assembly_version}" \
      -p:InformationalVersion="${MEM_PRODUCT_VERSION}" \
      -p:IncludeSourceRevisionInInformationalVersion=false \
      -p:ContinuousIntegrationBuild=true \
      -p:Deterministic=true

FROM ${DOTNET_RUNTIME_IMAGE} AS runtime
ARG MEM_PRODUCT_VERSION
ARG MEM_COMMIT_SHA
ARG BUILD_DATE

LABEL org.opencontainers.image.title="MEM Control Plane" \
      org.opencontainers.image.description="Message Easy Mode Control Plane" \
      org.opencontainers.image.source="https://github.com/message-easy-mode/mem" \
      org.opencontainers.image.version="${MEM_PRODUCT_VERSION}" \
      org.opencontainers.image.revision="${MEM_COMMIT_SHA}" \
      org.opencontainers.image.created="${BUILD_DATE}"

WORKDIR /app

RUN apt-get update \
    && apt-get install --yes --no-install-recommends age ca-certificates curl tzdata \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data /opt/mem/migrate/current \
    && ln -s /data /app/data

ENV ASPNETCORE_URLS=https://0.0.0.0:8443 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_EnableDiagnostics=0 \
    MEM_PRODUCT_VERSION=${MEM_PRODUCT_VERSION} \
    MEM_COMMIT_SHA=${MEM_COMMIT_SHA}

COPY --from=api-build /out/ ./
COPY --from=web-build /src/Web/dist/ ./wwwroot/
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=migrate-build /out/combined/ /opt/mem/migrate/current/

RUN chmod 0755 \
      /opt/mem/migrate/current/mem-migrate \
      /opt/mem/migrate/current/mem-migrate-web \
    && chmod 0644 /opt/mem/migrate/current/libe_sqlite3.so \
    && age --version >/dev/null \
    && age-keygen --version >/dev/null \
    && docker --version >/dev/null \
    && test "$(cat /opt/mem/migrate/current/VERSION)" = "${MEM_PRODUCT_VERSION}" \
    && grep -Fq '"releaseVersion": "'"${MEM_PRODUCT_VERSION}"'"' /opt/mem/migrate/current/BUILD-INFO.json \
    && grep -Fq '"sourceCommit": "'"${MEM_COMMIT_SHA}"'"' /opt/mem/migrate/current/BUILD-INFO.json \
    && /opt/mem/migrate/current/mem-migrate version >/dev/null \
    && /opt/mem/migrate/current/mem-migrate-web --version >/dev/null \
    && /opt/mem/migrate/current/mem-migrate worker convert --help 2>&1 \
      | grep -q 'Conversion worker options'

EXPOSE 8443
VOLUME ["/data"]
STOPSIGNAL SIGTERM

HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=6 \
  CMD curl --insecure --fail --silent --show-error https://127.0.0.1:8443/health/ready || exit 1

ENTRYPOINT ["dotnet", "Api.dll"]

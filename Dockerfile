# ─────────────────────────────────────────────────────────────────────────────
# Stage 1: frontend-build — compiles the Astro site to static files.
#
# astro.config.ts points outDir at '../src/InvoiceProcessor.Worker/wwwroot', so
# building from /src/frontend lands the output at
# /src/src/InvoiceProcessor.Worker/wwwroot. The runtime stage copies it to /app/wwwroot.
# ─────────────────────────────────────────────────────────────────────────────
FROM node:22-alpine AS frontend-build

WORKDIR /src/frontend

# Dependencies first: this layer stays cached while package-lock.json is unchanged.
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build


# ─────────────────────────────────────────────────────────────────────────────
# Stage 2: backend-build — restores and publishes a release build.
# Every package comes from nuget.org; no private feed is involved.
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build

WORKDIR /src

# Directory.Build.props sets the shared MSBuild properties (TargetFramework,
# Nullable, TreatWarningsAsErrors…) and must be present before any restore.
COPY docu-ai-flow.sln Directory.Build.props ./
COPY src/ ./src/
RUN dotnet publish src/InvoiceProcessor.Worker/InvoiceProcessor.Worker.csproj \
    -c Release \
    -o /app/publish


# ─────────────────────────────────────────────────────────────────────────────
# Stage 3: runtime
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# One RUN to keep the layer count down:
#   poppler-utils        — pdftoppm, rasterises a scanned page before OCR
#   tesseract-ocr(+spa)  — the OCR engine and its Spanish language data
#   curl                 — used by the compose healthcheck; not in the aspnet base image
# The OCR fallback is off by default; the tools ship anyway so turning it on is
# a config change and not a rebuild.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        poppler-utils \
        tesseract-ocr \
        tesseract-ocr-spa \
        curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=backend-build /app/publish ./
# ASP.NET serves this as the web root; it sits next to the binary, so the default
# content root finds it with no configuration.
COPY --from=frontend-build /src/src/InvoiceProcessor.Worker/wwwroot ./wwwroot

# There is no .git above /app, so DataRoot falls back to the start directory and
# every "./data/..." path resolves under /app/data — the folder compose mounts.
ENV DOCU_AI_FLOW_DATA=/app

# The aspnet base image already listens on 8080 (ASPNETCORE_HTTP_PORTS), so this
# needs no ASPNETCORE_URLS of its own.
EXPOSE 8080

ENTRYPOINT ["dotnet", "InvoiceProcessor.Worker.dll"]

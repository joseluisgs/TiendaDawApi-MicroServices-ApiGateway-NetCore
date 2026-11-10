# Statistics · build multi-stage. Contexto = RAÍZ del repo:
#   docker build -f Services/Statistics/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG BUILD_CONFIGURATION=Release

# Central Package Management + props comunes: sin ellos NU1015 (csproj sin Version=)
COPY global.json Directory.Packages.props Directory.Build.props ./
COPY BuildingBlocks/BuildingBlocks.csproj BuildingBlocks/
COPY Services/Statistics/Statistics.csproj Services/Statistics/Statistics.csproj

RUN dotnet restore Services/Statistics/Statistics.csproj

COPY BuildingBlocks/ BuildingBlocks/
COPY Services/Statistics/ Services/Statistics/

RUN dotnet publish Services/Statistics/Statistics.csproj -c $BUILD_CONFIGURATION -o /app/publish --no-restore

# ── Runtime ──────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl para el HEALTHCHECK (la imagen aspnet NO lo trae)
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build --chown=app:app /app/publish .

# El proceso corre como usuario "app" (no-root, APP_UID=1654):
#  - /app lo creó WORKDIR como root -> propietario app.
#  - storage escribe en wwwroot/uploads (el volumen hereda el ownership de la
#    imagen; si el directorio no existiera, Docker lo crearía como root y fallaría).
RUN mkdir -p /app/wwwroot/images/uploads \
    && chown -R app:app /app

# La topología (Urls, Backends, Services:*) vive en appsettings.Production.json;
# los secretos (JWT, SMTP, OAuth, contraseñas) llegan por environment en compose.
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 5107

HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:5107/health || exit 1

USER app
ENTRYPOINT ["dotnet", "Statistics.dll"]

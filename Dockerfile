# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------------------------
# SmartSurvey — multi-stage container build (ASP.NET Core 8, Linux).
#   docker build -t smartsurvey .
#   docker compose up --build        (app + PostgreSQL, see docker-compose.yml)
# ---------------------------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (better layer caching): only project files and shared build settings.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/SmartSurvey.Domain/SmartSurvey.Domain.csproj src/SmartSurvey.Domain/
COPY src/SmartSurvey.Application/SmartSurvey.Application.csproj src/SmartSurvey.Application/
COPY src/SmartSurvey.Infrastructure/SmartSurvey.Infrastructure.csproj src/SmartSurvey.Infrastructure/
COPY src/SmartSurvey.Web/SmartSurvey.Web.csproj src/SmartSurvey.Web/
RUN dotnet restore src/SmartSurvey.Web/SmartSurvey.Web.csproj

COPY src/ src/
RUN dotnet publish src/SmartSurvey.Web/SmartSurvey.Web.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# QuestPDF's native renderer needs fontconfig; DejaVu provides a broad glyph fallback for PDFs.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Data-protection keys (auth cookies / bearer tokens) are persisted to a volume so that
# logins survive container restarts.
RUN mkdir -p /app/keys && chown app:app /app/keys

# ASPNETCORE_HTTP_PORTS (not ASPNETCORE_URLS) is the .NET 8+ image convention; setting both logs a warning.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DataProtection__KeysPath=/app/keys \
    Https__Redirect=false \
    ReverseProxy__Enabled=true \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "SmartSurvey.Web.dll"]

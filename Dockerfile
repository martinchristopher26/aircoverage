# syntax=docker/dockerfile:1

# --- build the Vue SPA ---
FROM node:20-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm install
COPY web/ ./
# outputs /web/dist
RUN npm run build

# --- build & publish the .NET API ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api
WORKDIR /src
COPY AirCoverage.Api/*.csproj AirCoverage.Api/
RUN dotnet restore AirCoverage.Api/AirCoverage.Api.csproj
COPY AirCoverage.Api/ AirCoverage.Api/
# SPA served as static files by the API
COPY --from=web /web/dist AirCoverage.Api/wwwroot
RUN dotnet publish AirCoverage.Api/AirCoverage.Api.csproj -c Release -o /app

# --- tiny runtime image ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=api /app ./
# The SQLite file and the auto-generated self-signed dev cert both live on the
# mounted volume (see docker-compose.yml), NOT in the image layer. The app binds
# HTTPS on 8443 in code (Program.cs), so ASPNETCORE_URLS is intentionally unset.
ENV ConnectionStrings__Default="Data Source=/data/aircoverage.db"
ENV DevCert__Path="/data/aircoverage-dev.pfx"
VOLUME /data
EXPOSE 8443
ENTRYPOINT ["dotnet", "AirCoverage.Api.dll"]

# syntax=docker/dockerfile:1

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (layer-cached until the project file changes)
COPY varistyhubapi.csproj ./
RUN dotnet restore varistyhubapi.csproj

# Build & publish
COPY . ./
RUN dotnet publish varistyhubapi.csproj -c Release -o /app/publish --no-restore

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_ENVIRONMENT=Production
# Railway overrides PORT at runtime; Program.cs binds to it. 8080 is the local default.
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "varistyhubapi.dll"]

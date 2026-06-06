# syntax=docker/dockerfile:1.7
# Multi-stage build for the AtomicHabits API.

# 1) Restore + build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY AtomicHabits.csproj .
RUN dotnet restore AtomicHabits.csproj

COPY . .
RUN dotnet publish AtomicHabits.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# 2) Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Run as non-root for safer container posture.
RUN useradd --uid 1000 --create-home --shell /bin/bash atomichabits
USER atomichabits

COPY --from=build --chown=atomichabits:atomichabits /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "AtomicHabits.dll"]

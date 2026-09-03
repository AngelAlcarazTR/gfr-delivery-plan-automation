# syntax=docker/dockerfile:1
#
# MCP server (GFR delivery-plan) container image.
#
# IMPORTANT: build with the REPO ROOT as context — the Mcp project references
# src/Core and src/Adapters, so those must be inside the build context.
#
#   Cloud build (no local Docker needed):
#     az acr build -r <acr-name> -t gfr-mcp:latest -f src/Mcp/Dockerfile .
#   or:
#     az containerapp up --name gfr-mcp --resource-group <rg> \
#         --source . --ingress external --target-port 8080
#   Local build:
#     docker build -t gfr-mcp:latest -f src/Mcp/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (layer-cached): copy only the csproj files the graph needs.
COPY src/Core/Core.csproj src/Core/
COPY src/Adapters/Adapters.csproj src/Adapters/
COPY src/Mcp/Mcp.csproj src/Mcp/
RUN dotnet restore src/Mcp/Mcp.csproj

# Copy the sources and publish.
COPY src/Core/ src/Core/
COPY src/Adapters/ src/Adapters/
COPY src/Mcp/ src/Mcp/
RUN dotnet publish src/Mcp/Mcp.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# SkiaSharp (pulled in through Adapters) needs native font libraries on Linux.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./

# Kestrel/MCP listen on 8080 inside the container (matches --target-port).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Mcp.dll"]

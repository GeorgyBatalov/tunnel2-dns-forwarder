# syntax=docker/dockerfile:1

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy solution and project files
COPY Tunnel2.DnsForwarder.sln .
COPY src/Tunnel2.DnsForwarder/Tunnel2.DnsForwarder.csproj src/Tunnel2.DnsForwarder/
COPY tests/Tunnel2.DnsForwarder.UnitTests/Tunnel2.DnsForwarder.UnitTests.csproj tests/Tunnel2.DnsForwarder.UnitTests/
COPY tests/Tunnel2.DnsForwarder.IntegrationTests/Tunnel2.DnsForwarder.IntegrationTests.csproj tests/Tunnel2.DnsForwarder.IntegrationTests/

# Copy all source code
COPY src/ src/
COPY tests/ tests/

# Restore, build and publish
WORKDIR /source/src/Tunnel2.DnsForwarder
RUN dotnet restore && \
    dotnet publish -c Release -o /app --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

# Install dig/dnsutils for health check
RUN apt-get update && \
    apt-get install -y dnsutils && \
    rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=build /app .

# Create non-root user
RUN groupadd -r dnsforwarder && \
    useradd -r -g dnsforwarder dnsforwarder && \
    chown -R dnsforwarder:dnsforwarder /app

USER dnsforwarder

# Expose DNS port (UDP 53)
EXPOSE 53/udp

# Set environment variables
ENV DOTNET_EnableDiagnostics=0

# Health check using DNS query
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD dig @127.0.0.1 health.check A +short || exit 1

ENTRYPOINT ["dotnet", "Tunnel2.DnsForwarder.dll"]

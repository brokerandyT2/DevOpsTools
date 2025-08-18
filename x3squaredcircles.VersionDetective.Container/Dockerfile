# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY x3squaredcircles.VersionDetective.Container.csproj .
RUN dotnet restore

# Copy source code
COPY . .

# Build and publish the application
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS runtime

# Install required tools for git analysis and multi-language support
RUN apk add --no-cache \
    git \
    openssh-client \
    curl \
    jq \
    bash

# Create app user for security
RUN addgroup -g 1001 -S appgroup && \
    adduser -S -D -H -u 1001 -h /app -s /sbin/nologin -G appgroup appuser

# Set working directory
WORKDIR /app

# Copy published application
COPY --from=build /app/publish .

# Create source mount directory with proper permissions
RUN mkdir -p /src && \
    chown appuser:appgroup /src && \
    chmod 755 /src

# Ensure proper permissions for application
RUN chown -R appuser:appgroup /app && \
    chmod +x /app/version-detective

# Set environment variables
ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true \
    DOTNET_EnableDiagnostics=0

# Switch to non-root user
USER appuser

# Expose port 8080 for documentation server
EXPOSE 8080

# Health check for container monitoring (checks both main app and docs server)
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Volume for source code (must be mounted by pipeline)
VOLUME ["/src"]

# Entry point
ENTRYPOINT ["/app/version-detective"]
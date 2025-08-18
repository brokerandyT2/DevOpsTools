# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY x3squaredcircles.License.Server.csproj .
RUN dotnet restore

# Copy source code
COPY . .

# Build the application
RUN dotnet build -c Release -o /app/build

# Publish the application
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime

# Install required packages for encryption validation
RUN apk add --no-cache \
    util-linux \
    cryptsetup \
    file

# Create app user for security
RUN addgroup -g 1001 -S appgroup && \
    adduser -S -D -H -u 1001 -h /app -s /sbin/nologin -G appgroup appuser

# Set working directory
WORKDIR /app

# Copy published application
COPY --from=build /app/publish .

# Create data directory with proper permissions
RUN mkdir -p /data && \
    chown appuser:appgroup /data && \
    chmod 755 /data

# Copy license configuration file (will be replaced per customer)
COPY license-config.json .

# Ensure proper permissions
RUN chown -R appuser:appgroup /app && \
    chmod +x /app/x3squaredcircles.License.Server

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV LC_SCHEMA_VERSION=1.0.0

# Expose port
EXPOSE 8080

# Switch to non-root user
USER appuser

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1

# Volume for persistent data (must be encrypted)
VOLUME ["/data"]

# Entry point
ENTRYPOINT ["dotnet", "x3squaredcircles.License.Server.dll"]
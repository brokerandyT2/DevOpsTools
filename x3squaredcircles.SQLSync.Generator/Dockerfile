# Use the official .NET 9 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build

# Set the working directory
WORKDIR /src

# Copy the project file
COPY *.csproj ./

# Restore dependencies
RUN dotnet restore --runtime linux-x64

# Copy the entire source code
COPY . .

# Build and publish the application
RUN dotnet publish \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --no-restore \
    --output /app/publish \
    /p:PublishSingleFile=true \
    /p:PublishTrimmed=false \
    /p:IncludeNativeLibrariesForSelfExtract=true \
    /p:EnableCompressionInSingleFile=true

# Use a minimal runtime image
FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-alpine AS runtime

# Install additional dependencies that might be needed for database connections
RUN apk add --no-cache \
    icu-libs \
    tzdata \
    ca-certificates \
    && rm -rf /var/cache/apk/*

# Create a non-root user
RUN addgroup -g 1000 sqlgen && \
    adduser -D -s /bin/sh -u 1000 -G sqlgen sqlgen

# Set the working directory
WORKDIR /app

# Copy the published application
COPY --from=build /app/publish .

# Copy additional files
COPY README.md CHANGELOG.md ./

# Create directories for data and logs
RUN mkdir -p /app/data /app/logs && \
    chown -R sqlgen:sqlgen /app

# Set environment variables
ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://+:8080 \
    PATH="/app:${PATH}"

# Switch to non-root user
USER sqlgen

# Expose the port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD ./sql-schema-generator --help > /dev/null || exit 1

# Set the entrypoint
ENTRYPOINT ["./sql-schema-generator"]
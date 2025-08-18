# Use the official .NET 8 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build

# Set working directory
WORKDIR /src

# Copy project file
COPY x3squaredcircles.DesignToken.Generator.csproj .

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY . .

# Build the application
RUN dotnet publish -c Release -o /app --no-restore

# Use the official .NET 8 runtime image for final image
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS final

# Install git (required for git operations)
RUN apk add --no-cache git curl

# Set working directory
WORKDIR /app

# Copy the built application
COPY --from=build /app .

# Create mount point for source code
VOLUME ["/src"]

# Set environment variables for container
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true

# Set the entry point
ENTRYPOINT ["./design-token-generator"]
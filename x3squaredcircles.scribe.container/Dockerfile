# Stage 1: Build the application using the .NET 7 SDK
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

# Copy the project file and restore dependencies first to leverage Docker layer caching.
# This step is only re-run if the .csproj file changes.
COPY ["x3squaredcircles.scribe.container.csproj", "."]
RUN dotnet restore "./x3squaredcircles.scribe.container.csproj"

# Copy the rest of the application source code
COPY . .

# Publish the application, creating a self-contained and optimized output for release.
RUN dotnet publish "x3squaredcircles.scribe.container.csproj" -c Release -o /app/publish --no-restore


# Stage 2: Create the final, smaller runtime image using the ASP.NET 7 runtime
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS final
WORKDIR /app

# The Scribe's GitService and EnvironmentService require the 'git' command-line tool.
# We must install it into the final runtime image.
# We run these commands as root before switching to the less-privileged 'app' user.
USER root
RUN apt-get update && apt-get install -y --no-install-recommends git && \
    # Clean up the apt cache to keep the final image size down
    rm -rf /var/lib/apt/lists/*

# Switch to the non-root 'app' user for enhanced security.
USER app

# Copy the published application files from the 'build' stage.
COPY --from=build /app/publish .

# Define the entry point for the container. When the container starts, it will execute
# this command, which runs The Scribe application.
ENTRYPOINT ["dotnet", "x3squaredcircles.scribe.container.dll"]
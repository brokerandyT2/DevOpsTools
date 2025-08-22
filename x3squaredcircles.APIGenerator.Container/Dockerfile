# Stage 1: The Build Environment
# This stage uses the full .NET 8 SDK to build our application.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /source

# Copy csproj and restore dependencies to leverage Docker layer caching.
COPY *.csproj .
RUN dotnet restore

# Copy the rest of the application's source code.
COPY . .

# Publish the application in Release configuration for a self-contained output.
# The project/dll name is taken from the csproj file's AssemblyName.
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: The Final Polyglot Runtime Environment
# This stage starts with the lean ASP.NET runtime (for our HttpServerService)
# and installs all required runtimes for our polyglot analyzers.
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Install all required external dependencies in a single RUN layer to optimize image size.
# - git: Required by GitService.
# - openjdk-17-jre-headless: Latest long-term support (LTS) version of Java.
# - python3.11 & pip: A specific, modern version of Python 3.
# - nodejs & npm: Latest long-term support (LTS) version of Node.js.
RUN apt-get update && apt-get install -y --no-install-recommends \
    git \
    openjdk-17-jre-headless \
    python3.11 \
    python3-pip \
    nodejs \
    npm \
    # Clean up the apt cache to keep the final image layer smaller.
    && rm -rf /var/lib/apt/lists/*

# Set python3.11 to be the default 'python3'
RUN update-alternatives --install /usr/bin/python3 python3 /usr/bin/python3.11 1

WORKDIR /app

# Copy the published output from the build stage into the final image.
COPY --from=build /app/publish .
EXPOSE 8080
# Define a mount point for the user's source code repository.
# The CI/CD pipeline will mount the workspace into this directory.
VOLUME /src
VOLUME /tmp

# Set the entry point for the container.
# The DLL name must match the AssemblyName in the .csproj file.
ENTRYPOINT ["dotnet", "x3squaredcircles.datalink.container.dll"]
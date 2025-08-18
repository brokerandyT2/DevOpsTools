# Stage 1: The Build Environment
# This stage uses the full .NET 8 SDK to build the application and includes git for our GitService.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Install git command-line tool, which is a dependency for our GitService.
RUN apt-get update && apt-get install -y git

WORKDIR /source

# Copy the project file first and restore dependencies.
# This leverages Docker's layer caching. If the dependencies haven't changed,
# this layer will not be rebuilt, speeding up subsequent builds.
COPY *.csproj .
RUN dotnet restore

# Copy the rest of the source code into the container.
COPY . .

# Build and publish the application in Release configuration.
# The output will be a self-contained, optimized set of files.
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: The Final Runtime Environment
# This stage uses the much smaller ASP.NET 8 runtime image. We use aspnet instead of runtime
# because our HttpServerService requires the ASP.NET Core hosting libraries.
# We also install git here so the runtime can perform git operations.
FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y git && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy the published output from the build stage into the final image.
COPY --from=build /app/publish .

# Define a mount point for the user's source code and any temporary git clones.
# The CI/CD pipeline will mount the repository into this directory.
VOLUME /src
VOLUME /tmp

# Set the entry point for the container.
# When the container runs, it will execute our DataLink application.
ENTRYPOINT ["dotnet", "API.dll"]
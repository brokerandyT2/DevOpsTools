# Stage 1: Build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["Conductor.csproj", "."]
RUN dotnet restore "./Conductor.csproj"

# Copy the rest of the source code
COPY . .

# Build and publish the application for release
RUN dotnet publish "Conductor.csproj" -c Release -o /app/publish --no-restore

# Stage 2: Create the final, lean runtime image
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine

# Alpine Linux requires some additional packages for the Conductor to function.
# git: Required for any git-based operations a pipeline might perform.
# docker-cli: The "Docker-out-of-Docker" pattern requires the Docker client.
# bash: Provides a more robust shell than the default 'sh' for complex commands.
RUN apk --no-cache add git docker-cli bash

RUN mkdir -p /usr/share/jenkins && \
    wget -q -O /usr/share/jenkins/jenkins-declarative-linter.jar \
    https://github.com/jenkinsci/declarative-pipeline-migration-assistant-plugin/releases/download/v1.5.1/jenkins-declarative-linter-1.5.1.jar

WORKDIR /app
COPY --from=build /app/publish .

# The final entrypoint for the container. It executes the compiled application.
ENTRYPOINT ["dotnet", "x3squaredcircles.runner.container.dll"]
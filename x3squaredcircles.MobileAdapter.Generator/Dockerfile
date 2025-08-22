# Stage 1: Build Environment
# Use the official .NET 8 SDK image which contains all the necessary tools
# to build and publish the .NET application.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file first and restore dependencies. This is a Docker
# optimization that leverages layer caching. If the project's dependencies
# haven't changed, Docker can reuse this layer, speeding up subsequent builds.
COPY ["x3squaredcircles.MobileAdapter.Generator.csproj", "."]
RUN dotnet restore "./x3squaredcircles.MobileAdapter.Generator.csproj"

# Copy the rest of the application's source code into the build environment.
COPY . .

# Publish the application to a dedicated folder, creating a release-optimized,
# self-contained set of files ready for the runtime environment.
RUN dotnet publish "x3squaredcircles.MobileAdapter.Generator.csproj" -c Release -o /app/publish

# ---

# Stage 2: Final Runtime Image
# Use the much smaller ASP.NET runtime image for the final stage. This image
# contains the necessary .NET runtimes but excludes the SDK, resulting in a
# smaller, more secure container.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Expose port 8080. This is required by our architectural directives for the
# Developer Experience (DX) server, which provides the /health and /docs endpoints.
EXPOSE 8080

# Copy the published application files from the 'build' stage into the final image.
COPY --from=build /app/publish .

# Set the entry point for the container. This is the command that will be executed
# when the container starts, running the Mobile Adapter Generator.
ENTRYPOINT ["dotnet", "x3squaredcircles.MobileAdapter.Generator.dll"]
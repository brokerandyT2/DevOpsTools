# Stage 1: The Build Environment
# This stage uses the full .NET 8 SDK to build the application.
# It contains all the tools needed for restoring dependencies and compiling the code.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy the project file first and restore dependencies.
# This leverages Docker's layer caching. If the dependencies haven't changed,
# this layer won't be rebuilt, speeding up subsequent builds.
COPY *.csproj .
RUN dotnet restore

# Copy the rest of the source code into the container.
COPY . .

# Build and publish the application in Release configuration.
# The output will be a self-contained, optimized set of files.
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: The Final Runtime Environment
# This stage uses the much smaller ASP.NET 8 runtime image, which is all
# that's needed to run the compiled application and its embedded web server.
# This results in a smaller, more secure final image.
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Copy the published output from the build stage into the final image.
COPY --from=build /app/publish .

# Define a mount point for the user's source code.
# The CI/CD pipeline will mount the repository into this directory.
# This makes it explicit that the container expects external data here.
VOLUME /src

# Set the entry point for the container.
# When the container runs, it will execute our Guardian application.
ENTRYPOINT ["dotnet", "guardian.dll"]
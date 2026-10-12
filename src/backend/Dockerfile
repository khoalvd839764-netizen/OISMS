# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and csproj files for layer caching
COPY ["src/backend/OISM.sln", "src/backend/"]
COPY ["src/backend/src/OISM.Domain/OISM.Domain.csproj", "src/backend/src/OISM.Domain/"]
COPY ["src/backend/src/OISM.Application/OISM.Application.csproj", "src/backend/src/OISM.Application/"]
COPY ["src/backend/src/OISM.Infrastructure/OISM.Infrastructure.csproj", "src/backend/src/OISM.Infrastructure/"]
COPY ["src/backend/src/OISM.WebApi/OISM.WebApi.csproj", "src/backend/src/OISM.WebApi/"]
COPY ["src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj", "src/backend/tests/OISM.UnitTests/"]
COPY ["src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj", "src/backend/tests/OISM.IntegrationTests/"]

RUN dotnet restore "src/backend/OISM.sln"

# Copy source code and build
COPY src/backend/ src/backend/
WORKDIR "/src/backend/src/OISM.WebApi"
RUN dotnet build "OISM.WebApi.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "OISM.WebApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OISM.WebApi.dll"]

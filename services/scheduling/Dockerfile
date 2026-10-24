# Scheduling service — build from the repository root:
#   docker build -f services/scheduling/Dockerfile -t slothub-scheduling .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY shared/BuildingBlocks.Domain/BuildingBlocks.Domain.csproj shared/BuildingBlocks.Domain/
COPY shared/BuildingBlocks/BuildingBlocks.csproj shared/BuildingBlocks/
COPY services/scheduling/src/Scheduling.Domain/Scheduling.Domain.csproj services/scheduling/src/Scheduling.Domain/
COPY services/scheduling/src/Scheduling.Infrastructure/Scheduling.Infrastructure.csproj services/scheduling/src/Scheduling.Infrastructure/
COPY services/scheduling/src/Scheduling.Api/Scheduling.Api.csproj services/scheduling/src/Scheduling.Api/
RUN dotnet restore services/scheduling/src/Scheduling.Api/Scheduling.Api.csproj

COPY shared/ shared/
COPY services/scheduling/src/ services/scheduling/src/
RUN dotnet publish services/scheduling/src/Scheduling.Api/Scheduling.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
# Non-root user provided by the .NET base image
USER $APP_UID
ENTRYPOINT ["dotnet", "Scheduling.Api.dll"]

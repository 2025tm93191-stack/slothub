# Notification service — build from the repository root:
#   docker build -f services/notification/Dockerfile -t slothub-notification .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY shared/BuildingBlocks.Domain/BuildingBlocks.Domain.csproj shared/BuildingBlocks.Domain/
COPY shared/BuildingBlocks/BuildingBlocks.csproj shared/BuildingBlocks/
COPY services/notification/src/Notification.Domain/Notification.Domain.csproj services/notification/src/Notification.Domain/
COPY services/notification/src/Notification.Infrastructure/Notification.Infrastructure.csproj services/notification/src/Notification.Infrastructure/
COPY services/notification/src/Notification.Api/Notification.Api.csproj services/notification/src/Notification.Api/
RUN dotnet restore services/notification/src/Notification.Api/Notification.Api.csproj

COPY shared/ shared/
COPY services/notification/src/ services/notification/src/
RUN dotnet publish services/notification/src/Notification.Api/Notification.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
# Non-root user provided by the .NET base image
USER $APP_UID
ENTRYPOINT ["dotnet", "Notification.Api.dll"]

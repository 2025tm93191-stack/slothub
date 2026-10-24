# Booking service — build from the repository root:
#   docker build -f services/booking/Dockerfile -t slothub-booking .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY shared/BuildingBlocks.Domain/BuildingBlocks.Domain.csproj shared/BuildingBlocks.Domain/
COPY shared/BuildingBlocks/BuildingBlocks.csproj shared/BuildingBlocks/
COPY services/booking/src/Booking.Domain/Booking.Domain.csproj services/booking/src/Booking.Domain/
COPY services/booking/src/Booking.Infrastructure/Booking.Infrastructure.csproj services/booking/src/Booking.Infrastructure/
COPY services/booking/src/Booking.Api/Booking.Api.csproj services/booking/src/Booking.Api/
RUN dotnet restore services/booking/src/Booking.Api/Booking.Api.csproj

COPY shared/ shared/
COPY services/booking/src/ services/booking/src/
RUN dotnet publish services/booking/src/Booking.Api/Booking.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
# Non-root user provided by the .NET base image
USER $APP_UID
ENTRYPOINT ["dotnet", "Booking.Api.dll"]

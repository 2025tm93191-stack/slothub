# Booking service image — build from the repository root:
#   docker build -f services/booking/Dockerfile -t slothub-booking:1.0.0 .
# Configuration (connection string, service URLs, settings) comes from environment variables at run time;
# no .env file or password is ever copied into the image (see .dockerignore).

# ---- Build stage: full .NET SDK ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the package layer is cached until a project file changes
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY shared/BuildingBlocks.Domain/BuildingBlocks.Domain.csproj shared/BuildingBlocks.Domain/
COPY shared/BuildingBlocks/BuildingBlocks.csproj shared/BuildingBlocks/
COPY services/booking/src/Booking.Domain/Booking.Domain.csproj services/booking/src/Booking.Domain/
COPY services/booking/src/Booking.Infrastructure/Booking.Infrastructure.csproj services/booking/src/Booking.Infrastructure/
COPY services/booking/src/Booking.Api/Booking.Api.csproj services/booking/src/Booking.Api/
RUN dotnet restore services/booking/src/Booking.Api/Booking.Api.csproj

# Then the source, and publish a framework-dependent build
COPY shared/ shared/
COPY services/booking/src/ services/booking/src/
RUN dotnet publish services/booking/src/Booking.Api/Booking.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ---- Runtime stage: ASP.NET runtime only (no SDK, no source) ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG VERSION=1.0.0
ARG REVISION=unknown
LABEL org.opencontainers.image.title="slothub-booking" \
      org.opencontainers.image.description="SlotHub Booking service" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.source="https://github.com/2025tm93191-stack/slothub"

WORKDIR /app
COPY --from=build /app .
COPY --chmod=755 infra/docker/healthcheck.sh /usr/local/bin/healthcheck

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

# Healthy only when /health/ready returns 200 (service up and its database reachable)
HEALTHCHECK --interval=10s --timeout=5s --start-period=40s --retries=3 CMD ["healthcheck"]

# Non-root user provided by the .NET base image
USER $APP_UID
ENTRYPOINT ["dotnet", "Booking.Api.dll"]

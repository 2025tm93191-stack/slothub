# Payment service — build from the repository root:
#   docker build -f services/payment/Dockerfile -t slothub-payment .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY shared/BuildingBlocks.Domain/BuildingBlocks.Domain.csproj shared/BuildingBlocks.Domain/
COPY shared/BuildingBlocks/BuildingBlocks.csproj shared/BuildingBlocks/
COPY services/payment/src/Payment.Domain/Payment.Domain.csproj services/payment/src/Payment.Domain/
COPY services/payment/src/Payment.Infrastructure/Payment.Infrastructure.csproj services/payment/src/Payment.Infrastructure/
COPY services/payment/src/Payment.Api/Payment.Api.csproj services/payment/src/Payment.Api/
RUN dotnet restore services/payment/src/Payment.Api/Payment.Api.csproj

COPY shared/ shared/
COPY services/payment/src/ services/payment/src/
RUN dotnet publish services/payment/src/Payment.Api/Payment.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
# Non-root user provided by the .NET base image
USER $APP_UID
ENTRYPOINT ["dotnet", "Payment.Api.dll"]

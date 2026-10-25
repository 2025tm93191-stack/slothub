using BuildingBlocks.Chaos;
using BuildingBlocks.Idempotency;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payment.Domain.Payments;
using Payment.Infrastructure.Gateway;
using Payment.Infrastructure.Persistence;

namespace Payment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddServiceDbContext<PaymentDbContext>(configuration);
        services.AddIdempotency<PaymentDbContext>(configuration);
        services.AddChaos(configuration, MockPaymentGateway.DeclineMode, MockPaymentGateway.ErrorMode);
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentGateway, MockPaymentGateway>();
        services.AddScoped<PaymentService>();
        return services;
    }
}

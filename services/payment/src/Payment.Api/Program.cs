using BuildingBlocks.Chaos;
using BuildingBlocks.Configuration;
using BuildingBlocks.Hosting;
using BuildingBlocks.Persistence;
using FluentValidation;
using Payment.Api.Endpoints;
using Payment.Infrastructure;
using Payment.Infrastructure.Persistence;

const string serviceName = "Payment";

DotEnv.Load();
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(serviceName);
builder.Services.AddPaymentInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var app = builder.Build();
app.UseServiceDefaults(serviceName);

app.MapGroup("/api/v1").MapPaymentEndpoints();
app.MapChaosEndpoints();

await app.MigrateDatabaseIfEnabledAsync<PaymentDbContext>();
await app.RunAsync();

public partial class Program;

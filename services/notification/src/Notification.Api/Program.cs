using BuildingBlocks.Chaos;
using BuildingBlocks.Configuration;
using BuildingBlocks.Hosting;
using BuildingBlocks.Persistence;
using FluentValidation;
using Notification.Api.Endpoints;
using Notification.Infrastructure;
using Notification.Infrastructure.Persistence;

const string serviceName = "Notification";

DotEnv.Load();
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(serviceName);
builder.Services.AddNotificationInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var app = builder.Build();
app.UseServiceDefaults(serviceName);

app.MapGroup("/api/v1").MapNotificationEndpoints();
app.MapChaosEndpoints();

await app.MigrateDatabaseIfEnabledAsync<NotificationDbContext>();
await app.RunAsync();

public partial class Program;

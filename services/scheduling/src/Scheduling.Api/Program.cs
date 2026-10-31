using BuildingBlocks.Configuration;
using BuildingBlocks.Hosting;
using BuildingBlocks.Persistence;
using FluentValidation;
using Scheduling.Api.Endpoints;
using Scheduling.Infrastructure;
using Scheduling.Infrastructure.Persistence;

const string serviceName = "Scheduling";

DotEnv.Load();
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(serviceName);
builder.Services.AddSchedulingInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var app = builder.Build();
app.UseServiceDefaults(serviceName);

var api = app.MapGroup("/api/v1");
api.MapCatalogEndpoints();
api.MapSlotEndpoints();
api.MapHoldEndpoints();

if (await app.MigrateDatabaseIfEnabledAsync<SchedulingDbContext>())
{
    return; // MIGRATE_ONLY: migrations applied, exit (Kubernetes migration Job)
}

await app.RunAsync();

public partial class Program;

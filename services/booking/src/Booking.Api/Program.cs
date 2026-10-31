using Booking.Api.Endpoints;
using Booking.Infrastructure;
using Booking.Infrastructure.Persistence;
using BuildingBlocks.Configuration;
using BuildingBlocks.Hosting;
using BuildingBlocks.Persistence;
using FluentValidation;

const string serviceName = "Booking";

DotEnv.Load();
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(serviceName);
builder.Services.AddBookingInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var app = builder.Build();
app.UseServiceDefaults(serviceName);

app.MapGroup("/api/v1").MapBookingEndpoints();

if (await app.MigrateDatabaseIfEnabledAsync<BookingDbContext>())
{
    return; // MIGRATE_ONLY: migrations applied, exit (Kubernetes migration Job)
}

await app.RunAsync();

public partial class Program;

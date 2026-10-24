using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Correlation;
using BuildingBlocks.Errors;
using BuildingBlocks.Health;
using BuildingBlocks.Idempotency;
using BuildingBlocks.Logging;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;

namespace BuildingBlocks.Hosting;

/// <summary>The common wiring every SlotHub service gets: logging, errors, correlation, JSON, OpenAPI, health.</summary>
public static class ServiceDefaults
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        builder.AddStructuredLogging(serviceName);

        var services = builder.Services;
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdHandler>();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsSetup.Enrich);
        services.AddExceptionHandler<GlobalExceptionHandler>();
        // Malformed JSON / missing body throws so it is rendered by GlobalExceptionHandler like every other error.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        services.Configure<JsonOptions>(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper)));

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = $"SlotHub {serviceName} API";
                document.Info.Version = "v1";
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IdempotentAttribute>().Any())
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = IdempotencyMiddleware.HeaderName,
                        In = ParameterLocation.Header,
                        Required = true,
                        Description = "Unique key per logical operation. Repeating it returns the original result (BR4).",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = IdempotencyMiddleware.MaxKeyLength },
                    });
                }

                return Task.CompletedTask;
            });
        });

        services.AddHealthChecks();
        return builder;
    }

    public static WebApplication UseServiceDefaults(this WebApplication app, string serviceName)
    {
        // Order matters: correlation first so every log line and error carries the ID;
        // request logging outside the exception handler so it records the final status code;
        // idempotency outside the exception handler so error responses (409, 422...) are stored and replayed too.
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseStructuredRequestLogging();
        app.UseMiddleware<IdempotencyMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapOpenApi();
        app.MapScalarApiReference();
        app.MapHealthEndpoints(serviceName);
        return app;
    }
}

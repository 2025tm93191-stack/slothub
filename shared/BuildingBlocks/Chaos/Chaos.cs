using BuildingBlocks.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Chaos;

/// <summary>
/// Failure injection for the mock dependencies (Payment gateway, SMS/email provider).
/// <c>Mode</c> is interpreted by the owning service, e.g. Payment uses DECLINE or ERROR.
/// </summary>
public sealed record ChaosSettings(double FailureRate, int LatencyMs, string Mode);

/// <summary>Current chaos settings; initialised from env vars and changeable at runtime via the admin endpoint.</summary>
public sealed class ChaosState(ChaosSettings initial)
{
    private ChaosSettings _current = initial;

    public ChaosSettings Current => Volatile.Read(ref _current);

    public void Set(ChaosSettings settings) => Volatile.Write(ref _current, settings);

    public bool ShouldFail() => Random.Shared.NextDouble() < Current.FailureRate;

    public Task DelayAsync(CancellationToken cancellationToken) =>
        Current.LatencyMs > 0 ? Task.Delay(Current.LatencyMs, cancellationToken) : Task.CompletedTask;
}

public sealed class ChaosSettingsValidator : AbstractValidator<ChaosSettings>
{
    public ChaosSettingsValidator(IReadOnlyCollection<string> allowedModes)
    {
        RuleFor(x => x.FailureRate).InclusiveBetween(0, 1);
        RuleFor(x => x.LatencyMs).InclusiveBetween(0, 60_000);
        RuleFor(x => x.Mode)
            .Must(mode => mode is not null && allowedModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Mode must be one of: {string.Join(", ", allowedModes)}.");
    }
}

public static class ChaosExtensions
{
    /// <summary>Reads <c>CHAOS_FAILURE_RATE</c>, <c>CHAOS_LATENCY_MS</c> and <c>CHAOS_FAILURE_MODE</c>.</summary>
    public static IServiceCollection AddChaos(this IServiceCollection services, IConfiguration configuration, params string[] allowedModes)
    {
        var settings = new ChaosSettings(
            configuration.GetValue("CHAOS_FAILURE_RATE", 0.0),
            configuration.GetValue("CHAOS_LATENCY_MS", 0),
            configuration.GetValue("CHAOS_FAILURE_MODE", allowedModes[0])!.ToUpperInvariant());

        var validator = new ChaosSettingsValidator(allowedModes);
        var result = validator.Validate(settings);
        if (!result.IsValid)
        {
            throw new InvalidOperationException($"Invalid chaos configuration: {result}");
        }

        services.AddSingleton(new ChaosState(settings));
        services.AddSingleton<IValidator<ChaosSettings>>(validator);
        return services;
    }

    /// <summary><c>GET/PUT /api/v1/admin/chaos</c> — inspect or change failure injection during a demo.</summary>
    public static IEndpointRouteBuilder MapChaosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/chaos").WithTags("Admin");

        group.MapGet("/", (ChaosState chaos) => TypedResults.Ok(chaos.Current))
            .WithName("GetChaos")
            .WithSummary("Get current failure-injection settings");

        group.MapPut("/", (ChaosSettings settings, ChaosState chaos) =>
            {
                chaos.Set(settings with { Mode = settings.Mode.ToUpperInvariant() });
                return TypedResults.NoContent();
            })
            .Validate<ChaosSettings>()
            .WithName("SetChaos")
            .WithSummary("Change failure-injection settings at runtime");

        return app;
    }
}

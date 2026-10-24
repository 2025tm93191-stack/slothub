using BuildingBlocks.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Validation;

/// <summary>Request body failed validation. Rendered as 400 VALIDATION_FAILED with per-field <c>errors</c>.</summary>
public sealed class RequestValidationException(IDictionary<string, string[]> errors)
    : DomainException(ErrorKind.Validation, ErrorCodes.ValidationFailed, "One or more fields are invalid.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Runs the registered FluentValidation validator for the endpoint argument of type <typeparamref name="T"/>.</summary>
public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.Arguments.OfType<T>().FirstOrDefault() is { } argument)
        {
            var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                throw new RequestValidationException(result.ToDictionary());
            }
        }

        return await next(context);
    }
}

public static class ValidationExtensions
{
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}

using System.Text.Json;
using BuildingBlocks.Correlation;
using BuildingBlocks.Domain;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Errors;

/// <summary>
/// Gives every Problem Details response (exceptions, validation, 404 routes) the same shape:
/// <c>type, title, status, detail, instance, code, correlationId</c>.
/// </summary>
public static class ProblemDetailsSetup
{
    public const string TypeBaseUri = "https://slothub.dev/errors/";

    public static void Enrich(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;

        if (!problem.Extensions.TryGetValue("code", out var codeValue) || codeValue is not string code)
        {
            code = DefaultCodeFor(status);
            problem.Extensions["code"] = code;
        }

        problem.Type = TypeBaseUri + code.ToLowerInvariant().Replace('_', '-');
        problem.Title ??= Humanize(code);
        problem.Instance ??= context.HttpContext.Request.Path;
        problem.Extensions["correlationId"] = CorrelationId.Get(context.HttpContext);
        problem.Extensions.Remove("traceId");
    }

    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.PaymentRequired => StatusCodes.Status402PaymentRequired,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorKind.Timeout => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>"SLOT_UNAVAILABLE" → "Slot unavailable".</summary>
    public static string Humanize(string code)
    {
        var words = code.Replace('_', ' ').ToLowerInvariant();
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string DefaultCodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ErrorCodes.BadRequest,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        StatusCodes.Status500InternalServerError => ErrorCodes.InternalError,
        _ => JsonNamingPolicy.SnakeCaseUpper.ConvertName(((System.Net.HttpStatusCode)status).ToString()),
    };
}

using BuildingBlocks.Domain;
using BuildingBlocks.Validation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Errors;

/// <summary>Maps exceptions to Problem Details. Domain errors keep their code; anything unknown becomes a 500.</summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, detail) = exception switch
        {
            DomainException domain => (ProblemDetailsSetup.StatusFor(domain.Kind), domain.Code, domain.Message),
            BadHttpRequestException badRequest => (badRequest.StatusCode, ErrorCodes.BadRequest, badRequest.InnerException?.Message ?? badRequest.Message),
            _ => (StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, "An unexpected error occurred."),
        };

        if (exception is not DomainException)
        {
            logger.LogError(exception, "Request failed with {Code}", code);
        }
        else if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogWarning("Request failed with {Code}: {Detail}", code, detail);
        }
        else
        {
            logger.LogInformation("Request rejected with {Code}: {Detail}", code, detail);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = ProblemDetailsSetup.Humanize(code),
            Detail = detail,
            Extensions = { ["code"] = code },
        };

        if (exception is RequestValidationException validation)
        {
            problem.Extensions["errors"] = validation.Errors;
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}

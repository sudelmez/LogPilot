using LogPilot.AI.Providers;
using LogPilot.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Api;

internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        switch (exception)
        {
            case RequestValidationException validation:
                return await WriteAsync(context, exception, new ValidationProblemDetails(validation.Errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Validation failed"
                });

            case NotFoundException:
                return await WriteAsync(context, exception, new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Not found",
                    Detail = exception.Message
                });

            case ConflictException:
                return await WriteAsync(context, exception, new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Conflict",
                    Detail = exception.Message
                });

            case LlmUnavailableException:
                return await WriteAsync(context, exception, new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = "AI service unavailable",
                    Detail = exception.Message   // anahtar/ayrıntı içermeyen, kullanıcıya uygun mesaj
                });

            case OperationCanceledException when context.RequestAborted.IsCancellationRequested:
                context.Response.StatusCode = 499;   // istemci vazgeçti, hata değil
                return true;

            case OperationCanceledException:         // bizim sorgu zaman aşımımız
                return await WriteAsync(context, exception, new ProblemDetails
                {
                    Status = StatusCodes.Status504GatewayTimeout,
                    Title = "The database query timed out."
                });

            default:
                return false;   // framework 500 ProblemDetails üretir
        }
    }

    private async ValueTask<bool> WriteAsync(HttpContext context, Exception exception, ProblemDetails details)
    {
        context.Response.StatusCode = details.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = details
        });
    }
}

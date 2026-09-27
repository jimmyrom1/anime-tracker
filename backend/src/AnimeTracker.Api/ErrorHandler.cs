using AnimeTracker.Api.AniList;
using AnimeTracker.Api.Lists;
using AnimeTracker.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AnimeTracker.Api;

/// <summary>
/// Traduce las excepciones de negocio a respuestas HTTP con formato ProblemDetails. Así los
/// servicios lanzan errores con significado y los endpoints no se llenan de try/catch.
/// </summary>
public class ErrorHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        ProblemDetails? problem = exception switch
        {
            DomainException e => new ValidationProblemDetails(new Dictionary<string, string[]> { [e.Field] = [e.Message] })
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Datos no válidos",
                Extensions = { ["code"] = e.Code },
            },
            NotFoundException e => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = e.Message },
            ConflictException e => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = e.Message },
            AniListUnavailableException e => new ProblemDetails { Status = StatusCodes.Status503ServiceUnavailable, Title = e.Message },
            BadHttpRequestException e => new ProblemDetails { Status = e.StatusCode, Title = "Petición no válida", Detail = e.Message },
            _ => null,
        };
        if (problem is null) return false;

        http.Response.StatusCode = problem.Status!.Value;
        if (problem.Status == StatusCodes.Status503ServiceUnavailable) http.Response.Headers.RetryAfter = "60";
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem, Exception = exception });
    }
}

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Scolia.Application.Services;
using Scolia.Domain.Common;

namespace Scolia.Api.Security;

/// <summary>Toutes les erreurs suivent la norme RFC 9457 (Problem Details) avec un message clair en français.</summary>
public sealed class ProblemHandler(IProblemDetailsService problems, ILogger<ProblemHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, code) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, exception.Message, "introuvable"),
            ConflictException => (StatusCodes.Status409Conflict, exception.Message, "conflit"),
            DuplicateStudentException d => (StatusCodes.Status409Conflict, d.Message, d.Code),
            DomainException d => (StatusCodes.Status400BadRequest, d.Message, d.Code),
            FileNotFoundException => (StatusCodes.Status404NotFound, exception.Message, "introuvable"),
            InvalidDataException => (StatusCodes.Status400BadRequest, exception.Message, "donnees"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Requête invalide.", "requete"),
            OperationCanceledException => (499, "Requête interrompue.", "annule"),
            _ => (StatusCodes.Status500InternalServerError, "Une erreur inattendue s'est produite. Elle a été consignée dans le journal technique.", "interne"),
        };
        if (status >= 500) logger.LogError(exception, "Erreur non gérée sur {Path}", http.Request.Path);
        http.Response.StatusCode = status;
        var details = new ProblemDetails { Status = status, Title = title, Type = $"https://scolia.app/erreurs/{code}" };
        details.Extensions["code"] = code;
        if (exception is DomainException { Errors: not null } domain) details.Extensions["errors"] = domain.Errors;
        if (exception is DuplicateStudentException duplicate) details.Extensions["candidates"] = duplicate.Candidates;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = details, Exception = exception });
    }
}

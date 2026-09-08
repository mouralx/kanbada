using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Kanbada.Api;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            ApiError e => e.Status,
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => 409,
            Npgsql.PostgresException { SqlState: "40001" or "40P01" } => 409,
            Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "40001" or "40P01" or "23505" } } => 409,
            Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23503" or "23514" } } => 400,
            BadHttpRequestException e => e.StatusCode,
            System.Text.Json.JsonException => 400,
            FormatException => 400,
            ArgumentException => 400,
            _ => 500
        };
        if (status == 500)
            logger.LogError(exception, "Unhandled error for {Method} {Path}", context.Request.Method, context.Request.Path);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = new ProblemDetails { Status = status, Title = status >= 500 ? "Server error" : "Request failed", Detail = exception is ApiError ? exception.Message : status == 400 ? "Invalid request data." : "The request could not be completed.", Instance = context.Request.Path, Extensions = { ["traceId"] = context.TraceIdentifier } } });
    }
}

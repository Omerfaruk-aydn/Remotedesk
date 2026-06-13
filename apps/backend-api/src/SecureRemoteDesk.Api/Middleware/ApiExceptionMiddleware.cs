using System.Net;
using SecureRemoteDesk.Application.Contracts;

namespace SecureRemoteDesk.Api.Middleware;

public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (UnauthorizedAccessException ex)
        {
            await WriteAsync(context, HttpStatusCode.Unauthorized, "unauthorized", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, "bad_request", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled API error");
            await WriteAsync(context, HttpStatusCode.InternalServerError, "internal_error", "An unexpected error occurred.");
        }
    }

    private static async Task WriteAsync(HttpContext context, HttpStatusCode status, string code, string message)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ErrorEnvelope(new ErrorBody(code, message)));
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HostCraft.Api.Models.Errors;

/// <summary>
/// Standard error payload for API responses.
/// </summary>
public class ApiError : ProblemDetails
{
    public string Code { get; set; } = "error";
    public IDictionary<string, string[]>? Errors { get; set; }
    public string? TraceId { get; set; }

    public void ApplyDefaults(HttpContext httpContext)
    {
        Status ??= StatusCodes.Status500InternalServerError;
        Title ??= "Unexpected error";
        Detail ??= "An unexpected error occurred.";
        Type ??= $"https://errors.hostcraft.app/{Status}";
        Instance ??= httpContext.Request.Path;
        TraceId ??= httpContext.TraceIdentifier;
    }
}

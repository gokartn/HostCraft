using Microsoft.AspNetCore.Http;

namespace HostCraft.Api.Services;

public class ApiActionResult
{
    public bool Success { get; }
    public int StatusCode { get; }
    public string? Error { get; }

    protected ApiActionResult(bool success, int statusCode, string? error = null)
    {
        Success = success;
        StatusCode = statusCode;
        Error = error;
    }

    public static ApiActionResult Ok(int statusCode = StatusCodes.Status200OK) => new(true, statusCode);
    public static ApiActionResult NoContent() => new(true, StatusCodes.Status204NoContent);
    public static ApiActionResult Fail(int statusCode, string error) => new(false, statusCode, error);
}

public sealed class ApiActionResult<T> : ApiActionResult
{
    public T? Data { get; }

    private ApiActionResult(bool success, int statusCode, T? data = default, string? error = null)
        : base(success, statusCode, error)
    {
        Data = data;
    }

    public static ApiActionResult<T> Ok(T data, int statusCode = StatusCodes.Status200OK) =>
        new(true, statusCode, data);

    public new static ApiActionResult<T> NoContent() => new(true, StatusCodes.Status204NoContent);

    public new static ApiActionResult<T> Fail(int statusCode, string error) =>
        new(false, statusCode, default, error);
}

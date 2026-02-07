using Microsoft.AspNetCore.Http;

namespace HostCraft.Api.Services;

public class AuthActionResult
{
    public bool Success { get; }
    public int StatusCode { get; }
    public string? Error { get; }

    protected AuthActionResult(bool success, int statusCode, string? error = null)
    {
        Success = success;
        StatusCode = statusCode;
        Error = error;
    }

    public static AuthActionResult Ok() => new(true, StatusCodes.Status200OK);
    public static AuthActionResult NoContent() => new(true, StatusCodes.Status204NoContent);
    public static AuthActionResult Fail(int statusCode, string error) => new(false, statusCode, error);
}

public sealed class AuthActionResult<T> : AuthActionResult
{
    public T? Data { get; }

    private AuthActionResult(bool success, int statusCode, T? data = default, string? error = null)
        : base(success, statusCode, error)
    {
        Data = data;
    }

    public static AuthActionResult<T> Ok(T data, int statusCode = StatusCodes.Status200OK) =>
        new(true, statusCode, data);

    public new static AuthActionResult<T> NoContent() => new(true, StatusCodes.Status204NoContent);

    public new static AuthActionResult<T> Fail(int statusCode, string error) =>
        new(false, statusCode, default, error);
}

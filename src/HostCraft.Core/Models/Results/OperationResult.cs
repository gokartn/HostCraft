namespace HostCraft.Core.Models.Results;

/// <summary>
/// Standard result wrapper for service operations. Avoids ad-hoc anonymous error payloads.
/// </summary>
public class OperationResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public List<string> Errors { get; init; } = new();

    public static OperationResult<T> SuccessResult(T data) => new()
    {
        Success = true,
        Data = data
    };

    public static OperationResult<T> Failure(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };

    public static OperationResult<T> Failure(IEnumerable<string> errors) => new()
    {
        Success = false,
        Errors = errors.ToList()
    };
}

public static class OperationResult
{
    public static OperationResult<T> Success<T>(T data) => OperationResult<T>.SuccessResult(data);
    public static OperationResult<T> Failure<T>(string error) => OperationResult<T>.Failure(error);
    public static OperationResult<T> Failure<T>(IEnumerable<string> errors) => OperationResult<T>.Failure(errors);
}
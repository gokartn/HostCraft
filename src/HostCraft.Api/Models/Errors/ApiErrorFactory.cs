using System.Reflection;
using FluentValidation;
using HostCraft.Core.Models.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace HostCraft.Api.Models.Errors;

/// <summary>
/// Builds ApiError instances from exceptions and controller results to keep error responses consistent.
/// </summary>
public class ApiErrorFactory
{
    private const int ClientClosedStatus = 499;
    private readonly IHostEnvironment _hostEnvironment;

    public ApiErrorFactory(IHostEnvironment hostEnvironment)
    {
        _hostEnvironment = hostEnvironment;
    }

    public ApiError FromException(Exception exception, HttpContext httpContext)
    {
        return exception switch
        {
            ValidationException validationException => FromValidationException(validationException, httpContext),
            UnauthorizedAccessException => Create(httpContext, StatusCodes.Status403Forbidden, "Forbidden", "You are not allowed to perform this action.", "forbidden"),
            OperationCanceledException => Create(httpContext, ClientClosedStatus, "Request cancelled", "The request was cancelled by the client.", "request_cancelled"),
            _ => Create(httpContext, StatusCodes.Status500InternalServerError, "Unexpected error", _hostEnvironment.IsDevelopment() ? exception.Message : "An unexpected error occurred.", "unexpected_error")
        };
    }

    public ApiError FromValidationException(ValidationException exception, HttpContext httpContext)
    {
        var errors = exception.Errors
            .GroupBy(e => string.IsNullOrWhiteSpace(e.PropertyName) ? "general" : e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        return Create(httpContext, StatusCodes.Status400BadRequest, "Validation failed", "One or more validation errors occurred.", "validation_failed", errors);
    }

    public ApiError FromObjectResult(object? value, int statusCode, HttpContext httpContext)
    {
        if (value is ApiError apiError)
        {
            apiError.Status ??= statusCode;
            apiError.ApplyDefaults(httpContext);
            return apiError;
        }

        if (value is ValidationProblemDetails validationProblem)
        {
            return Create(httpContext, statusCode, validationProblem.Title ?? "Validation failed", validationProblem.Detail ?? "One or more validation errors occurred.", "validation_failed", validationProblem.Errors);
        }

        if (value is ProblemDetails problemDetails)
        {
            var errors = problemDetails.Extensions.TryGetValue("errors", out var existingErrors) && existingErrors is IDictionary<string, string[]> typedErrors
                ? typedErrors
                : null;

            var apiProblem = new ApiError
            {
                Status = statusCode,
                Title = problemDetails.Title ?? GetDefaultTitle(statusCode),
                Detail = problemDetails.Detail ?? GetDefaultDetail(statusCode),
                Type = problemDetails.Type,
                Instance = problemDetails.Instance,
                Errors = errors,
                TraceId = problemDetails.Extensions.TryGetValue("traceId", out var trace) ? trace?.ToString() : null,
                Code = problemDetails.Extensions.TryGetValue("code", out var code) ? code?.ToString() ?? GetDefaultCode(statusCode) : GetDefaultCode(statusCode)
            };

            apiProblem.ApplyDefaults(httpContext);
            return apiProblem;
        }

        var extractedErrors = ExtractErrors(value);
        var detail = ExtractDetail(value) ?? GetDefaultDetail(statusCode);
        var titleText = GetDefaultTitle(statusCode);
        var codeText = GetDefaultCode(statusCode);

        return Create(httpContext, statusCode, titleText, detail, codeText, extractedErrors);
    }

    public ApiError FromStatusCode(int statusCode, HttpContext httpContext)
    {
        return Create(httpContext, statusCode, GetDefaultTitle(statusCode), GetDefaultDetail(statusCode), GetDefaultCode(statusCode));
    }

    private static ApiError Create(HttpContext httpContext, int statusCode, string title, string detail, string code, IDictionary<string, string[]>? errors = null)
    {
        var apiError = new ApiError
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Code = code,
            Errors = errors,
            Instance = httpContext.Request.Path,
            TraceId = httpContext.TraceIdentifier,
            Type = $"https://errors.hostcraft.app/{statusCode}"
        };

        apiError.ApplyDefaults(httpContext);
        return apiError;
    }

    private static string GetDefaultTitle(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "Bad request",
            StatusCodes.Status401Unauthorized => "Unauthorized",
            StatusCodes.Status403Forbidden => "Forbidden",
            StatusCodes.Status404NotFound => "Not found",
            StatusCodes.Status409Conflict => "Conflict",
            StatusCodes.Status422UnprocessableEntity => "Unprocessable entity",
            ClientClosedStatus => "Request cancelled",
            StatusCodes.Status500InternalServerError => "Server error",
            _ when statusCode >= 500 => "Server error",
            _ => "Request failed"
        };
    }

    private static string GetDefaultDetail(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "The request could not be processed.",
            StatusCodes.Status401Unauthorized => "Authentication is required to access this resource.",
            StatusCodes.Status403Forbidden => "You are not allowed to perform this action.",
            StatusCodes.Status404NotFound => "The requested resource was not found.",
            StatusCodes.Status409Conflict => "The request could not be completed due to a conflict.",
            StatusCodes.Status422UnprocessableEntity => "The request was well-formed but could not be processed.",
            ClientClosedStatus => "The request was cancelled by the client.",
            _ when statusCode >= 500 => "An unexpected server error occurred.",
            _ => "The request could not be completed."
        };
    }

    private static string GetDefaultCode(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "bad_request",
            StatusCodes.Status401Unauthorized => "unauthorized",
            StatusCodes.Status403Forbidden => "forbidden",
            StatusCodes.Status404NotFound => "not_found",
            StatusCodes.Status409Conflict => "conflict",
            StatusCodes.Status422UnprocessableEntity => "unprocessable_entity",
            ClientClosedStatus => "request_cancelled",
            _ when statusCode >= 500 => "server_error",
            _ => "error"
        };
    }

    private static IDictionary<string, string[]>? ExtractErrors(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is IDictionary<string, string[]> dictionary)
        {
            return new Dictionary<string, string[]>(dictionary);
        }

        var type = value.GetType();
        var errorsProperty = type.GetProperty("Errors", BindingFlags.Public | BindingFlags.Instance);

        if (errorsProperty?.GetValue(value) is IDictionary<string, string[]> errorsDictionary)
        {
            return new Dictionary<string, string[]>(errorsDictionary);
        }

        if (errorsProperty?.GetValue(value) is IEnumerable<string> flatErrors)
        {
            return new Dictionary<string, string[]>
            {
                ["errors"] = flatErrors.ToArray()
            };
        }

        var errorDetailsProperty = type.GetProperty("ErrorDetails", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (errorDetailsProperty?.GetValue(value) is string errorDetails && !string.IsNullOrWhiteSpace(errorDetails))
        {
            return new Dictionary<string, string[]>
            {
                ["errors"] = new[] { errorDetails }
            };
        }

        if (TryReadOperationResultFailure(value, out _, out var opErrors) && opErrors is not null && opErrors.Any())
        {
            return new Dictionary<string, string[]>
            {
                ["errors"] = opErrors.ToArray()
            };
        }

        return null;
    }

    private static string? ExtractDetail(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string stringValue && !string.IsNullOrWhiteSpace(stringValue))
        {
            return stringValue;
        }

        if (TryReadOperationResultFailure(value, out var opErrorMessage, out var opErrors))
        {
            return opErrorMessage ?? opErrors?.FirstOrDefault();
        }

        var fromProperty = TryGetStringProperty(value, "error", "Error", "message", "Message", "detail", "Detail", "ErrorDetails", "errorDetails", "Title", "title");
        if (!string.IsNullOrWhiteSpace(fromProperty))
        {
            return fromProperty;
        }

        return value.ToString();
    }

    private static string? TryGetStringProperty(object value, params string[] propertyNames)
    {
        var type = value.GetType();

        foreach (var name in propertyNames)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property?.GetValue(value) is string stringValue && !string.IsNullOrWhiteSpace(stringValue))
            {
                return stringValue;
            }

            if (value is IDictionary<string, object> objectDictionary && objectDictionary.TryGetValue(name, out var dictValue) && dictValue is string dictString && !string.IsNullOrWhiteSpace(dictString))
            {
                return dictString;
            }
        }

        return null;
    }

    private static bool TryReadOperationResultFailure(object value, out string? errorMessage, out IEnumerable<string>? errors)
    {
        errorMessage = null;
        errors = null;

        var type = value.GetType();
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(OperationResult<>))
        {
            return false;
        }

        var successProperty = type.GetProperty("Success", BindingFlags.Public | BindingFlags.Instance);
        if (successProperty?.GetValue(value) is bool success && success)
        {
            return false;
        }

        var errorMessageProperty = type.GetProperty("ErrorMessage", BindingFlags.Public | BindingFlags.Instance);
        errorMessage = errorMessageProperty?.GetValue(value)?.ToString();

        var errorsProperty = type.GetProperty("Errors", BindingFlags.Public | BindingFlags.Instance);
        if (errorsProperty?.GetValue(value) is IEnumerable<string> opErrors)
        {
            errors = opErrors;
        }

        return true;
    }
}

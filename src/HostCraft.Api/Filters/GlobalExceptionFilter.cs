using FluentValidation;
using HostCraft.Api.Models.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HostCraft.Api.Filters;

/// <summary>
/// Centralized exception handler that converts known exceptions into consistent API responses.
/// </summary>
public class GlobalExceptionFilter : IExceptionFilter
{
    private readonly ILogger<GlobalExceptionFilter> _logger;
    private readonly ApiErrorFactory _apiErrorFactory;

    public GlobalExceptionFilter(ApiErrorFactory apiErrorFactory, ILogger<GlobalExceptionFilter> logger)
    {
        _apiErrorFactory = apiErrorFactory;
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        var exception = context.Exception;

        var response = exception switch
        {
            ValidationException ex => _apiErrorFactory.FromValidationException(ex, context.HttpContext),
            _ => _apiErrorFactory.FromException(exception, context.HttpContext)
        };

        _logger.LogError(exception, "Exception handled by global filter: {ExceptionType}", exception.GetType().Name);

        context.Result = new ObjectResult(response)
        {
            StatusCode = response.Status
        };

        context.ExceptionHandled = true;
    }
}

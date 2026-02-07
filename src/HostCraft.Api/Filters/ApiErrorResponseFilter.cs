using HostCraft.Api.Models.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HostCraft.Api.Filters;

/// <summary>
/// Normalizes all error responses into the ApiError shape so callers receive consistent payloads.
/// </summary>
public class ApiErrorResponseFilter : IAsyncResultFilter
{
    private readonly ApiErrorFactory _apiErrorFactory;

    public ApiErrorResponseFilter(ApiErrorFactory apiErrorFactory)
    {
        _apiErrorFactory = apiErrorFactory;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult objectResult)
        {
            var statusCode = objectResult.StatusCode ?? context.HttpContext.Response.StatusCode;

            if (statusCode >= 400)
            {
                var apiError = _apiErrorFactory.FromObjectResult(objectResult.Value, statusCode, context.HttpContext);
                context.Result = new ObjectResult(apiError)
                {
                    StatusCode = apiError.Status
                };
            }
        }
        else if (context.Result is StatusCodeResult statusCodeResult && statusCodeResult.StatusCode >= 400)
        {
            var apiError = _apiErrorFactory.FromStatusCode(statusCodeResult.StatusCode, context.HttpContext);
            context.Result = new ObjectResult(apiError)
            {
                StatusCode = apiError.Status
            };
        }
        else if (context.Result is ForbidResult)
        {
            var apiError = _apiErrorFactory.FromStatusCode(StatusCodes.Status403Forbidden, context.HttpContext);
            context.Result = new ObjectResult(apiError)
            {
                StatusCode = apiError.Status
            };
        }
        else if (context.Result is UnauthorizedResult)
        {
            var apiError = _apiErrorFactory.FromStatusCode(StatusCodes.Status401Unauthorized, context.HttpContext);
            context.Result = new ObjectResult(apiError)
            {
                StatusCode = apiError.Status
            };
        }

        await next();
    }
}

using Microsoft.AspNetCore.Diagnostics;
using ThreeDErp.Application.Common;

namespace ThreeDErp.Api.Middleware;

/// <summary>
/// 将 Application 层抛出的业务异常统一映射为 ProblemDetails 响应：
/// ValidationException -> 400 ValidationProblem，NotFoundException -> 404，ConflictException -> 409。
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case ValidationException validationException:
                await Results
                    .ValidationProblem(
                        new Dictionary<string, string[]>(validationException.Errors),
                        title: "请求校验失败",
                        type: "https://tools.ietf.org/html/rfc9110#section-15.5.1")
                    .ExecuteAsync(httpContext);
                return true;

            case ConflictException conflictException:
                await Results
                    .Problem(
                        title: "请求冲突",
                        detail: conflictException.Message,
                        statusCode: StatusCodes.Status409Conflict,
                        type: "https://tools.ietf.org/html/rfc9110#section-15.5.10")
                    .ExecuteAsync(httpContext);
                return true;

            case NotFoundException notFoundException:
                await Results
                    .Problem(
                        title: "资源不存在",
                        detail: notFoundException.Message,
                        statusCode: StatusCodes.Status404NotFound,
                        type: "https://tools.ietf.org/html/rfc9110#section-15.5.5")
                    .ExecuteAsync(httpContext);
                return true;

            default:
                logger.LogError(exception, "Unhandled exception while processing {Path}.", httpContext.Request.Path);
                return false;
        }
    }
}

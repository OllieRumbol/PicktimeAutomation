using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using PicktimeAutomation.AzureFunctions.Extensions;
using PicktimeAutomation.Models;

namespace PicktimeAutomation.AzureFunctions.Middleware;

/// <summary>
/// The one place unexpected errors are handled, for every function (plan section 3).
/// An unhandled exception fails one run, not the host. This middleware exists so the run
/// still appears in the run record, and so an HTTP caller gets no internal detail.
/// </summary>
public sealed class ExceptionHandlingMiddleware : IFunctionsWorkerMiddleware
{
    public const string FailedRunMessage = "The booking run failed. See the logs.";

    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(ILogger<ExceptionHandlingMiddleware> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, context))
        {
            _logger.LogError(exception, "Function {FunctionName} failed with an unexpected error.", context.FunctionDefinition.Name);

            // The middleware does not know the booking date, so it is left empty (plan section 3.1).
            _logger.LogBookingSummary(new BookingSummary { Verdict = RunVerdict.Error });

            // Null for a function that is not triggered by HTTP, such as the timer.
            var httpContext = context.GetHttpContext();
            if (httpContext is null)
            {
                // Rethrown so the host still records the invocation as failed.
                throw;
            }

            await WriteFailedRunResponseAsync(httpContext);
        }
    }

    // The caller's own cancellation is not an error of the run, so it is never handled here.
    private static bool IsCallerCancellation(Exception exception, FunctionContext context)
    {
        return exception is OperationCanceledException && context.CancellationToken.IsCancellationRequested;
    }

    private static async Task WriteFailedRunResponseAsync(HttpContext httpContext)
    {
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        var problem = Results.Problem(title: FailedRunMessage, statusCode: StatusCodes.Status500InternalServerError);
        await problem.ExecuteAsync(httpContext);
    }
}

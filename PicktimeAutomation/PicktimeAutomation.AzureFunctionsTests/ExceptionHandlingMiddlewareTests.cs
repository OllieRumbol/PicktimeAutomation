using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using PicktimeAutomation.AzureFunctions.Middleware;
using PicktimeAutomation.Models;

namespace PicktimeAutomation.AzureFunctionsTests;

/// <summary>
/// Test 27 (plan section 7.7), for <see cref="ExceptionHandlingMiddleware"/>.
/// </summary>
[TestClass]
public sealed class ExceptionHandlingMiddlewareTests
{
    private const string InternalDetail = "Internal detail that must not reach the caller";

    // Test 27
    [TestMethod]
    public async Task Invoke_HttpFunctionThrows_LogsErrorSummaryAndReturns500WithNoInternalDetail()
    {
        var logger = new FakeLogger<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(logger);
        var httpContext = TestHttp.CreateContext();
        var context = new FakeFunctionContext(httpContext);

        await middleware.Invoke(context, ThrowUnexpectedError);

        var error = logger.Collector.GetSnapshot().Single(record => record.Level == LogLevel.Error);
        Assert.IsInstanceOfType<InvalidOperationException>(error.Exception);
        Assert.IsTrue(SummaryLog.HasVerdict(logger.Collector, RunVerdict.Error));

        var body = TestHttp.ReadBody(httpContext);
        Assert.AreEqual(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Assert.StartsWith("application/problem+json", httpContext.Response.ContentType);
        Assert.DoesNotContain(InternalDetail, body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);

        using var problem = JsonDocument.Parse(body);
        Assert.AreEqual(ExceptionHandlingMiddleware.FailedRunMessage, problem.RootElement.GetProperty("title").GetString());
    }

    // Test 27, for a function that is not triggered by HTTP, such as the timer.
    [TestMethod]
    // It is rethrown, so the host still records the invocation as failed.
    public async Task Invoke_TimerFunctionThrows_LogsErrorAndSummaryAndRethrows()
    {
        var logger = new FakeLogger<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(logger);
        var context = new FakeFunctionContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(context, ThrowUnexpectedError));

        var error = logger.Collector.GetSnapshot().Single(record => record.Level == LogLevel.Error);
        Assert.IsInstanceOfType<InvalidOperationException>(error.Exception);
        Assert.IsTrue(SummaryLog.HasVerdict(logger.Collector, RunVerdict.Error));
    }

    // A timeout also throws OperationCanceledException, but the caller did not cancel, so it is a failed run.
    [TestMethod]
    public async Task Invoke_TimeoutWhileCallerHasNotCancelled_RecordsAnErrorRun()
    {
        var logger = new FakeLogger<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(logger);
        var httpContext = TestHttp.CreateContext();
        var context = new FakeFunctionContext(httpContext);

        await middleware.Invoke(context, _ => throw new TaskCanceledException("The request timed out."));

        Assert.IsTrue(SummaryLog.HasVerdict(logger.Collector, RunVerdict.Error));
        Assert.AreEqual(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
    }

    // The caller's own cancellation is not a failed run, so it is not recorded as one.
    [TestMethod]
    public async Task Invoke_CallerCancels_RethrowsAndRecordsNoError()
    {
        var logger = new FakeLogger<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(logger);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new FakeFunctionContext(cancellationToken: cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => middleware.Invoke(context, functionContext => throw new OperationCanceledException(functionContext.CancellationToken)));

        Assert.AreEqual(0, logger.Collector.Count);
    }

    private static Task ThrowUnexpectedError(FunctionContext context)
    {
        throw new InvalidOperationException(InternalDetail);
    }
}

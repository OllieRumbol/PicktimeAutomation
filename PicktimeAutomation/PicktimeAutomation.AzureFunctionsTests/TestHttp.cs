using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace PicktimeAutomation.AzureFunctionsTests;

/// <summary>
/// Builds a plain <see cref="DefaultHttpContext"/>, and writes an <see cref="IResult"/> to it as the host would.
/// </summary>
internal static class TestHttp
{
    public static DefaultHttpContext CreateContext(string queryString = "")
    {
        var httpContext = new DefaultHttpContext
        {
            // The built-in results resolve a logger factory from the request services.
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.QueryString = new QueryString(queryString);
        httpContext.Response.Body = new MemoryStream();

        return httpContext;
    }

    public static async Task<string> ExecuteAsync(IResult result, HttpContext httpContext)
    {
        await result.ExecuteAsync(httpContext);

        return ReadBody(httpContext);
    }

    public static string ReadBody(HttpContext httpContext)
    {
        var body = (MemoryStream)httpContext.Response.Body;

        return Encoding.UTF8.GetString(body.ToArray());
    }
}

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;
using PicktimeAutomation.AzureFunctions.Extensions;
using PicktimeAutomation.AzureFunctions.Middleware;
using PicktimeAutomation.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// After ConfigureFunctionsWebApplication, so the HTTP context is available to the middleware.
builder.UseMiddleware<ExceptionHandlingMiddleware>();

builder.Services.AddWorkerTelemetry(builder.Configuration);

builder.Services.AddPicktimeServices(builder.Configuration);

builder.Build().Run();

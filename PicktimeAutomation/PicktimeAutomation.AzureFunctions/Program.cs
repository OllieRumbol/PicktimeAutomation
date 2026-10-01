using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PicktimeAutomation.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddHttpClient<IPicktimeApiService, PicktimeApiService>(client =>
{
    client.BaseAddress = new Uri("https://www.picktime.com/");
    client.DefaultRequestHeaders.Add("scantoken", "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJhY2NvdW50SWQiOiI0ZmNjMTViNy02NjNkLTQzMjAtOWQyMy1iYzFmOGZjMGI2NjkiLCJpc3MiOiJQVCIsImlhdCI6MTc3NDQ2NzY0M30.tQs-HzOBAm5FfnlmVC42bGeaDJEvmpc-IKwGtZLDNbQ");
});
builder.Services.AddTransient<IPicktimeBookingService, PicktimeBookingService>();

builder.Build().Run();

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PicktimeAutomation.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddHttpClient<IPicktimeApiService, PicktimeApiService>(client =>
        {
            client.BaseAddress = new Uri("https://www.picktime.com/");
            client.DefaultRequestHeaders.Add("scantoken", "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJhY2NvdW50SWQiOiI0ZmNjMTViNy02NjNkLTQzMjAtOWQyMy1iYzFmOGZjMGI2NjkiLCJpc3MiOiJQVCIsImlhdCI6MTc3NDQ2NzY0M30.tQs-HzOBAm5FfnlmVC42bGeaDJEvmpc-IKwGtZLDNbQ");
        });
        services.AddTransient<IPicktimeBookingService, PicktimeBookingService>();
    })
    .Build();

host.Run();
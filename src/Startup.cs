using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PdfConverterFunction;
using PdfConverterFunction.Models;
using Polly;
using System;

[assembly: FunctionsStartup(typeof(Startup))]
namespace PdfConverterFunction
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services
                .AddLogging()
                .AddHttpClient("Resillient")
                .AddTransientHttpErrorPolicy(policyBuilder => policyBuilder.CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(15)
                ));

            builder.Services.AddOptions<BlobStorageConfiguration>().Configure<IConfiguration>((settings, config) => settings.ConnectionString = config["AzureWebJobsStorage"]);
        }
    }
}

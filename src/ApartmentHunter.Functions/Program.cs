using ApartmentHunter.Core.Models;
using ApartmentHunter.Core.Scrapers;
using ApartmentHunter.Infrastructure.Sms;
using ApartmentHunter.Infrastructure.Storage;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

const string KeyVaultUri = "https://recherche-appart-kv.vault.azure.net/";

// Mapping nom secret Key Vault → clé de configuration .NET
var secretMappings = new Dictionary<string, string>
{
    ["OVH-AppKey"]        = "OvhSms:AppKey",
    ["OVH-AppSecret"]     = "OvhSms:AppSecret",
    ["OVH-ConsumerKey"]   = "OvhSms:ConsumerKey",
    ["OVH-ServiceName"]   = "OvhSms:ServiceName",
    ["OVH-MobileNumber1"] = "OvhSms:RecipientPhoneNumbers:0",
    ["OVH-MobileNumber2"] = "OvhSms:RecipientPhoneNumbers:1",
    // Credentials scrapers Jinka et GensDeConfiance (login automatique)
    ["Jinka-Username"]    = "Scrapers:Jinka:Username",
    ["Jinka-Password"]    = "Scrapers:Jinka:Password",
    ["GDC-Username"]      = "Scrapers:GensDeConfiance:Username",
    ["GDC-Password"]      = "Scrapers:GensDeConfiance:Password",
};

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration((context, builder) =>
    {
        // En local : secrets dans local.settings.json
        // En production : lecture depuis Azure Key Vault via Managed Identity
        if (!context.HostingEnvironment.IsDevelopment())
        {
            var kvClient = new SecretClient(new Uri(KeyVaultUri), new DefaultAzureCredential());
            var overrides = new Dictionary<string, string?>();

            foreach (var (secretName, configKey) in secretMappings)
            {
                try
                {
                    var secret = kvClient.GetSecretAsync(secretName).GetAwaiter().GetResult();
                    overrides[configKey] = secret.Value.Value;
                }
                catch
                {
                    // Secret pas encore disponible (ex: OVH-ServiceName en cours de provisionnement)
                }
            }

            builder.AddInMemoryCollection(overrides);
        }
    })
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddHttpClient();

        services.Configure<SearchCriteria>(config.GetSection("SearchCriteria"));
        services.Configure<ScraperOptions>(config.GetSection("Scrapers"));
        services.Configure<OvhSmsOptions>(config.GetSection("OvhSms"));

        services.AddTransient<IListingScraper, PapScraper>();
        services.AddTransient<IListingScraper, LeBonCoinScraper>();
        services.AddTransient<IListingScraper, SeLogerScraper>();
        services.AddTransient<IListingScraper, JinkaScraper>();
        services.AddTransient<IListingScraper, GensDeConfianceScraper>();

        var storageConnection = config["AzureWebJobsStorage"]
            ?? throw new InvalidOperationException("AzureWebJobsStorage manquant");
        services.AddSingleton(_ => new TableServiceClient(storageConnection));
        services.AddSingleton<ISeenListingsRepository, AzureTableSeenListingsRepository>();

        services.AddHttpClient<ISmsService, OvhSmsService>();
    })
    .Build();

host.Run();

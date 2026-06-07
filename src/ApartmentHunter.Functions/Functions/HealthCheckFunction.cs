using System.Net;
using System.Text.Json;
using ApartmentHunter.Core.Scrapers;
using ApartmentHunter.Infrastructure.Sms;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApartmentHunter.Functions.Functions;

public class HealthCheckFunction(
    TableServiceClient tableServiceClient,
    IOptions<OvhSmsOptions> smsOptions,
    IOptions<ScraperOptions> scraperOptions,
    ILogger<HealthCheckFunction> logger)
{
    [Function(nameof(HealthCheckFunction))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req,
        CancellationToken ct)
    {
        var checks = new Dictionary<string, object>();

        // Table Storage
        try
        {
            await tableServiceClient.GetPropertiesAsync(ct);
            checks["tableStorage"] = "ok";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health: Table Storage KO");
            checks["tableStorage"] = $"error: {ex.Message}";
        }

        // OVH SMS config (secrets lus depuis Key Vault en prod)
        var sms = smsOptions.Value;
        checks["ovhSms"] = new
        {
            serviceNameConfigured = !string.IsNullOrEmpty(sms.ServiceName) && !sms.ServiceName.StartsWith("CONFIGURER"),
            recipientCount = sms.RecipientPhoneNumbers.Count
        };

        // Scrapers activés
        var sc = scraperOptions.Value;
        checks["scrapers"] = new
        {
            pap = sc.Pap.Enabled && !string.IsNullOrEmpty(sc.Pap.RssUrl) && !sc.Pap.RssUrl.StartsWith("CONFIGURER"),
            leBonCoin = sc.LeBonCoin.Enabled && !string.IsNullOrEmpty(sc.LeBonCoin.SearchUrl) && !sc.LeBonCoin.SearchUrl.StartsWith("CONFIGURER"),
            seLoger = sc.SeLoger.Enabled && !string.IsNullOrEmpty(sc.SeLoger.SearchUrl) && !sc.SeLoger.SearchUrl.StartsWith("CONFIGURER"),
            jinka = sc.Jinka.Enabled,
            gensDeConfiance = sc.GensDeConfiance.Enabled
        };

        var allOk = checks["tableStorage"] is "ok";
        var statusCode = allOk ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;

        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        var body = JsonSerializer.Serialize(new
        {
            status = allOk ? "healthy" : "degraded",
            timestamp = DateTimeOffset.UtcNow,
            checks
        }, new JsonSerializerOptions { WriteIndented = true });

        await response.WriteStringAsync(body, ct);
        return response;
    }
}

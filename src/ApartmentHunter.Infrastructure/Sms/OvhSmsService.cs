using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApartmentHunter.Infrastructure.Sms;

public class OvhSmsService(
    HttpClient httpClient,
    IOptions<OvhSmsOptions> options,
    ILogger<OvhSmsService> logger) : ISmsService
{
    private readonly OvhSmsOptions _options = options.Value;
    private const string BaseUrl = "https://eu.api.ovh.com/1.0";

    public async Task SendAsync(string message, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/sms/{_options.ServiceName}/jobs";
        var body = JsonSerializer.Serialize(new
        {
            charset = "UTF-8",
            @class = "phoneDisplay",
            coding = "7bit",
            message,
            noStopClause = false,
            priority = "high",
            receivers = _options.RecipientPhoneNumbers.ToArray(),
            senderForResponse = false,
            sender = _options.SenderName
        });

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = ComputeSignature("POST", url, body, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Ovh-Application", _options.AppKey);
        request.Headers.Add("X-Ovh-Timestamp", timestamp);
        request.Headers.Add("X-Ovh-Signature", signature);
        request.Headers.Add("X-Ovh-Consumer", _options.ConsumerKey);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Erreur OVH SMS ({StatusCode}): {Error}", response.StatusCode, error);
            response.EnsureSuccessStatusCode();
        }

        logger.LogInformation("SMS envoyé à {Recipients}", string.Join(", ", _options.RecipientPhoneNumbers));
    }

    private string ComputeSignature(string method, string url, string body, string timestamp)
    {
        // OVH signature: "$1$" + SHA1(appSecret + "+" + consumerKey + "+" + method + "+" + url + "+" + body + "+" + timestamp)
        var toSign = $"{_options.AppSecret}+{_options.ConsumerKey}+{method}+{url}+{body}+{timestamp}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(toSign));
        return "$1$" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}

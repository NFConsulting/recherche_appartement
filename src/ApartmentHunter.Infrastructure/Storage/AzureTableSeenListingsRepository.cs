using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace ApartmentHunter.Infrastructure.Storage;

public class AzureTableSeenListingsRepository(
    TableServiceClient tableServiceClient,
    ILogger<AzureTableSeenListingsRepository> logger) : ISeenListingsRepository
{
    private const string TableName = "SeenListings";
    private TableClient? _tableClient;

    private async Task<TableClient> GetTableClientAsync(CancellationToken ct)
    {
        if (_tableClient is not null) return _tableClient;
        _tableClient = tableServiceClient.GetTableClient(TableName);
        await _tableClient.CreateIfNotExistsAsync(ct);
        return _tableClient;
    }

    public async Task<bool> HasBeenSeenAsync(string listingId, CancellationToken ct = default)
    {
        try
        {
            var table = await GetTableClientAsync(ct);
            var partitionKey = GetPartitionKey(listingId);
            var rowKey = SanitizeKey(listingId);
            var response = await table.GetEntityIfExistsAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
            return response.HasValue;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la vérification de l'annonce {ListingId}", listingId);
            return false;
        }
    }

    public async Task MarkAsSeenAsync(string listingId, string source, string url, CancellationToken ct = default)
    {
        try
        {
            var table = await GetTableClientAsync(ct);
            var entity = new TableEntity(GetPartitionKey(listingId), SanitizeKey(listingId))
            {
                ["Source"] = source,
                ["Url"] = url,
                ["SeenAt"] = DateTimeOffset.UtcNow
            };
            await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la sauvegarde de l'annonce {ListingId}", listingId);
        }
    }

    private static string GetPartitionKey(string listingId)
    {
        // Partition par source (préfixe avant le _)
        var idx = listingId.IndexOf('_');
        return idx > 0 ? listingId[..idx] : "other";
    }

    private static string SanitizeKey(string key) =>
        key.Replace("/", "-").Replace("\\", "-").Replace("#", "-").Replace("?", "-");
}

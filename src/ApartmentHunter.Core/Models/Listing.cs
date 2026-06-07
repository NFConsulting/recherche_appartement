namespace ApartmentHunter.Core.Models;

public record Listing(
    string Id,
    string Source,
    string Title,
    decimal Price,
    int Rooms,
    decimal? Surface,
    string Arrondissement,
    string Url,
    DateTime PublishedAt
);

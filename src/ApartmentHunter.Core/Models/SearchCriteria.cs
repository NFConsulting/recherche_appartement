namespace ApartmentHunter.Core.Models;

public class SearchCriteria
{
    public List<int> Arrondissements { get; set; } = [10, 11, 12, 13, 18, 19, 20];
    public decimal PriceMin { get; set; } = 1800;
    public decimal PriceMax { get; set; } = 2500;
    public int RoomsMin { get; set; } = 3;
    public int RoomsMax { get; set; } = 3;

    public bool Matches(Listing listing)
    {
        if (listing.Price < PriceMin || listing.Price > PriceMax) return false;
        if (listing.Rooms < RoomsMin || listing.Rooms > RoomsMax) return false;
        return true;
    }
}

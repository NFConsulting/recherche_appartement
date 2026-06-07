namespace ApartmentHunter.Core.Scrapers;

public class ScraperOptions
{
    public PapOptions Pap { get; set; } = new();
    public LeBonCoinOptions LeBonCoin { get; set; } = new();
    public SeLogerOptions SeLoger { get; set; } = new();
    public JinkaOptions Jinka { get; set; } = new();
    public GensDeConfianceOptions GensDeConfiance { get; set; } = new();
}

public class PapOptions
{
    public bool Enabled { get; set; } = true;
    public string RssUrl { get; set; } = "";
}

public class LeBonCoinOptions
{
    public bool Enabled { get; set; } = true;
    public string SearchUrl { get; set; } = "";
}

public class SeLogerOptions
{
    public bool Enabled { get; set; } = true;
    public string SearchUrl { get; set; } = "";
}

public class JinkaOptions
{
    public bool Enabled { get; set; } = true;
    public string SearchUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class GensDeConfianceOptions
{
    public bool Enabled { get; set; } = true;
    public string SearchUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

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
    /// <summary>
    /// URL RSS PAP — faire une recherche sur pap.fr et ajouter ?rss=1 à la fin de l'URL
    /// </summary>
    public string RssUrl { get; set; } = "";
}

public class LeBonCoinOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// URL de recherche leboncoin.fr — copier l'URL après avoir fait une recherche filtrée
    /// </summary>
    public string SearchUrl { get; set; } = "";
}

public class SeLogerOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// URL de recherche seloger.com — copier l'URL après avoir fait une recherche filtrée
    /// </summary>
    public string SearchUrl { get; set; } = "";
}

public class JinkaOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// URL de recherche jinka.fr — copier l'URL après avoir fait une recherche filtrée
    /// </summary>
    public string SearchUrl { get; set; } = "";
    /// <summary>
    /// Cookie de session — récupérer dans les DevTools (Application > Cookies) après connexion
    /// </summary>
    public string SessionCookie { get; set; } = "";
}

public class GensDeConfianceOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// URL de recherche gensdeconfiance.fr — copier l'URL après avoir fait une recherche filtrée
    /// </summary>
    public string SearchUrl { get; set; } = "";
    /// <summary>
    /// Cookie de session — récupérer dans les DevTools (Application > Cookies) après connexion
    /// </summary>
    public string SessionCookie { get; set; } = "";
}

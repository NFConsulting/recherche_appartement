namespace ApartmentHunter.Infrastructure.Sms;

public class OvhSmsOptions
{
    /// <summary>Créer sur https://eu.api.ovh.com/createApp/</summary>
    public string AppKey { get; set; } = "";
    public string AppSecret { get; set; } = "";
    /// <summary>Obtenu lors de l'activation du Consumer Key via /auth/credential</summary>
    public string ConsumerKey { get; set; } = "";
    /// <summary>Nom du service SMS OVH (format: sms-xxxxx-1)</summary>
    public string ServiceName { get; set; } = "";
    /// <summary>Nom affiché comme expéditeur (11 caractères max)</summary>
    public string SenderName { get; set; } = "Appart";
    /// <summary>Liste des numéros destinataires (format +33XXXXXXXXX)</summary>
    public List<string> RecipientPhoneNumbers { get; set; } = [];
}

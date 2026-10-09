using System.Text.Json;

namespace Wyrmhold.Core;

/// <summary>
/// Les clés API de l'utilisateur. Elles sont gardées CHIFFRÉES (DPAPI, voir SecureStore) :
/// seul ce compte Windows peut les relire. Un seul objet pour toute l'appli (Secrets.Current) :
/// quand l'utilisateur change une clé, tous ceux qui lisent cet objet voient la nouvelle tout de suite.
/// </summary>
public class Secrets
{
    public string SteamApiKey { get; set; } = "";
    public string SteamId { get; set; } = "";

    public string SteamGridDbApiKey { get; set; } = "";

    // IsThereAnyDeal : la clé API sert aux recherches et aux prix ;
    // le client ID sert à la connexion au compte (OAuth, synchro de la Waitlist).
    // Le client secret n'est pas utilisé (PKCE) : gardé seulement pour relire les anciens fichiers.
    public string IsThereAnyDealApiKey { get; set; } = "";
    public string IsThereAnyDealClientId { get; set; } = "";
    public string IsThereAnyDealClientSecret { get; set; } = "";

    // Le nom du fichier chiffré dans le dossier « tokens » (tokens\api-keys.bin).
    private const string StoreName = "api-keys";

    // L'ancien fichier en clair : importé une fois, puis supprimé.
    private static readonly string LegacyPath = Path.Combine(AppPaths.DataFolder, "secrets.json");

    private static readonly object LoadLock = new object();
    private static Secrets? _current;

    /// <summary>Les clés de l'utilisateur, lues sur le disque au premier accès.</summary>
    public static Secrets Current
    {
        get
        {
            lock (LoadLock)
            {
                return _current ??= LoadFromDisk();
            }
        }
    }

    /// <summary>Prévient l'appli qu'une clé a changé (ex. pour relire la bibliothèque Steam).</summary>
    public static event Action? Changed;

    // Ancien nom, gardé pour ne pas tout réécrire : renvoie le même objet partagé.
    public static Secrets Load() => Current;

    // [JsonIgnore] : calculés à partir des clés, inutile de les enregistrer.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasSteam => SteamApiKey.Length > 0 && SteamId.Length > 0;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasAnyKey => SteamApiKey.Length > 0 || SteamGridDbApiKey.Length > 0 || IsThereAnyDealApiKey.Length > 0;

    // Toutes les valeurs remplies : le rapport de bug les masque si elles apparaissent dans le journal.
    public IEnumerable<string> AllValues()
    {
        string[] values =
        {
            SteamApiKey, SteamId, SteamGridDbApiKey,
            IsThereAnyDealApiKey, IsThereAnyDealClientId, IsThereAnyDealClientSecret
        };

        return values.Where(value => !string.IsNullOrWhiteSpace(value));
    }

    /// <summary>Enregistre les clés (chiffrées) et prévient le reste de l'appli.</summary>
    public void Save()
    {
        // Espaces ou retours à la ligne collés avec la clé : on les retire, sinon la clé est refusée.
        SteamApiKey = SteamApiKey.Trim();
        SteamId = SteamId.Trim();
        SteamGridDbApiKey = SteamGridDbApiKey.Trim();
        IsThereAnyDealApiKey = IsThereAnyDealApiKey.Trim();
        IsThereAnyDealClientId = IsThereAnyDealClientId.Trim();

        SecureStore.Save(StoreName, JsonSerializer.Serialize(this));
        Changed?.Invoke();
    }

    private static Secrets LoadFromDisk()
    {
        string? json = SecureStore.Load(StoreName);

        if (json != null)
        {
            try
            {
                return JsonSerializer.Deserialize<Secrets>(json) ?? new Secrets();
            }
            catch (JsonException ex)
            {
                Logger.Log($"Clés API illisibles : {ex.Message}");
                return new Secrets();
            }
        }

        return ImportLegacyFile();
    }

    /// <summary>
    /// Ancien fonctionnement (avant la version 0.9.0) : les clés étaient en clair dans secrets.json.
    /// On les chiffre, on vérifie qu'elles se relisent bien, puis on supprime le fichier en clair.
    /// </summary>
    private static Secrets ImportLegacyFile()
    {
        if (!File.Exists(LegacyPath))
        {
            return new Secrets();
        }

        try
        {
            Secrets imported = JsonSerializer.Deserialize<Secrets>(File.ReadAllText(LegacyPath)) ?? new Secrets();
            SecureStore.Save(StoreName, JsonSerializer.Serialize(imported));

            // Relecture : si le chiffrement a raté, on garde le fichier en clair (mieux que perdre les clés).
            string? check = SecureStore.Load(StoreName);

            if (check != null && JsonSerializer.Deserialize<Secrets>(check)?.SteamApiKey == imported.SteamApiKey)
            {
                File.Delete(LegacyPath);
                Logger.Log("Clés API importées depuis secrets.json et chiffrées ; secrets.json supprimé.");
            }
            else
            {
                Logger.Log("Clés API importées, mais la relecture a échoué : secrets.json est gardé.");
            }

            return imported;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Logger.Log($"Import de secrets.json impossible : {ex.Message}");
            return new Secrets();
        }
    }
}

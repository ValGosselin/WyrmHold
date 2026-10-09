using System.Text.RegularExpressions;

namespace Wyrmhold.Core;

/// <summary>
/// Retire d'un texte (journal, rapport de plantage) ce qui permettrait de reconnaître l'utilisateur
/// ou d'accéder à ses comptes, AVANT qu'il ne soit montré ou envoyé.
/// Deux méthodes complémentaires :
/// 1. les valeurs exactes connues (clés de secrets.json, jetons enregistrés, nom du compte Windows) ;
/// 2. des « formes » reconnaissables (SteamID64, key=…, jetons, e-mails, longues clés hexadécimales),
///    pour ce qui n'est pas dans la liste.
/// </summary>
public static class ReportSanitizer
{
    // Chaque règle : un motif (expression régulière) et son remplacement.
    // « $1 » remet la partie entre parenthèses (ex. « key= ») et masque seulement la valeur.
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    {
        // Chemins du profil Windows : C:\Users\valen\… → C:\Users\<utilisateur>\…
        (new Regex(@"([A-Za-z]:[\\/]+Users[\\/]+)[^\\/\s""':]+", RegexOptions.IgnoreCase), "$1<utilisateur>"),

        // Paramètres d'adresse sensibles : ?key=…, &steamid=…, &access_token=…
        (new Regex(@"([?&](?:key|steamid|access_token|refresh_token|webapi_token|token|code|code_verifier|client_secret)=)[^&\s""']+",
            RegexOptions.IgnoreCase), "$1***"),

        // En-tête d'authentification : « Bearer eyJ… »
        (new Regex(@"(bearer\s+)[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase), "$1***"),

        // Jetons au format JWT (trois morceaux qui commencent par « eyJ »).
        (new Regex(@"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*"), "[jeton]"),

        // SteamID64 : 17 chiffres qui commencent toujours par 7656119.
        (new Regex(@"\b7656119\d{10}\b"), "[SteamID]"),

        // Adresses e-mail.
        (new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}"), "[e-mail]"),

        // Longues suites hexadécimales (ex. clé Steam Web API : 32 caractères).
        (new Regex(@"\b[0-9A-Fa-f]{32,}\b"), "[clé]"),
    };

    /// <summary>Nettoie le texte avec les valeurs secrètes connues sur ce PC.</summary>
    public static string Clean(string text)
    {
        return Clean(text, GetKnownSecrets(), Environment.UserName);
    }

    /// <summary>
    /// Version testable : on donne soi-même les secrets et le nom du compte Windows.
    /// </summary>
    public static string Clean(string text, IEnumerable<string> secrets, string userName)
    {
        // 1. Les valeurs exactes, les plus longues d'abord (une valeur peut en contenir une autre).
        //    Seulement celles d'au moins 6 caractères avec un chiffre : les marqueurs comme « connected »
        //    (enregistrés aussi dans « tokens ») ne sont pas secrets, et les masquer abîmerait le journal.
        foreach (string secret in secrets.Where(s => s.Length >= 6 && s.Any(char.IsDigit)).Distinct().OrderByDescending(s => s.Length))
        {
            text = text.Replace(secret, "***", StringComparison.OrdinalIgnoreCase);
        }

        // 2. Les formes reconnaissables.
        foreach ((Regex pattern, string replacement) in Rules)
        {
            text = pattern.Replace(text, replacement);
        }

        // 3. Le nom du compte Windows, seulement en mot entier (« valen » mais pas « valentine »).
        if (userName.Length >= 3)
        {
            text = Regex.Replace(text, $@"\b{Regex.Escape(userName)}\b", "<utilisateur>", RegexOptions.IgnoreCase);
        }

        return text;
    }

    /// <summary>
    /// Vérification (console, mode 7) : combien de secrets connus restent dans un texte déjà nettoyé.
    /// Doit toujours valoir 0.
    /// </summary>
    public static int CountRemainingSecrets(string text)
    {
        return GetKnownSecrets()
            .Where(s => s.Length >= 6 && s.Any(char.IsDigit))
            .Count(s => text.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    // Toutes les valeurs secrètes enregistrées sur ce PC : secrets.json et jetons chiffrés (dossier « tokens »).
    private static List<string> GetKnownSecrets()
    {
        var values = new List<string>();

        try
        {
            values.AddRange(Secrets.Load().AllValues());

            foreach (string stored in SecureStore.LoadAll())
            {
                values.Add(stored);

                // Certains jetons sont enregistrés en JSON (ex. IsThereAnyDeal) : on prend aussi chaque valeur.
                foreach (Match match in Regex.Matches(stored, @"""([^""]{16,})"""))
                {
                    values.Add(match.Groups[1].Value);
                }
            }
        }
        catch (Exception ex)
        {
            // Un fichier illisible ne doit pas empêcher le rapport : les règles générales s'appliquent quand même.
            Logger.Log($"Secrets illisibles pour le nettoyage du rapport : {ex.Message}");
        }

        return values;
    }
}

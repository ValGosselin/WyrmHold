using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Wyrmhold.Core;

/// <summary>
/// Convertit le BBCode des annonces Steam ([h2], [list], [*], [b]…) en lignes simples à afficher.
/// </summary>
public static class BbCode
{
    // Une balise : « [ » + « / » éventuel + nom + paramètres éventuels (« =… » ou « attribut=… ») + « ] ».
    private static readonly Regex TagRegex = new Regex(@"\[(/?)([a-zA-Z0-9*]+)(?:[= ][^\]]*)?\]");

    // Les images Steam s'écrivent parfois hors de [img] : « {STEAM_CLAN_IMAGE}/12345/abc.png ».
    private static readonly Regex ClanImageRegex = new Regex(@"\{STEAM_CLAN_IMAGE\}\S*");

    private static readonly Regex SpacesRegex = new Regex(@"\s+");

    // Seules ces balises sont traitées. Le reste entre crochets reste du texte :
    // les notes de patch écrivent souvent « [Fixed] » ou « [PC] » en toutes lettres.
    private static readonly HashSet<string> KnownTags = new HashSet<string>
    {
        "h1", "h2", "h3", "h4", "h5", "h6", "p", "br", "hr",
        "b", "i", "u", "s", "strike", "spoiler", "noparse", "url",
        "list", "olist", "ul", "ol", "*", "li",
        "table", "tr", "td", "th", "quote", "code",
        "img", "previewyoutube", "video", "expand"
    };

    public static List<PatchNoteLine> ToLines(string bbCode)
    {
        List<PatchNoteLine> lines = new List<PatchNoteLine>();
        StringBuilder currentText = new StringBuilder();
        PatchNoteLineKind currentKind = PatchNoteLineKind.Paragraph;
        int listDepth = 0;
        string? hiddenUntil = null;   // la balise dont on attend la fermeture pour réafficher le texte
        int position = 0;             // où s'arrête ce qu'on a déjà lu

        // Termine la ligne en cours (si elle contient du texte) et en commence une nouvelle.
        void EndLine()
        {
            string text = SpacesRegex.Replace(currentText.ToString(), " ").Trim();

            if (text.Length > 0)
            {
                int depth = currentKind == PatchNoteLineKind.Bullet ? Math.Max(0, listDepth - 1) : 0;
                lines.Add(new PatchNoteLine(currentKind, text, depth));
            }

            currentText.Clear();
            currentKind = PatchNoteLineKind.Paragraph;
        }

        // Ajoute du texte brut. Dans l'ancien BBCode, un retour à la ligne compte comme un saut de ligne.
        void AddText(string text)
        {
            text = ClanImageRegex.Replace(WebUtility.HtmlDecode(text), "");
            string[] parts = text.Split('\n');

            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    EndLine();
                }

                currentText.Append(parts[i]);
            }
        }

        foreach (Match match in TagRegex.Matches(bbCode))
        {
            bool isClosing = match.Groups[1].Value == "/";
            string tag = match.Groups[2].Value.ToLowerInvariant();

            if (!KnownTags.Contains(tag))
            {
                continue;   // « [Fixed] » : on le laisse dans le texte
            }

            // Le texte situé entre la balise précédente et celle-ci.
            if (hiddenUntil is null)
            {
                AddText(bbCode.Substring(position, match.Index - position));
            }

            position = match.Index + match.Length;

            // Dans une image ou une vidéo : on ignore tout jusqu'à sa balise fermante.
            if (hiddenUntil is not null)
            {
                if (isClosing && tag == hiddenUntil)
                {
                    hiddenUntil = null;
                }

                continue;
            }

            switch (tag)
            {
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    EndLine();
                    currentKind = isClosing ? PatchNoteLineKind.Paragraph : PatchNoteLineKind.Heading;
                    break;

                case "list" or "olist" or "ul" or "ol":
                    EndLine();
                    listDepth = isClosing ? Math.Max(0, listDepth - 1) : listDepth + 1;
                    break;

                case "*" or "li":
                    EndLine();
                    currentKind = isClosing ? PatchNoteLineKind.Paragraph : PatchNoteLineKind.Bullet;
                    break;

                case "td" or "th":
                    if (isClosing)
                    {
                        currentText.Append("   ");
                    }
                    break;

                case "p" or "br" or "hr" or "tr" or "quote" or "code" or "table":
                    EndLine();
                    break;

                case "img" or "previewyoutube" or "video":
                    // « [img]lien[/img] » : le lien est entre les balises, on le saute.
                    // « [img src="lien"][/img] » : le lien est dans la balise, il n'y a rien à sauter.
                    bool hasParameter = match.Value.Contains('=');

                    if (!isClosing && !hasParameter)
                    {
                        hiddenUntil = tag;
                    }
                    break;

                // [b], [i], [url]… : on garde seulement leur texte.
            }
        }

        if (hiddenUntil is null)
        {
            AddText(bbCode.Substring(position));
        }

        EndLine();
        return lines;
    }
}

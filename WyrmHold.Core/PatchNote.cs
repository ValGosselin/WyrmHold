namespace Wyrmhold.Core;

public enum PatchNoteLineKind
{
    Paragraph,
    Heading,
    Bullet
}

/// <summary>
/// Une ligne de patch note prête à afficher : un titre, un paragraphe ou un point de liste.
/// </summary>
public record PatchNoteLine(PatchNoteLineKind Kind, string Text, int Depth)
{
    // Décalage vers la droite des listes imbriquées, en pixels.
    public double Indent => Depth * 14;
}

/// <summary>
/// Une annonce Steam convertie pour l'affichage.
/// </summary>
public class PatchNote
{
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public long DateUnix { get; init; }
    public List<PatchNoteLine> Lines { get; init; } = new List<PatchNoteLine>();

    // Seule la plus récente est dépliée à l'ouverture de la fiche.
    public bool IsExpanded { get; init; }

    public string DateText => DateTimeOffset.FromUnixTimeSeconds(DateUnix).LocalDateTime.ToString("dd/MM/yyyy");

    public static PatchNote FromSteam(SteamNewsItem item, bool isExpanded)
    {
        return new PatchNote
        {
            Title = item.Title,
            Url = item.Url,
            DateUnix = item.DateUnix,
            Lines = BbCode.ToLines(item.Contents),
            IsExpanded = isExpanded
        };
    }
}

/// <summary>
/// Ce que la fiche affiche dans sa section « Patch notes ».
/// </summary>
public class PatchNotesResult
{
    public List<PatchNote> Notes { get; init; } = new List<PatchNote>();

    // Une phrase à afficher au-dessus des notes (vide si tout va bien).
    public string Message { get; init; } = "";

    // Le lien du bouton sous les notes, et son texte.
    public string LinkUrl { get; init; } = "";
    public string LinkText { get; init; } = "";
}

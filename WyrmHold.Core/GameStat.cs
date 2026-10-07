namespace Wyrmhold.Core;

/// <summary>
/// Une statistique brute qu'un jeu envoie à Steam (ex. « Étoiles : 87 »).
/// Elle n'est reliée à aucun succès : c'est à toi de faire le rapprochement.
/// </summary>
public record GameStat(string Name, double Value)
{
    // 87 → « 87 » ; 12,5 → « 12,5 » (les nombres entiers n'affichent pas de virgule).
    public string ValueText => Value % 1 == 0 ? Value.ToString("N0") : Value.ToString("N2");
}

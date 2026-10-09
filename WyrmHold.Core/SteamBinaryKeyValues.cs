using System.Text;

namespace Wyrmhold.Core;

/// <summary>
/// Un nœud du format « KeyValues binaire » de Steam : un nom, et soit une valeur, soit des enfants.
/// C'est le format des fichiers de Steam\appcache\stats (succès et statistiques des jeux).
/// </summary>
public class KeyValueNode
{
    public string Name { get; init; } = "";
    public object? Value { get; init; }   // string, int, float, long ou ulong ; null pour un nœud qui a des enfants
    public List<KeyValueNode> Children { get; } = new List<KeyValueNode>();

    /// <summary>L'enfant qui porte ce nom (sans tenir compte des majuscules), ou null.</summary>
    public KeyValueNode? this[string name] =>
        Children.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase));

    public string? AsString => Value as string;

    public int? AsInt => Value is int number ? number : null;
}

/// <summary>
/// Lit un fichier KeyValues binaire. Chaque entrée commence par un octet qui dit son type,
/// puis son nom (texte terminé par un octet 0), puis sa valeur. Le type 8 ferme un groupe.
/// Format non documenté par Valve : vérifié sur les fichiers d'un vrai PC le 9 octobre 2026.
/// </summary>
public static class SteamBinaryKeyValues
{
    private const byte Group = 0, Text = 1, Int32 = 2, Float32 = 3, UInt64 = 7, End = 8, Int64 = 10, AlternateEnd = 11;

    public static KeyValueNode Read(byte[] bytes)
    {
        int position = 0;
        var root = new KeyValueNode { Name = "" };
        ReadChildren(bytes, ref position, root);
        return root;
    }

    private static void ReadChildren(byte[] bytes, ref int position, KeyValueNode parent)
    {
        while (position < bytes.Length)
        {
            byte type = bytes[position++];

            if (type == End || type == AlternateEnd)
            {
                return;
            }

            string name = ReadText(bytes, ref position);

            switch (type)
            {
                case Group:
                    var group = new KeyValueNode { Name = name };
                    ReadChildren(bytes, ref position, group);
                    parent.Children.Add(group);
                    break;
                case Text:
                    parent.Children.Add(new KeyValueNode { Name = name, Value = ReadText(bytes, ref position) });
                    break;
                case Int32:
                    parent.Children.Add(new KeyValueNode { Name = name, Value = BitConverter.ToInt32(bytes, position) });
                    position += 4;
                    break;
                case Float32:
                    parent.Children.Add(new KeyValueNode { Name = name, Value = BitConverter.ToSingle(bytes, position) });
                    position += 4;
                    break;
                case UInt64:
                    parent.Children.Add(new KeyValueNode { Name = name, Value = BitConverter.ToUInt64(bytes, position) });
                    position += 8;
                    break;
                case Int64:
                    parent.Children.Add(new KeyValueNode { Name = name, Value = BitConverter.ToInt64(bytes, position) });
                    position += 8;
                    break;
                default:
                    // Type inconnu : impossible de savoir combien d'octets sauter, on arrête proprement.
                    throw new InvalidDataException($"Type KeyValues inconnu ({type}) à l'octet {position}.");
            }
        }
    }

    private static string ReadText(byte[] bytes, ref int position)
    {
        int start = position;

        while (position < bytes.Length && bytes[position] != 0)
        {
            position++;
        }

        string text = Encoding.UTF8.GetString(bytes, start, position - start);
        position++;   // l'octet 0 de fin
        return text;
    }
}

namespace Wyrmhold.Core;

/// <summary>
/// La save d'Inazuma Eleven GO Light ou Shadow, version européenne : game.ie4 (la partie) et head.ie4 (le résumé montré
/// à l'écran de chargement), tous deux chiffrés. Emplacements repris de InazumaElevenSaveEditor (licence MIT) et vérifiés
/// le 3 novembre 2026 sur une vraie save. Cet éditeur ne connaît ni le chapitre ni les matchs : les nombres de head.ie4
/// sont donc gardés sous le nom head.0xNN, pour les repérer avec l'outil de surveillance.
/// </summary>
public class InazumaGoSaveReader : ICustomSaveReader
{
    private const string HeadFileName = "head.ie4";

    private const int GameIdOffset = 4;
    private const ushort InternationalGameId = 0x2CF1;

    private const int PrestigeOffset = 0x8DDC;
    private const int FriendshipOffset = 0x8DE0;

    // Un bloc de 204 octets par joueur recruté : son numéro à +4, son niveau à +0x12, son Keshin à +0x30,
    // puis 6 techniques de 12 octets à +0x4C (numéro sur 4 octets, puis niveau sur 1 octet).
    private const int PlayerBlocksOffset = 0x3488;
    private const int PlayerBlockSize = 0xCC;
    private const int PlayerIdOffset = 4;
    private const int PlayerLevelOffset = 0x12;
    private const int PlayerKeshinOffset = 0x30;
    private const int PlayerMovesOffset = 0x4C;
    private const int PlayerMoveCount = 6;
    private const int PlayerMoveSize = 12;
    private const int MoveLevelOffset = 4;
    private const int MaximumPlayers = 112;
    private const int TeamSize = 11;

    // Les 3 équipes personnalisées : 16 numéros de 4 octets chacune.
    private const int CustomTeamsOffset = 0x9088;
    private const int CustomTeamCount = 3;
    private const int CustomTeamSlots = 16;

    public Dictionary<string, long> Read(string savePath)
    {
        byte[] game = Level5SaveCipher.Decrypt(RomFile.ReadAll(savePath));

        if (BitConverter.ToUInt16(game, GameIdOffset) != InternationalGameId)
        {
            throw new InvalidDataException("Ce fichier n'est pas une save européenne d'Inazuma Eleven GO.");
        }

        Dictionary<string, long> values = new Dictionary<string, long>
        {
            ["prestige"] = BitConverter.ToUInt32(game, PrestigeOffset),
            ["friendship"] = BitConverter.ToUInt32(game, FriendshipOffset)
        };

        ReadPlayers(game, values);
        ReadCustomTeams(game, values);
        ReadHead(Path.Combine(Path.GetDirectoryName(savePath) ?? "", HeadFileName), values);

        return values;
    }

    private static void ReadPlayers(byte[] game, Dictionary<string, long> values)
    {
        List<int> levels = new List<int>();
        HashSet<uint> keshins = new HashSet<uint>();
        int bestMoveLevel = 0;

        for (int index = 0; index < MaximumPlayers; index++)
        {
            int block = PlayerBlocksOffset + index * PlayerBlockSize;
            uint playerId = BitConverter.ToUInt32(game, block + PlayerIdOffset);

            if (playerId == 0)
            {
                continue;
            }

            levels.Add(game[block + PlayerLevelOffset]);
            values[$"player.{playerId:X8}"] = 1;

            uint keshin = BitConverter.ToUInt32(game, block + PlayerKeshinOffset);

            if (keshin != 0)
            {
                keshins.Add(keshin);
            }

            for (int move = 0; move < PlayerMoveCount; move++)
            {
                bestMoveLevel = Math.Max(bestMoveLevel, game[block + PlayerMovesOffset + move * PlayerMoveSize + MoveLevelOffset]);
            }
        }

        levels.Sort((first, second) => second.CompareTo(first));

        values["players.count"] = levels.Count;
        values["players.eleventhLevel"] = levels.Count >= TeamSize ? levels[TeamSize - 1] : 0;
        values["keshin.count"] = keshins.Count;
        values["moves.bestLevel"] = bestMoveLevel;
    }

    // Le remplissage de l'équipe personnalisée la plus complète.
    private static void ReadCustomTeams(byte[] game, Dictionary<string, long> values)
    {
        int best = 0;

        for (int team = 0; team < CustomTeamCount; team++)
        {
            int start = CustomTeamsOffset + team * CustomTeamSlots * sizeof(uint);
            int filled = Enumerable.Range(0, CustomTeamSlots).Count(slot => BitConverter.ToUInt32(game, start + slot * sizeof(uint)) != 0);

            best = Math.Max(best, filled);
        }

        values["customteam.players"] = best;
    }

    private static void ReadHead(string headPath, Dictionary<string, long> values)
    {
        if (!File.Exists(headPath))
        {
            return;
        }

        byte[] head = Level5SaveCipher.Decrypt(RomFile.ReadAll(headPath));

        for (int offset = 0; offset + sizeof(uint) <= head.Length; offset += sizeof(uint))
        {
            uint value = BitConverter.ToUInt32(head, offset);

            if (value != 0)
            {
                values[$"head.0x{offset:X2}"] = value;
            }
        }
    }
}

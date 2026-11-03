using System.Buffers.Binary;

namespace Wyrmhold.Core;

/// <summary>
/// Transforme une save en valeurs nommées, que les règles des succès maison peuvent lire.
/// </summary>
public interface ICustomSaveReader
{
    // savePath = le fichier principal ; un lecteur peut lire d'autres fichiers rangés à côté.
    Dictionary<string, long> Read(string savePath);
}

/// <summary>
/// La save de Pokémon Let's Go (savedata.bin, 1 Mo, non chiffrée). Emplacements repris de PKHeX (GPL-3.0) et vérifiés
/// le 3 novembre 2026 sur une vraie save, y compris avec le mod Exquisite Eevee PLUS : mêmes blocs, mêmes tailles.
/// Une valeur à zéro n'est pas gardée : les règles lisent 0 pour tout ce qui manque.
/// </summary>
public class LetsGoSaveReader : ICustomSaveReader
{
    private const int SaveSize = 0x100000;

    // Le marqueur juste avant la table des blocs, et celui du début du Pokédex.
    private const int BlockTableMarkerOffset = 0xB8610;
    private const uint BlockTableMarker = 0x42454546;
    private const uint PokedexMarker = 0x2F120F17;

    private const int ItemsOffset = 0x0000;
    private const int ItemsSize = 0xD90;
    private const int EventWorkOffset = 0x1200;
    private const int EventWorkCount = 1000;
    private const int EventFlagCount = 4096;
    private const int PokedexOffset = 0x2A00;
    private const int PokedexCaughtOffset = PokedexOffset + 0x8C;
    private const int PokedexCaughtBytes = 0x68;
    private const int CaptureRecordOffset = 0x46600;
    private const int CapturedTotalOffset = CaptureRecordOffset + 0x794;
    private const int TransferredTotalOffset = CaptureRecordOffset + 0x7A8;

    private const int LastKantoSpecies = 151;

    // Les combats contre les Maîtres dresseurs (TRID_MASTER_xxx dans la liste de FlagsEditorEX).
    private const int FirstMasterTrainerFlag = 0xC44;
    private const int LastMasterTrainerFlag = 0xCD5;

    public Dictionary<string, long> Read(string savePath) => Read(RomFile.ReadAll(savePath));

    public Dictionary<string, long> Read(byte[] save)
    {
        if (save.Length != SaveSize
            || ReadUInt32(save, BlockTableMarkerOffset) != BlockTableMarker
            || ReadUInt32(save, PokedexOffset) != PokedexMarker)
        {
            throw new InvalidDataException("Ce fichier n'est pas une save de Pokémon Let's Go.");
        }

        Dictionary<string, long> values = new Dictionary<string, long>();

        void Keep(string key, long value)
        {
            if (value != 0)
            {
                values[key] = value;
            }
        }

        ReadPokedex(save, Keep);
        ReadEvents(save, Keep);
        ReadItems(save, Keep);

        Keep("captured.total", ReadUInt32(save, CapturedTotalOffset));
        Keep("transferred.total", ReadUInt32(save, TransferredTotalOffset));

        return values;
    }

    // Un bit par espèce : le bit 0 est Bulbizarre (n° 1). Meltan et Melmetal sont à leur numéro, 808 et 809.
    private static void ReadPokedex(byte[] save, Action<string, long> keep)
    {
        int caughtInKanto = 0;

        for (int index = 0; index < PokedexCaughtBytes * 8; index++)
        {
            if (IsBitSet(save, PokedexCaughtOffset, index))
            {
                int species = index + 1;

                keep($"dex.{species}", 1);
                caughtInKanto += species <= LastKantoSpecies ? 1 : 0;
            }
        }

        keep("dex.caught", caughtInKanto);
    }

    // D'abord 1000 valeurs de 4 octets (works), puis les flags, un bit chacun. Les numéros sont ceux de FlagsEditorEX.
    private static void ReadEvents(byte[] save, Action<string, long> keep)
    {
        int flagsOffset = EventWorkOffset + EventWorkCount * sizeof(uint);
        int masterTrainers = 0;

        for (int work = 0; work < EventWorkCount; work++)
        {
            keep($"work.{work}", ReadUInt32(save, EventWorkOffset + work * sizeof(uint)));
        }

        for (int flag = 0; flag < EventFlagCount; flag++)
        {
            if (IsBitSet(save, flagsOffset, flag))
            {
                keep($"flag.0x{flag:X3}", 1);
                masterTrainers += flag is >= FirstMasterTrainerFlag and <= LastMasterTrainerFlag ? 1 : 0;
            }
        }

        keep("masters.defeated", masterTrainers);
    }

    // Une entrée du sac sur 4 octets : le numéro de l'objet sur 11 bits, la quantité sur 10 bits à partir du bit 15.
    private static void ReadItems(byte[] save, Action<string, long> keep)
    {
        for (int offset = ItemsOffset; offset < ItemsOffset + ItemsSize; offset += sizeof(uint))
        {
            uint entry = ReadUInt32(save, offset);

            if (entry != 0)
            {
                keep($"item.{entry & 0x7FF}", (entry >> 15) & 0x3FF);
            }
        }
    }

    private static bool IsBitSet(byte[] save, int offset, int bit)
    {
        return (save[offset + (bit >> 3)] & (1 << (bit & 7))) != 0;
    }

    private static uint ReadUInt32(byte[] save, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(save.AsSpan(offset));
    }
}

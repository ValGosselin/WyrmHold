namespace Wyrmhold.Core;

/// <summary>
/// Déchiffre les saves Level-5 (.ie4 d'Inazuma Eleven GO). Algorithme lu dans InazumaElevenSaveEditor de Tiniifan
/// (licence MIT) et réécrit ici : voir THIRD-PARTY-NOTICES.md. Les 8 derniers octets du fichier ne sont pas chiffrés :
/// une somme de contrôle, jamais vérifiée ici (l'identifiant du jeu suffit), puis la graine dont dépend tout le reste.
/// </summary>
public static class Level5SaveCipher
{
    private const int TrailerSize = 8;
    private const int TableSize = 256;
    private const int TableShuffles = 4096;

    private const uint StateMultiplier = 0x6C078965;
    private const uint LastState = 0x03DF95B3;

    private static readonly int[] OddPrimes = FindOddPrimes(TableSize);

    /// <summary>Le fichier en clair, de la même taille : les 8 derniers octets restent identiques.</summary>
    public static byte[] Decrypt(byte[] input)
    {
        if (input.Length <= TrailerSize)
        {
            throw new InvalidDataException("Fichier de save trop court.");
        }

        int length = input.Length - TrailerSize;
        byte[] output = (byte[])input.Clone();
        byte[] table = BuildTable(BitConverter.ToUInt32(input, input.Length - sizeof(uint)));
        int key = 0;

        for (int index = 0; index < length; index++)
        {
            // La clé change tous les 256 octets.
            if (index % TableSize == 0)
            {
                key = OddPrimes[table[(index >> 8) & 0xFF]];
            }

            output[index] = (byte)(input[index] ^ table[(key * (index + 1)) & 0xFF]);
        }

        return output;
    }

    // Une table de 256 valeurs, mélangée 4 096 fois par un petit générateur (xorshift) qui part de la graine.
    private static byte[] BuildTable(uint seed)
    {
        uint[] states = new uint[4];
        uint state = seed;

        for (uint index = 0; index < 3; index++)
        {
            state ^= state >> 30;
            state = unchecked(index + 1 + state * StateMultiplier);
            states[index] = state;
        }

        states[3] = LastState;

        byte[] table = new byte[TableSize];

        for (int index = 0; index < TableSize; index++)
        {
            table[index] = (byte)index;
        }

        for (int round = 0; round < TableShuffles; round++)
        {
            uint random = NextRandom(states);
            byte first = (byte)random;
            byte second = (byte)(random >> 8);

            if (first != second)
            {
                (table[table[first]], table[table[second]]) = (table[table[second]], table[table[first]]);
            }
        }

        return table;
    }

    private static uint NextRandom(uint[] states)
    {
        uint x = states[0];
        uint y = states[3];

        states[0] = states[1];
        states[1] = states[2];
        states[2] = states[3];

        x ^= x << 11;
        x ^= x >> 8;
        y ^= y >> 19;
        states[3] = x ^ y;

        return states[3] % 0x10000;
    }

    private static int[] FindOddPrimes(int count)
    {
        List<int> primes = new List<int>();

        for (int candidate = 3; primes.Count < count; candidate += 2)
        {
            if (primes.TakeWhile(prime => prime * prime <= candidate).All(prime => candidate % prime != 0))
            {
                primes.Add(candidate);
            }
        }

        return primes.ToArray();
    }
}

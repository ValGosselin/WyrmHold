using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Wyrmhold.Core;

/// <summary>
/// Calcule l'empreinte d'un jeu émulé, avec l'algorithme de RetroAchievements (rcheevos, lu le 2 novembre 2026).
/// Une empreinte est un résumé de 32 caractères : le même jeu donne toujours la même, et RetroAchievements
/// reconnaît ainsi la version exacte de la ROM. Les fichiers sont seulement lus, jamais modifiés.
/// - Game Boy, Game Boy Color, Game Boy Advance : MD5 du fichier entier ;
/// - DS : MD5 d'une partie seulement de la ROM (voir HashNintendoDs) ;
/// - GameCube et Wii : calculée par DolphinTool, livré avec Dolphin, qui sait lire les disques compressés (RVZ…).
/// </summary>
public static class RetroAchievementsHasher
{
    // Une ROM DS : l'en-tête fait 512 octets, seuls les premiers comptent ; puis le bloc d'icône et de titres.
    private const int DsHeaderBytes = 512;
    private const int DsHeaderHashedBytes = 0x160;
    private const int DsIconBytes = 0xA00;

    // Au-delà, rcheevos considère que le fichier n'est pas une ROM DS.
    private const long DsMaxCodeBytes = 16 * 1024 * 1024;

    // Une archive est ouverte en mémoire : le plus gros jeu DS fait 512 Mo.
    private const long MaxArchivedRomBytes = 512L * 1024 * 1024;

    private static readonly TimeSpan DolphinToolTimeout = TimeSpan.FromMinutes(2);

    // DolphinTool garde ses fichiers temporaires ici, pas dans le dossier de Dolphin de l'utilisateur.
    private static readonly string DolphinToolFolder = Path.Combine(AppPaths.DataFolder, "DolphinTool");

    private static readonly Regex HashLine = new Regex("^[0-9a-f]{32}$", RegexOptions.Multiline);

    public static bool IsSupported(GameConsole console) => console
        is GameConsole.GameBoy or GameConsole.GameBoyColor or GameConsole.GameBoyAdvance
        or GameConsole.NintendoDs or GameConsole.GameCube or GameConsole.Wii;

    public static bool NeedsDolphinTool(GameConsole console) => console is GameConsole.GameCube or GameConsole.Wii;

    /// <summary>
    /// Change si le fichier du jeu change : l'empreinte enregistrée reste valable tant que ce texte est le même.
    /// Null si le fichier est absent (disque débranché…).
    /// </summary>
    public static string? GetFileStamp(Game game)
    {
        FileInfo file = new FileInfo(game.RomFilePath ?? "");
        return file.Exists ? $"{file.Length}-{file.LastWriteTimeUtc.Ticks}" : null;
    }

    /// <summary>
    /// L'empreinte d'un jeu, ou null si elle est impossible à calculer (archive illisible, ROM abîmée…).
    /// dolphinToolPath sert seulement à la GameCube et à la Wii.
    /// </summary>
    public static async Task<string?> ComputeAsync(Game game, string? dolphinToolPath, CancellationToken cancellationToken = default)
    {
        if (game.RomFilePath is not string path)
        {
            return null;
        }

        if (NeedsDolphinTool(game.Console))
        {
            return dolphinToolPath is null ? null : await HashDiscAsync(dolphinToolPath, path, cancellationToken);
        }

        if (!IsSupported(game.Console))
        {
            return null;
        }

        // Un calcul de plusieurs Mo : hors du fil de l'appelant, pour ne pas bloquer l'interface.
        return await Task.Run(() =>
        {
            using Stream? rom = OpenRom(game);

            if (rom is null)
            {
                return null;
            }

            return game.Console == GameConsole.NintendoDs ? HashNintendoDs(rom) : HashWholeFile(rom);
        }, cancellationToken);
    }

    public static string HashWholeFile(Stream rom)
    {
        return Convert.ToHexStringLower(MD5.HashData(rom));
    }

    /// <summary>
    /// Une ROM DS : le début de l'en-tête, le code des deux processeurs (ARM9 et ARM7), puis l'icône et les titres.
    /// Le reste (musiques, images…) n'est pas lu, ce qui reste rapide même pour un gros jeu.
    /// </summary>
    public static string? HashNintendoDs(Stream rom)
    {
        byte[] header = new byte[DsHeaderBytes];
        rom.Position = 0;

        if (rom.ReadAtLeast(header, DsHeaderBytes, throwOnEndOfStream: false) < DsHeaderBytes)
        {
            return null;
        }

        // Une ROM copiée pour une cartouche SuperCard a 512 octets de plus au début : on les saute.
        long offset = 0;

        if (HasSuperCardHeader(header))
        {
            offset = DsHeaderBytes;
            rom.ReadAtLeast(header, DsHeaderBytes, throwOnEndOfStream: false);
        }

        // Les emplacements et les tailles sont écrits dans l'en-tête, sur 4 octets, poids faible d'abord.
        long arm9Address = ReadUInt32(header, 0x20);
        long arm9Size = ReadUInt32(header, 0x2C);
        long arm7Address = ReadUInt32(header, 0x30);
        long arm7Size = ReadUInt32(header, 0x3C);
        long iconAddress = ReadUInt32(header, 0x68);

        if (arm9Size + arm7Size > DsMaxCodeBytes)
        {
            return null;
        }

        // IncrementalHash : on donne les morceaux un par un, le résultat est celui de leur suite.
        using IncrementalHash md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        md5.AppendData(header, 0, DsHeaderHashedBytes);
        AppendBlock(md5, rom, offset + arm9Address, (int)arm9Size);
        AppendBlock(md5, rom, offset + arm7Address, (int)arm7Size);
        AppendBlock(md5, rom, offset + iconAddress, DsIconBytes);

        return Convert.ToHexStringLower(md5.GetHashAndReset());
    }

    private static bool HasSuperCardHeader(byte[] header)
    {
        return header[0] == 0x2E && header[1] == 0x00 && header[2] == 0x00 && header[3] == 0xEA
            && header[0xB0] == 0x44 && header[0xB1] == 0x46 && header[0xB2] == 0x96 && header[0xB3] == 0x00;
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    }

    // Si la ROM se termine avant la fin du bloc, le reste vaut zéro (certains jeux amateurs ont une icône incomplète).
    private static void AppendBlock(IncrementalHash md5, Stream rom, long address, int size)
    {
        byte[] block = new byte[size];

        rom.Position = address;
        rom.ReadAtLeast(block, size, throwOnEndOfStream: false);
        md5.AppendData(block);
    }

    // Le fichier du jeu, ou le jeu extrait d'un .zip (InstallPath = « jeux.zip#jeu.gba »).
    private static Stream? OpenRom(Game game)
    {
        string[] parts = (game.InstallPath ?? "").Split('#');

        if (parts.Length == 1)
        {
            return RomFile.OpenRead(parts[0]);
        }

        using ZipArchive archive = ZipFile.OpenRead(parts[0]);
        ZipArchiveEntry? entry = archive.GetEntry(parts[1]);

        if (entry is null || entry.Length > MaxArchivedRomBytes)
        {
            return null;
        }

        MemoryStream rom = new MemoryStream((int)entry.Length);

        using (Stream content = entry.Open())
        {
            content.CopyTo(rom);
        }

        rom.Position = 0;
        return rom;
    }

    private static async Task<string?> HashDiscAsync(string dolphinToolPath, string discPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DolphinToolFolder);

        ProcessStartInfo start = new ProcessStartInfo(dolphinToolPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in new[] { "verify", "-u", DolphinToolFolder, "-i", discPath, "-a", "rchash" })
        {
            start.ArgumentList.Add(argument);
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DolphinToolTimeout);

        using Process process = Process.Start(start)
            ?? throw new Win32Exception("DolphinTool n'a pas démarré.");

        try
        {
            // Les deux sorties sont lues en même temps : si l'une se remplissait, DolphinTool resterait bloqué.
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.WaitForExitAsync(timeout.Token);

            Match hash = HashLine.Match((await output).ReplaceLineEndings("\n"));

            if (process.ExitCode == 0 && hash.Success)
            {
                return hash.Value;
            }

            Logger.Log($"DolphinTool n'a pas donné d'empreinte (code {process.ExitCode}) : {(await errors).Trim()}");
            return null;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill();
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            Logger.Log($"DolphinTool n'a pas répondu en {DolphinToolTimeout.TotalMinutes} minutes.");
            return null;
        }
    }
}

using Microsoft.Data.Sqlite;

namespace Wyrmhold.Core;

public class GameDatabase
{
    private readonly string _connectionString;

    public string DatabasePath { get; }

    public GameDatabase()
    {
        DatabasePath = Path.Combine(AppPaths.DataFolder, "wyrmhold.db");

        _connectionString = $"Data Source={DatabasePath}";
    }

    public void Initialize()
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Game (
                Id             INTEGER PRIMARY KEY,
                Platform       TEXT    NOT NULL,
                PlatformGameId TEXT    NOT NULL,
                Name           TEXT    NOT NULL,
                IsInstalled    INTEGER NOT NULL,
                InstallPath    TEXT,
                UNIQUE (Platform, PlatformGameId)
            );
            """;

        command.ExecuteNonQuery();
    }
    public void SaveGames(Platform platform, List<Game> games)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteTransaction transaction = connection.BeginTransaction();

        using SqliteCommand reset = connection.CreateCommand();
        reset.Transaction = transaction;
        reset.CommandText = """
        UPDATE Game
        SET IsInstalled = 0, InstallPath = NULL
        WHERE Platform = $platform;
        """;
        reset.Parameters.AddWithValue("$platform", platform.ToString());
        reset.ExecuteNonQuery();

        foreach (Game game in games)
        {
            using SqliteCommand upsert = connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = """
            INSERT INTO Game (Platform, PlatformGameId, Name, IsInstalled, InstallPath)
            VALUES ($platform, $gameId, $name, $isInstalled, $installPath)
            ON CONFLICT (Platform, PlatformGameId) DO UPDATE SET
                Name        = excluded.Name,
                IsInstalled = excluded.IsInstalled,
                InstallPath = excluded.InstallPath;
            """;
            upsert.Parameters.AddWithValue("$platform", game.Platform.ToString());
            upsert.Parameters.AddWithValue("$gameId", game.PlatformGameId);
            upsert.Parameters.AddWithValue("$name", game.Name);
            upsert.Parameters.AddWithValue("$isInstalled", game.IsInstalled ? 1 : 0);
            upsert.Parameters.AddWithValue("$installPath", (object?)game.InstallPath ?? DBNull.Value);
            upsert.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}


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
        AddColumnIfMissing(connection, "PlaytimeMinutes", "INTEGER NOT NULL DEFAULT 0");
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
            INSERT INTO Game (Platform, PlatformGameId, Name, IsInstalled, InstallPath, PlaytimeMinutes)
            VALUES ($platform, $gameId, $name, $isInstalled, $installPath, $playtime)
            ON CONFLICT (Platform, PlatformGameId) DO UPDATE SET
                Name            = excluded.Name,
                IsInstalled     = excluded.IsInstalled,
                InstallPath     = excluded.InstallPath,
                PlaytimeMinutes = max(Game.PlaytimeMinutes, excluded.PlaytimeMinutes);
            """;
            upsert.Parameters.AddWithValue("$platform", game.Platform.ToString());
            upsert.Parameters.AddWithValue("$gameId", game.PlatformGameId);
            upsert.Parameters.AddWithValue("$name", game.Name);
            upsert.Parameters.AddWithValue("$isInstalled", game.IsInstalled ? 1 : 0);
            upsert.Parameters.AddWithValue("$installPath", (object?)game.InstallPath ?? DBNull.Value);
            upsert.Parameters.AddWithValue("$playtime", game.PlaytimeMinutes);
            upsert.ExecuteNonQuery();
        }

        transaction.Commit();
    }
    private static void AddColumnIfMissing(SqliteConnection connection, string column, string definition)
    {
        using SqliteCommand check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Game') WHERE name = $column;";
        check.Parameters.AddWithValue("$column", column);

        if (Convert.ToInt64(check.ExecuteScalar()) > 0)
        {
            return;
        }

        using SqliteCommand alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE Game ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }
    public List<Game> LoadGames()
    {
        List<Game> games = new List<Game>();

        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
        SELECT Platform, PlatformGameId, Name, IsInstalled, InstallPath, PlaytimeMinutes
        FROM Game
        WHERE IsInstalled = 1 OR Platform = 'Steam';
        """;

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (!Enum.TryParse(reader.GetString(0), out Platform platform))
            {
                continue;
            }

            games.Add(new Game
            {
                Platform = platform,
                PlatformGameId = reader.GetString(1),
                Name = reader.GetString(2),
                IsInstalled = reader.GetInt64(3) == 1,
                InstallPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                PlaytimeMinutes = reader.GetInt32(5)
            });
        }

        return games;
    }
}


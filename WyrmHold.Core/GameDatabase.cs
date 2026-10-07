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
        AddColumnIfMissing(connection, "IsFamilyShared", "INTEGER");
        AddColumnIfMissing(connection, "OwnerSteamId", "TEXT");
        AddColumnIfMissing(connection, "LastPlayedUnix", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "Description", "TEXT");
        AddColumnIfMissing(connection, "Developers", "TEXT");
        AddColumnIfMissing(connection, "ReleaseDateUnix", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "IsEarlyAccess", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "Tags", "TEXT");
        AddColumnIfMissing(connection, "MetadataUpdatedUnix", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "IsOwned", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "IsFavorite", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "AddedUnix", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "SizeOnDiskBytes", "INTEGER NOT NULL DEFAULT 0");

        // Les jeux déjà présents avant l'ajout de la colonne reçoivent la date d'aujourd'hui.
        using (SqliteCommand fillAddedDates = connection.CreateCommand())
        {
            fillAddedDates.CommandText = "UPDATE Game SET AddedUnix = CAST(strftime('%s', 'now') AS INTEGER) WHERE AddedUnix = 0;";
            fillAddedDates.ExecuteNonQuery();
        }
        using (SqliteCommand createCollections = connection.CreateCommand())
        {
            createCollections.CommandText = """
                CREATE TABLE IF NOT EXISTS Collection (
                    Id   INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE COLLATE NOCASE
                );
 
                CREATE TABLE IF NOT EXISTS CollectionGame (
                    CollectionId   INTEGER NOT NULL,
                    Platform       TEXT NOT NULL,
                    PlatformGameId TEXT NOT NULL,
                    PRIMARY KEY (CollectionId, Platform, PlatformGameId)
                );
                """;
            createCollections.ExecuteNonQuery();
        }

        using (SqliteCommand createViews = connection.CreateCommand())
        {
            createViews.CommandText = """
                CREATE TABLE IF NOT EXISTS SavedView (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name        TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    FiltersJson TEXT NOT NULL
                );
                """;
            createViews.ExecuteNonQuery();
        }

        using SqliteCommand createSessions = connection.CreateCommand();
        createSessions.CommandText = """
        CREATE TABLE IF NOT EXISTS PlaySession (
            Id          INTEGER PRIMARY KEY,
            GameId      INTEGER NOT NULL REFERENCES Game(Id),
            StartedUnix INTEGER NOT NULL,
            EndedUnix   INTEGER NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_PlaySession_GameId ON PlaySession(GameId);
        """;
        createSessions.ExecuteNonQuery();
    }
    // ----- Collections -----

    public List<GameCollection> LoadCollections()
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name FROM Collection ORDER BY Name COLLATE NOCASE;";

        List<GameCollection> collections = new List<GameCollection>();
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            collections.Add(new GameCollection(reader.GetInt64(0), reader.GetString(1)));
        }

        return collections;
    }

    public long CreateCollection(string name)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Collection (Name) VALUES ($name) RETURNING Id;";
        command.Parameters.AddWithValue("$name", name);

        return (long)command.ExecuteScalar()!;
    }

    public void RenameCollection(long collectionId, string newName)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Collection SET Name = $name WHERE Id = $id;";
        command.Parameters.AddWithValue("$name", newName);
        command.Parameters.AddWithValue("$id", collectionId);
        command.ExecuteNonQuery();
    }

    public void DeleteCollection(long collectionId)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteTransaction transaction = connection.BeginTransaction();

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM CollectionGame WHERE CollectionId = $id;
            DELETE FROM Collection WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", collectionId);
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    public List<(long CollectionId, string Platform, string PlatformGameId)> LoadCollectionMemberships()
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT CollectionId, Platform, PlatformGameId FROM CollectionGame;";

        List<(long, string, string)> memberships = new List<(long, string, string)>();
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            memberships.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        }

        return memberships;
    }

    public void SetGameInCollection(long collectionId, Game game, bool isMember)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = isMember
            ? """
              INSERT OR IGNORE INTO CollectionGame (CollectionId, Platform, PlatformGameId)
              VALUES ($collectionId, $platform, $gameId);
              """
            : """
              DELETE FROM CollectionGame
              WHERE CollectionId = $collectionId AND Platform = $platform AND PlatformGameId = $gameId;
              """;
        command.Parameters.AddWithValue("$collectionId", collectionId);
        command.Parameters.AddWithValue("$platform", game.Platform.ToString());
        command.Parameters.AddWithValue("$gameId", game.PlatformGameId);
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
            INSERT INTO Game (Platform, PlatformGameId, Name, IsInstalled, InstallPath,
                              PlaytimeMinutes, LastPlayedUnix, IsFamilyShared, OwnerSteamId,
                              SizeOnDiskBytes, AddedUnix)
            VALUES ($platform, $gameId, $name, $isInstalled, $installPath,
                    $playtime, $lastPlayed, $isFamilyShared, $ownerSteamId,
                    $size, CAST(strftime('%s', 'now') AS INTEGER))
            ON CONFLICT (Platform, PlatformGameId) DO UPDATE SET
                SizeOnDiskBytes = CASE WHEN excluded.SizeOnDiskBytes > 0
                                       THEN excluded.SizeOnDiskBytes
                                       ELSE Game.SizeOnDiskBytes END,
                Name            = excluded.Name,
                IsInstalled     = excluded.IsInstalled,
                InstallPath     = excluded.InstallPath,
                PlaytimeMinutes = max(Game.PlaytimeMinutes, excluded.PlaytimeMinutes),
                LastPlayedUnix  = max(Game.LastPlayedUnix, excluded.LastPlayedUnix),
                IsFamilyShared  = COALESCE(excluded.IsFamilyShared, Game.IsFamilyShared),
                OwnerSteamId    = COALESCE(excluded.OwnerSteamId, Game.OwnerSteamId);
            """;
            upsert.Parameters.AddWithValue("$platform", game.Platform.ToString());
            upsert.Parameters.AddWithValue("$gameId", game.PlatformGameId);
            upsert.Parameters.AddWithValue("$name", game.Name);
            upsert.Parameters.AddWithValue("$isInstalled", game.IsInstalled ? 1 : 0);
            upsert.Parameters.AddWithValue("$installPath", (object?)game.InstallPath ?? DBNull.Value);
            upsert.Parameters.AddWithValue("$playtime", game.PlaytimeMinutes);
            upsert.Parameters.AddWithValue("$isFamilyShared", game.IsFamilyShared switch
            {
                true => 1,
                false => 0,
                null => DBNull.Value
            });
            upsert.Parameters.AddWithValue("$ownerSteamId", (object?)game.OwnerSteamId ?? DBNull.Value);
            upsert.Parameters.AddWithValue("$lastPlayed", game.LastPlayedUnix);
            upsert.Parameters.AddWithValue("$size", game.SizeOnDiskBytes);
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
        SELECT Platform, PlatformGameId, Name, IsInstalled, InstallPath, PlaytimeMinutes, IsFamilyShared, OwnerSteamId, LastPlayedUnix, Description, Developers, ReleaseDateUnix, IsEarlyAccess, Tags, MetadataUpdatedUnix, IsFavorite, AddedUnix, SizeOnDiskBytes
        FROM Game
        WHERE IsInstalled = 1 OR IsOwned = 1 OR Platform = 'Steam';
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
                PlaytimeMinutes = reader.GetInt32(5),
                IsFamilyShared = reader.IsDBNull(6) ? null : reader.GetInt64(6) == 1,
                OwnerSteamId = reader.IsDBNull(7) ? null : reader.GetString(7),
                LastPlayedUnix = reader.GetInt64(8),
                Description = reader.IsDBNull(9) ? null : reader.GetString(9),
                Developers = reader.IsDBNull(10) ? null : reader.GetString(10),
                ReleaseDateUnix = reader.GetInt64(11),
                IsEarlyAccess = reader.GetInt64(12) == 1,
                Tags = reader.IsDBNull(13) ? null : reader.GetString(13),
                MetadataUpdatedUnix = reader.GetInt64(14),
                IsFavorite = reader.GetInt64(reader.GetOrdinal("IsFavorite")) == 1,
                AddedUnix = reader.GetInt64(reader.GetOrdinal("AddedUnix")),
                SizeOnDiskBytes = reader.GetInt64(reader.GetOrdinal("SizeOnDiskBytes"))

            });
        }

        return games;
    }
    public void SetFavorite(Game game, bool isFavorite)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
        UPDATE Game SET IsFavorite = $isFavorite
        WHERE Platform = $platform AND PlatformGameId = $gameId;
        """;
        command.Parameters.AddWithValue("$isFavorite", isFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$platform", game.Platform.ToString());
        command.Parameters.AddWithValue("$gameId", game.PlatformGameId);
        command.ExecuteNonQuery();
    }
    public void SetSizeOnDisk(Game game, long sizeOnDiskBytes)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
        UPDATE Game SET SizeOnDiskBytes = $size
        WHERE Platform = $platform AND PlatformGameId = $gameId;
        """;
        command.Parameters.AddWithValue("$size", sizeOnDiskBytes);
        command.Parameters.AddWithValue("$platform", game.Platform.ToString());
        command.Parameters.AddWithValue("$gameId", game.PlatformGameId);
        command.ExecuteNonQuery();
    }
    public void SaveOwnedGames(Platform platform, List<Game> games)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteTransaction transaction = connection.BeginTransaction();

        using SqliteCommand reset = connection.CreateCommand();
        reset.Transaction = transaction;
        reset.CommandText = "UPDATE Game SET IsOwned = 0 WHERE Platform = $platform;";
        reset.Parameters.AddWithValue("$platform", platform.ToString());
        reset.ExecuteNonQuery();

        foreach (Game game in games)
        {
            using SqliteCommand upsert = connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = """
            INSERT INTO Game (Platform, PlatformGameId, Name, IsInstalled, IsOwned, PlaytimeMinutes, LastPlayedUnix, AddedUnix)
            VALUES ($platform, $gameId, $name, 0, 1, $playtime, $lastPlayed, CAST(strftime('%s', 'now') AS INTEGER))
            ON CONFLICT (Platform, PlatformGameId) DO UPDATE SET
                IsOwned         = 1,
                PlaytimeMinutes = max(Game.PlaytimeMinutes, excluded.PlaytimeMinutes),
                LastPlayedUnix  = max(Game.LastPlayedUnix, excluded.LastPlayedUnix);
            """;
            upsert.Parameters.AddWithValue("$platform", platform.ToString());
            upsert.Parameters.AddWithValue("$gameId", game.PlatformGameId);
            upsert.Parameters.AddWithValue("$name", game.Name);
            upsert.Parameters.AddWithValue("$playtime", game.PlaytimeMinutes);
            upsert.Parameters.AddWithValue("$lastPlayed", game.LastPlayedUnix);
            upsert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void RemoveFamilyGames()
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand delete = connection.CreateCommand();
        delete.CommandText = """
        DELETE FROM Game
        WHERE Platform = 'Steam' AND IsFamilyShared = 1 AND IsInstalled = 0;
        """;
        delete.ExecuteNonQuery();
    }
    public void SaveFamilyGames(List<Game> games)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteTransaction transaction = connection.BeginTransaction();

        foreach (Game game in games)
        {
            using SqliteCommand upsert = connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = """
            INSERT INTO Game (Platform, PlatformGameId, Name, IsInstalled,
                              PlaytimeMinutes, LastPlayedUnix, IsFamilyShared, OwnerSteamId, AddedUnix)
            VALUES ('Steam', $gameId, $name, 0,
                    $playtime, $lastPlayed, 1, $ownerSteamId, CAST(strftime('%s', 'now') AS INTEGER))
            ON CONFLICT (Platform, PlatformGameId) DO UPDATE SET
                PlaytimeMinutes = max(Game.PlaytimeMinutes, excluded.PlaytimeMinutes),
                LastPlayedUnix  = max(Game.LastPlayedUnix, excluded.LastPlayedUnix),
                IsFamilyShared  = 1,
                OwnerSteamId    = excluded.OwnerSteamId;
            """;
            upsert.Parameters.AddWithValue("$gameId", game.PlatformGameId);
            upsert.Parameters.AddWithValue("$name", game.Name);
            upsert.Parameters.AddWithValue("$playtime", game.PlaytimeMinutes);
            upsert.Parameters.AddWithValue("$lastPlayed", game.LastPlayedUnix);
            upsert.Parameters.AddWithValue("$ownerSteamId", (object?)game.OwnerSteamId ?? DBNull.Value);
            upsert.ExecuteNonQuery();
        }

        transaction.Commit();
    }
    public void SaveMetadata(List<Game> games)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteTransaction transaction = connection.BeginTransaction();

        foreach (Game game in games)
        {
            using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
            UPDATE Game SET
                Description         = $description,
                Developers          = $developers,
                ReleaseDateUnix     = $releaseDate,
                IsEarlyAccess       = $isEarlyAccess,
                Tags                = $tags,
                MetadataUpdatedUnix = $updated
            WHERE Platform = $platform AND PlatformGameId = $gameId;
            """;
            update.Parameters.AddWithValue("$description", (object?)game.Description ?? DBNull.Value);
            update.Parameters.AddWithValue("$developers", (object?)game.Developers ?? DBNull.Value);
            update.Parameters.AddWithValue("$releaseDate", game.ReleaseDateUnix);
            update.Parameters.AddWithValue("$isEarlyAccess", game.IsEarlyAccess ? 1 : 0);
            update.Parameters.AddWithValue("$tags", (object?)game.Tags ?? DBNull.Value);
            update.Parameters.AddWithValue("$updated", game.MetadataUpdatedUnix);
            update.Parameters.AddWithValue("$platform", game.Platform.ToString());
            update.Parameters.AddWithValue("$gameId", game.PlatformGameId);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
    }
    public void AddPlaySession(Game game, long startedUnix, long endedUnix, int minutes)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteTransaction transaction = connection.BeginTransaction();

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
        INSERT INTO PlaySession (GameId, StartedUnix, EndedUnix)
        SELECT Id, $started, $ended
        FROM Game
        WHERE Platform = $platform AND PlatformGameId = $gameId;

        UPDATE Game SET
            PlaytimeMinutes = PlaytimeMinutes + $minutes,
            LastPlayedUnix  = max(LastPlayedUnix, $started)
        WHERE Platform = $platform AND PlatformGameId = $gameId;
        """;
        command.Parameters.AddWithValue("$started", startedUnix);
        command.Parameters.AddWithValue("$ended", endedUnix);
        command.Parameters.AddWithValue("$minutes", minutes);
        command.Parameters.AddWithValue("$platform", game.Platform.ToString());
        command.Parameters.AddWithValue("$gameId", game.PlatformGameId);
        command.ExecuteNonQuery();

        transaction.Commit();
    }
    public List<(long StartedUnix, long EndedUnix)> LoadPlaySessions(long sinceUnix)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT StartedUnix, EndedUnix FROM PlaySession
            WHERE StartedUnix >= $since;
            """;
        command.Parameters.AddWithValue("$since", sinceUnix);

        List<(long, long)> sessions = new List<(long, long)>();
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            sessions.Add((reader.GetInt64(0), reader.GetInt64(1)));
        }

        return sessions;
    }

    // ----- Vues enregistrées -----

    public List<(long Id, string Name, string FiltersJson)> LoadSavedViews()
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, FiltersJson FROM SavedView ORDER BY Name COLLATE NOCASE;";

        List<(long, string, string)> views = new List<(long, string, string)>();
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            views.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        }

        return views;
    }

    public void SaveView(string name, string filtersJson)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SavedView (Name, FiltersJson) VALUES ($name, $json)
            ON CONFLICT (Name) DO UPDATE SET FiltersJson = excluded.FiltersJson;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$json", filtersJson);
        command.ExecuteNonQuery();
    }

    public void DeleteSavedView(long viewId)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SavedView WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", viewId);
        command.ExecuteNonQuery();
    }
}

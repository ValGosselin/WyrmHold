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

        // Phase 5 : mises à jour et succès.
        AddColumnIfMissing(connection, "InstalledVersion", "TEXT");
        AddColumnIfMissing(connection, "LastUpdateDetectedUnix", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "SteamAppId", "TEXT");
        AddColumnIfMissing(connection, "AchievementsUnlocked", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "AchievementsTotal", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "HasNewAchievements", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "AchievementsCheckedUnix", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "AchievementsAdded", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "AchievementsAddedUnix", "INTEGER NOT NULL DEFAULT 0");

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

        // Liste de suivi de l'onglet Boutiques : un jeu IsThereAnyDeal par ligne.
        // ItadId est la clé primaire : un même jeu ne peut pas être suivi deux fois.
        using (SqliteCommand createWatchList = connection.CreateCommand())
        {
            createWatchList.CommandText = """
                CREATE TABLE IF NOT EXISTS WatchedGame (
                    ItadId    TEXT    PRIMARY KEY,
                    Title     TEXT    NOT NULL,
                    Type      TEXT    NOT NULL DEFAULT '',
                    AddedUnix INTEGER NOT NULL
                );
                """;
            createWatchList.ExecuteNonQuery();
        }

        // Dernier prix connu de chaque jeu suivi (rempli par la vérification à l'ouverture).
        // Les prix sont gardés en CENTIMES entiers (4899 = 48,99 €) : un nombre à virgule
        // (REAL) peut stocker 48,99 comme 48,98999999…, un entier jamais.
        AddColumnIfMissing(connection, "WatchedGame", "BestPriceCents", "INTEGER");
        AddColumnIfMissing(connection, "WatchedGame", "BestShop", "TEXT");
        AddColumnIfMissing(connection, "WatchedGame", "BestCut", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "WatchedGame", "HistoryLowCents", "INTEGER");
        AddColumnIfMissing(connection, "WatchedGame", "Currency", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "WatchedGame", "PricesCheckedUnix", "INTEGER NOT NULL DEFAULT 0");

        // Synchro avec la Waitlist IsThereAnyDeal (facultative) :
        // SyncedWithItad = 1 si le jeu a déjà été vu sur ta Waitlist lors d'une synchro.
        AddColumnIfMissing(connection, "WatchedGame", "SyncedWithItad", "INTEGER NOT NULL DEFAULT 0");

        // Jeux retirés ici alors que le site n'a pas pu être prévenu (pas de réseau…) :
        // on réessaie à la synchro suivante, sinon ils reviendraient depuis la Waitlist.
        using (SqliteCommand createPendingRemovals = connection.CreateCommand())
        {
            createPendingRemovals.CommandText = """
                CREATE TABLE IF NOT EXISTS WaitlistPendingRemoval (
                    ItadId TEXT PRIMARY KEY
                );
                """;
            createPendingRemovals.ExecuteNonQuery();
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

        // Bloc-notes de l'overlay : un texte libre par jeu (plateforme + identifiant, comme les collections).
        using SqliteCommand createNotes = connection.CreateCommand();
        createNotes.CommandText = """
        CREATE TABLE IF NOT EXISTS GameNote (
            Platform       TEXT    NOT NULL,
            PlatformGameId TEXT    NOT NULL,
            Text           TEXT    NOT NULL,
            UpdatedUnix    INTEGER NOT NULL,
            PRIMARY KEY (Platform, PlatformGameId)
        );
        """;
        createNotes.ExecuteNonQuery();
    }

    // ----- Bloc-notes par jeu -----

    /// <summary>Les notes d'un jeu ("" s'il n'en a pas).</summary>
    public string LoadGameNote(Game game)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Text FROM GameNote WHERE Platform = $platform AND PlatformGameId = $gameId;";
        command.Parameters.AddWithValue("$platform", game.Platform.ToString());
        command.Parameters.AddWithValue("$gameId", game.PlatformGameId);

        return command.ExecuteScalar() as string ?? "";
    }

    /// <summary>Enregistre les notes d'un jeu ; un texte vide efface la ligne.</summary>
    public void SaveGameNote(Game game, string text)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(text)
            ? "DELETE FROM GameNote WHERE Platform = $platform AND PlatformGameId = $gameId;"
            : """
              INSERT INTO GameNote (Platform, PlatformGameId, Text, UpdatedUnix)
              VALUES ($platform, $gameId, $text, $now)
              ON CONFLICT (Platform, PlatformGameId) DO UPDATE SET Text = $text, UpdatedUnix = $now;
              """;
        command.Parameters.AddWithValue("$platform", game.Platform.ToString());
        command.Parameters.AddWithValue("$gameId", game.PlatformGameId);
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.ExecuteNonQuery();
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
                              SizeOnDiskBytes, AddedUnix, InstalledVersion)
            VALUES ($platform, $gameId, $name, $isInstalled, $installPath,
                    $playtime, $lastPlayed, $isFamilyShared, $ownerSteamId,
                    $size, CAST(strftime('%s', 'now') AS INTEGER), $version)
            ON CONFLICT (Platform, PlatformGameId) DO UPDATE SET
                LastUpdateDetectedUnix = CASE
                    WHEN excluded.InstalledVersion IS NOT NULL
                         AND Game.InstalledVersion IS NOT NULL
                         AND excluded.InstalledVersion <> Game.InstalledVersion
                    THEN CAST(strftime('%s', 'now') AS INTEGER)
                    ELSE Game.LastUpdateDetectedUnix END,
                InstalledVersion = COALESCE(excluded.InstalledVersion, Game.InstalledVersion),
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
            upsert.Parameters.AddWithValue("$version", (object?)game.InstalledVersion ?? DBNull.Value);
            upsert.ExecuteNonQuery();
        }

        transaction.Commit();
    }
    private static void AddColumnIfMissing(SqliteConnection connection, string column, string definition)
    {
        AddColumnIfMissing(connection, "Game", column, definition);
    }

    // Même nom, un paramètre de plus (une « surcharge ») : la même méthode sert maintenant
    // à n'importe quelle table. L'ancienne version ci-dessus l'appelle avec "Game".
    private static void AddColumnIfMissing(SqliteConnection connection, string table, string column, string definition)
    {
        using SqliteCommand check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column;";
        check.Parameters.AddWithValue("$column", column);

        if (Convert.ToInt64(check.ExecuteScalar()) > 0)
        {
            return;
        }

        using SqliteCommand alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }
    public List<Game> LoadGames()
    {
        List<Game> games = new List<Game>();

        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
        SELECT Platform, PlatformGameId, Name, IsInstalled, InstallPath, PlaytimeMinutes, IsFamilyShared, OwnerSteamId, LastPlayedUnix, Description, Developers, ReleaseDateUnix, IsEarlyAccess, Tags, MetadataUpdatedUnix, IsFavorite, AddedUnix, SizeOnDiskBytes,
               InstalledVersion, LastUpdateDetectedUnix, SteamAppId,
               AchievementsUnlocked, AchievementsTotal, HasNewAchievements, AchievementsCheckedUnix,
               AchievementsAdded, AchievementsAddedUnix
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
                SizeOnDiskBytes = reader.GetInt64(reader.GetOrdinal("SizeOnDiskBytes")),
                InstalledVersion = ReadNullableString(reader, "InstalledVersion"),
                LastUpdateDetectedUnix = reader.GetInt64(reader.GetOrdinal("LastUpdateDetectedUnix")),
                SteamAppId = ReadNullableString(reader, "SteamAppId"),
                AchievementsUnlocked = reader.GetInt32(reader.GetOrdinal("AchievementsUnlocked")),
                AchievementsTotal = reader.GetInt32(reader.GetOrdinal("AchievementsTotal")),
                HasNewAchievements = reader.GetInt64(reader.GetOrdinal("HasNewAchievements")) == 1,
                AchievementsCheckedUnix = reader.GetInt64(reader.GetOrdinal("AchievementsCheckedUnix")),
                AchievementsAdded = reader.GetInt32(reader.GetOrdinal("AchievementsAdded")),
                AchievementsAddedUnix = reader.GetInt64(reader.GetOrdinal("AchievementsAddedUnix"))
            });
        }

        return games;
    }

    private static string? ReadNullableString(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    // ----- Succès et patch notes -----

    public void SaveAchievements(List<Game> games)
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
                AchievementsUnlocked    = $unlocked,
                AchievementsTotal       = $total,
                HasNewAchievements      = $hasNew,
                AchievementsCheckedUnix = $checked,
                AchievementsAdded       = $added,
                AchievementsAddedUnix   = $addedAt
            WHERE Platform = $platform AND PlatformGameId = $gameId;
            """;
            update.Parameters.AddWithValue("$added", game.AchievementsAdded);
            update.Parameters.AddWithValue("$addedAt", game.AchievementsAddedUnix);
            update.Parameters.AddWithValue("$unlocked", game.AchievementsUnlocked);
            update.Parameters.AddWithValue("$total", game.AchievementsTotal);
            update.Parameters.AddWithValue("$hasNew", game.HasNewAchievements ? 1 : 0);
            update.Parameters.AddWithValue("$checked", game.AchievementsCheckedUnix);
            update.Parameters.AddWithValue("$platform", game.Platform.ToString());
            update.Parameters.AddWithValue("$gameId", game.PlatformGameId);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void SetSteamAppId(Game game, string steamAppId)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
        UPDATE Game SET SteamAppId = $steamAppId
        WHERE Platform = $platform AND PlatformGameId = $gameId;
        """;
        command.Parameters.AddWithValue("$steamAppId", steamAppId);
        command.Parameters.AddWithValue("$platform", game.Platform.ToString());
        command.Parameters.AddWithValue("$gameId", game.PlatformGameId);
        command.ExecuteNonQuery();
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

    // ----- Liste de suivi (onglet Boutiques) -----

    public List<WatchedGame> LoadWatchedGames()
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT ItadId, Title, Type, AddedUnix,
                   BestPriceCents, BestShop, BestCut, HistoryLowCents, Currency, PricesCheckedUnix,
                   SyncedWithItad
            FROM WatchedGame
            ORDER BY Title COLLATE NOCASE;
            """;

        List<WatchedGame> games = new List<WatchedGame>();
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            games.Add(new WatchedGame
            {
                ItadId = reader.GetString(0),
                Title = reader.GetString(1),
                Type = reader.GetString(2),
                AddedUnix = reader.GetInt64(3),
                BestPrice = reader.IsDBNull(4) ? null : reader.GetInt64(4) / 100m,
                BestShop = reader.IsDBNull(5) ? null : reader.GetString(5),
                BestCut = reader.GetInt32(6),
                HistoryLow = reader.IsDBNull(7) ? null : reader.GetInt64(7) / 100m,
                Currency = reader.GetString(8),
                PricesCheckedUnix = reader.GetInt64(9),
                SyncedWithItad = reader.GetInt64(10) == 1
            });
        }

        return games;
    }

    public void AddWatchedGame(WatchedGame game)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        // INSERT OR IGNORE : si le jeu est déjà suivi (même ItadId), SQLite ne fait rien au lieu de planter.
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO WatchedGame (ItadId, Title, Type, AddedUnix, SyncedWithItad)
            VALUES ($id, $title, $type, $added, $synced);
            """;
        command.Parameters.AddWithValue("$id", game.ItadId);
        command.Parameters.AddWithValue("$title", game.Title);
        command.Parameters.AddWithValue("$type", game.Type);
        command.Parameters.AddWithValue("$added", game.AddedUnix);
        command.Parameters.AddWithValue("$synced", game.SyncedWithItad ? 1 : 0);
        command.ExecuteNonQuery();
    }

    /// <summary>Marque des jeux suivis comme présents (ou non) sur la Waitlist IsThereAnyDeal.</summary>
    public void SetWatchedGamesSynced(IEnumerable<string> itadIds, bool isSynced)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        foreach (string itadId in itadIds)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE WatchedGame SET SyncedWithItad = $synced WHERE ItadId = $id;";
            command.Parameters.AddWithValue("$synced", isSynced ? 1 : 0);
            command.Parameters.AddWithValue("$id", itadId);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public List<string> LoadPendingWaitlistRemovals()
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ItadId FROM WaitlistPendingRemoval;";

        List<string> ids = new List<string>();
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    public void AddPendingWaitlistRemoval(string itadId)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO WaitlistPendingRemoval (ItadId) VALUES ($id);";
        command.Parameters.AddWithValue("$id", itadId);
        command.ExecuteNonQuery();
    }

    public void ClearPendingWaitlistRemovals()
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM WaitlistPendingRemoval;";
        command.ExecuteNonQuery();
    }

    /// <summary>Enregistre les derniers prix connus des jeux suivis, en une seule transaction.</summary>
    public void SaveWatchedPrices(IEnumerable<WatchedGame> games)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Une transaction regroupe toutes les mises à jour : c'est beaucoup plus rapide,
        // et en cas d'erreur, rien n'est enregistré à moitié.
        using SqliteTransaction transaction = connection.BeginTransaction();

        foreach (WatchedGame game in games)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE WatchedGame
                SET BestPriceCents = $best, BestShop = $shop, BestCut = $cut,
                    HistoryLowCents = $low, Currency = $currency, PricesCheckedUnix = $checked
                WHERE ItadId = $id;
                """;
            // DBNull.Value = « pas de valeur » en SQL (NULL) : un jeu sans aucune offre en ce moment.
            command.Parameters.AddWithValue("$best", ToCents(game.BestPrice) ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$shop", game.BestShop ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$cut", game.BestCut);
            command.Parameters.AddWithValue("$low", ToCents(game.HistoryLow) ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$currency", game.Currency);
            command.Parameters.AddWithValue("$checked", game.PricesCheckedUnix);
            command.Parameters.AddWithValue("$id", game.ItadId);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static long? ToCents(decimal? amount)
    {
        return amount == null ? null : (long)Math.Round(amount.Value * 100);
    }

    public void RemoveWatchedGame(string itadId)
    {
        using SqliteConnection connection = new SqliteConnection(_connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM WatchedGame WHERE ItadId = $id;";
        command.Parameters.AddWithValue("$id", itadId);
        command.ExecuteNonQuery();
    }
}

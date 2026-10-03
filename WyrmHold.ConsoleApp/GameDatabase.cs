using Microsoft.Data.Sqlite;

namespace Wyrmhold.ConsoleApp;

public class GameDatabase
{
    private readonly string _connectionString;

    public string DatabasePath { get; }

    public GameDatabase()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wyrmhold");

        Directory.CreateDirectory(folder);

        DatabasePath = Path.Combine(folder, "wyrmhold.db");
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
}
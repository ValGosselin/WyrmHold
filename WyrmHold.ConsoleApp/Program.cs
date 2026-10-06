string file = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Ubisoft Game Launcher", "cache", "configuration", "configurations");

string outputPath = Path.Combine(Path.GetTempPath(), "configurations.txt");

try
{
    var configuration = UbiParser.Parsers.ParseConfigurationCacheFile(file);

    Console.WriteLine($"Type du résultat : {configuration?.GetType().FullName}");
    File.WriteAllText(outputPath, configuration?.ToString() ?? "(rien)");
    Console.WriteLine($"Contenu écrit dans : {outputPath}");
}
catch (Exception ex)
{
    Console.WriteLine($"Erreur : {ex.Message}");
}
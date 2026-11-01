namespace Wyrmhold.Core;

/// <summary>
/// La console d'un jeu émulé. None = jeu PC. Enregistré en base sous forme de texte (« GameBoyAdvance »).
/// </summary>
public enum GameConsole
{
    None,
    GameBoy,
    GameBoyColor,
    GameBoyAdvance,
    NintendoDs,
    GameCube,
    Wii,
    Switch,
    Nintendo3ds
}

public static class GameConsoles
{
    // Le nom complet : sous la tuile et dans la fiche.
    public static string Name(GameConsole console) => console switch
    {
        GameConsole.GameBoy => "Game Boy",
        GameConsole.GameBoyColor => "Game Boy Color",
        GameConsole.GameBoyAdvance => "Game Boy Advance",
        GameConsole.NintendoDs => "Nintendo DS",
        GameConsole.GameCube => "GameCube",
        GameConsole.Wii => "Wii",
        GameConsole.Switch => "Switch",
        GameConsole.Nintendo3ds => "Nintendo 3DS",
        _ => ""
    };

    // Le nom court : pastille d'un jeu possédé plusieurs fois, et début de l'identifiant d'un jeu RetroArch.
    public static string ShortName(GameConsole console) => console switch
    {
        GameConsole.GameBoy => "GB",
        GameConsole.GameBoyColor => "GBC",
        GameConsole.GameBoyAdvance => "GBA",
        GameConsole.NintendoDs => "DS",
        GameConsole.Nintendo3ds => "3DS",
        _ => Name(console)
    };
}

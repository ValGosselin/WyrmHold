namespace Wyrmhold.Core;

public enum Platform
{
    Steam,
    Epic,
    Ubisoft,
    Gog,
    Ea,
    BattleNet,

    // Les émulateurs : une valeur par émulateur, comme un lanceur. La console du jeu est à part
    // (Game.Console) : RetroArch fait tourner plusieurs consoles, et une console pourrait changer d'émulateur.
    RetroArch,
    Dolphin,
    Ryujinx,
    Azahar
}

public static class PlatformExtensions
{
    public static bool IsEmulator(this Platform platform)
    {
        return platform is Platform.RetroArch or Platform.Dolphin or Platform.Ryujinx or Platform.Azahar;
    }
}

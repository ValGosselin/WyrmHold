using System.Runtime.InteropServices;

namespace WyrmHold.App;

/// <summary>
/// Demande à Windows si un jeu tourne en plein écran exclusif (Direct3D). Dans ce mode, le jeu
/// a l'écran pour lui seul : une fenêtre par-dessus ne s'afficherait pas, ou ferait sortir le jeu
/// du plein écran. C'est la même question que Windows se pose avant d'afficher une notification.
/// </summary>
public static class FullScreenDetector
{
    // Valeurs de SHQueryUserNotificationState (énumération QUERY_USER_NOTIFICATION_STATE).
    // On ne regarde que celle-ci : QUNS_BUSY (2) vaut aussi pour un jeu en fenêtré sans bordure,
    // où l'overlay fonctionne très bien.
    private const int QunsRunningD3dFullScreen = 3;

    public static bool IsExclusiveFullScreen()
    {
        try
        {
            return SHQueryUserNotificationState(out int state) == 0 && state == QunsRunningD3dFullScreen;
        }
        catch (Exception)
        {
            // En cas de doute, on ne bloque pas l'overlay.
            return false;
        }
    }

    // Renvoie 0 (S_OK) si la réponse est valable.
    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}

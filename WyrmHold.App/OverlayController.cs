using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Fait le lien entre le détecteur de jeu (GameWatcher), le raccourci global et l'overlay façon Steam
/// (OverlayHubWindow, une fenêtre par partie).
/// Overlay désactivé dans les réglages = pas de raccourci, et pas de surveillance (sauf le temps
/// qu'un jeu lancé depuis Wyrmhold soit suivi pour son temps de jeu, comme avant).
/// </summary>
public sealed class OverlayController : IDisposable
{
    private readonly LibraryService _library;

    // L'overlay de la partie en cours (null = pas encore ouvert pendant cette partie).
    private OverlayHubWindow? _hub;

    // Le raccourci : ouvre ou ferme l'overlay (sur l'onglet de la dernière fois).
    private GlobalHotkey? _hotkey;
    private string _hotkeyText = AppSettings.DefaultOverlayHotkey;

    // ----- Notifications de succès -----

    // Combien de temps une notification reste affichée.
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(4);

    // Écart avec le haut de l'écran.
    private const double ScreenMargin = 16;

    private record ToastContent(string Title, string Name, string Description, string? IconUrl, bool IsGold);

    private readonly AchievementToastWindow _toast = new AchievementToastWindow();

    // Plusieurs succès d'un coup : ils attendent ici et passent l'un après l'autre.
    private readonly Queue<ToastContent> _toasts = new Queue<ToastContent>();
    private readonly DispatcherTimer _toastTimer = new DispatcherTimer { Interval = ToastDuration };

    public OverlayController(LibraryService library)
    {
        _library = library;

        // Le jeu se ferme : son overlay aussi (le prochain jeu aura le sien).
        _library.Watcher.GameStopped += (running, end) =>
        {
            if (_hub?.Running == running)
            {
                CloseHub();
            }
        };

        _library.LiveAchievements.AchievementUnlocked += OnAchievementUnlocked;
        _library.LiveAchievements.GameCompleted += OnGameCompleted;

        // Fin d'une notification : on la cache et on passe à la suivante.
        _toastTimer.Tick += (sender, e) =>
        {
            _toastTimer.Stop();
            _toast.Hide();
            ShowNextToast();
        };
    }

    // Le raccourci réellement utilisé (celui des réglages, ou celui par défaut s'il était illisible).
    public string CurrentHotkey => _hotkeyText;

    // false = Windows a refusé le raccourci (déjà utilisé par une autre application).
    public bool IsHotkeyAccepted { get; private set; } = true;

    /// <summary>
    /// Applique les réglages de l'overlay. Renvoie false si Windows refuse le raccourci.
    /// </summary>
    public bool ApplySettings()
    {
        AppSettings settings = _library.Settings;
        _library.UpdateWatcherState();

        if (!settings.OverlayEnabled)
        {
            _hotkey?.Dispose();
            _hotkey = null;
            IsHotkeyAccepted = true;
            CloseHub();
            return true;
        }

        if (_hotkey is null)
        {
            _hotkey = new GlobalHotkey();
            _hotkey.Pressed += Toggle;
        }

        IsHotkeyAccepted = Register(_hotkey, settings.OverlayHotkey, AppSettings.DefaultOverlayHotkey, "de l'overlay", out _hotkeyText);
        return IsHotkeyAccepted;
    }

    /// <summary>
    /// Enregistre un raccourci écrit dans les réglages (ou celui par défaut s'il est illisible).
    /// usedText reçoit le raccourci réellement utilisé.
    /// </summary>
    private static bool Register(GlobalHotkey hotkey, string settingText, string defaultText, string name, out string usedText)
    {
        if (!HotkeyText.TryParse(settingText, out KeyGesture gesture))
        {
            Logger.Log($"Raccourci {name} illisible ({settingText}) : {defaultText} utilisé.");
            HotkeyText.TryParse(defaultText, out gesture);
            usedText = defaultText;
        }
        else
        {
            usedText = settingText;
        }

        if (!hotkey.TryRegister(gesture))
        {
            Logger.Log($"Raccourci {name} refusé par Windows (déjà utilisé ?) : {usedText}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Libère le raccourci pendant qu'on en tape un nouveau dans les réglages
    /// (sinon Windows l'intercepterait avant la case de saisie). ApplySettings le remet.
    /// </summary>
    public void SuspendHotkey()
    {
        _hotkey?.Unregister();
    }

    /// <summary>Le raccourci : ouvre l'overlay (onglet de la dernière fois) ou le referme.</summary>
    public void Toggle()
    {
        if (_hub is { IsVisible: true })
        {
            _hub.HideHub();
            return;
        }

        OpenHub();
    }

    /// <summary>
    /// Ouvre l'overlay du jeu en cours. Il prend le clavier et la souris (on clique et on tape dedans) ;
    /// en le cachant, Windows rend la main au jeu.
    /// </summary>
    private void OpenHub()
    {
        // En plein écran exclusif, une fenêtre par-dessus ferait sortir le jeu du plein écran : on ne fait rien.
        if (FullScreenDetector.IsExclusiveFullScreen())
        {
            return;
        }

        if (_library.Watcher.Current is not RunningGame running)
        {
            EnqueueToast(new ToastContent("OVERLAY", "Aucun jeu en cours", $"Lance un jeu installé, puis rappuie sur {_hotkeyText}.", null, false));
            return;
        }

        // L'overlay gardé appartient à une autre partie (autre jeu, ou jeu relancé) : on en refait un.
        if (_hub is not null && _hub.Running != running)
        {
            CloseHub();
        }

        if (_hub is null)
        {
            OverlayHubWindow hub = new OverlayHubWindow(_library, running);
            hub.Closed += (sender, e) =>
            {
                if (_hub == hub)
                {
                    _hub = null;
                }
            };
            _hub = hub;
        }

        _hub.Open(_hotkeyText);
    }

    private void CloseHub()
    {
        OverlayHubWindow? hub = _hub;
        _hub = null;
        hub?.Close();
    }

    /// <summary>
    /// Le bouton « Tester la notification » des réglages : une fausse notification, pour voir à quoi elle ressemble.
    /// </summary>
    public void ShowTestToast()
    {
        EnqueueToast(new ToastContent(
            "SUCCÈS DÉBLOQUÉ (TEST)",
            "Notification de test",
            "Si tu lis ceci, les notifications de succès marchent.",
            null,
            false));
    }

    /// <summary>
    /// Le bouton « Tester le 100 % » des réglages : la notification dorée et son son.
    /// </summary>
    public void ShowTestCompletedToast()
    {
        EnqueueToast(new ToastContent("100 % — TOUS LES SUCCÈS ! (TEST)", "Jeu de test", "53 succès débloqués", null, true));
    }

    public void Dispose()
    {
        _toastTimer.Stop();
        _hotkey?.Dispose();
        _hotkey = null;
        CloseHub();
        _toast.Close();
    }

    private void OnAchievementUnlocked(Game game, AchievementDetail achievement)
    {
        _hub?.RefreshAchievements();   // la liste et l'accueil de l'overlay, s'il est affiché

        // Steam affiche déjà ses propres notifications : pas de doublon (réglage coché par défaut).
        if (game.Platform == Platform.Steam && _library.Settings.MuteSteamAchievementNotifications)
        {
            Logger.Log("Notification non affichée : succès Steam (réglage « Ne pas notifier les succès Steam »).");
            return;
        }

        EnqueueToast(new ToastContent("SUCCÈS DÉBLOQUÉ", achievement.Name, achievement.Description, achievement.IconUrl, false));
    }

    private void OnGameCompleted(Game game)
    {

        // Affichée même pour Steam : Steam, lui, ne fête pas le 100 %.
        EnqueueToast(new ToastContent(
            "100 % — TOUS LES SUCCÈS !",
            game.Name,
            $"{game.AchievementsTotal} succès débloqués",
            null,
            true));
    }

    private void EnqueueToast(ToastContent content)
    {
        _toasts.Enqueue(content);

        // Si une notification est déjà affichée, celle-ci attendra son tour.
        if (!_toastTimer.IsEnabled)
        {
            ShowNextToast();
        }
    }

    private void ShowNextToast()
    {
        while (_toasts.TryDequeue(out ToastContent? content))
        {
            // En plein écran exclusif, pas de notification (la base, elle, est déjà à jour).
            if (FullScreenDetector.IsExclusiveFullScreen())
            {
                continue;
            }

            // Tout en haut à droite (par-dessus l'overlay s'il est ouvert : elle s'affiche après lui).
            double top = SystemParameters.WorkArea.Top + ScreenMargin;

            _toast.ShowToast(content.Title, content.Name, content.Description, content.IconUrl, content.IsGold, top);
            _toastTimer.Start();

            if (_library.Settings.AchievementSoundEnabled)
            {
                if (content.IsGold)
                {
                    AchievementSounds.PlayCompleted();
                }
                else
                {
                    AchievementSounds.PlayUnlock();
                }
            }
            return;
        }
    }

}

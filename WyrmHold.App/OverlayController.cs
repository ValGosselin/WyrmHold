using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Fait le lien entre le détecteur de jeu (GameWatcher), le raccourci global et la fenêtre overlay.
/// Overlay désactivé dans les réglages = pas de raccourci, et pas de surveillance (sauf le temps
/// qu'un jeu lancé depuis Wyrmhold soit suivi pour son temps de jeu, comme avant).
/// </summary>
public sealed class OverlayController : IDisposable
{
    private readonly LibraryService _library;
    private readonly OverlayWindow _window = new OverlayWindow();

    // Met à jour le temps de session chaque seconde, seulement quand l'overlay est visible.
    private readonly DispatcherTimer _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

    private GlobalHotkey? _hotkey;
    private string _hotkeyText = AppSettings.DefaultOverlayHotkey;

    // ----- Aide en jeu -----

    // Le second raccourci : ouvre ou ferme la fenêtre Aide du jeu en cours.
    private GlobalHotkey? _helpHotkey;
    private string _helpHotkeyText = AppSettings.DefaultHelpHotkey;

    // La fenêtre Aide ouverte par le raccourci (null = fermée).
    private HelpWindow? _helpWindow;

    // ----- Notifications de succès -----

    // Combien de temps une notification reste affichée.
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(4);

    // Écart entre l'overlay et une notification posée en dessous, et avec le haut de l'écran.
    private const double ToastGap = 8;
    private const double ScreenMargin = 16;

    private record ToastContent(string Title, string Name, string Description, string? IconUrl, bool IsGold);

    private readonly AchievementToastWindow _toast = new AchievementToastWindow();

    // Plusieurs succès d'un coup : ils attendent ici et passent l'un après l'autre.
    private readonly Queue<ToastContent> _toasts = new Queue<ToastContent>();
    private readonly DispatcherTimer _toastTimer = new DispatcherTimer { Interval = ToastDuration };

    public OverlayController(LibraryService library)
    {
        _library = library;

        // Un jeu démarre ou se ferme pendant que l'overlay est affiché : on le redessine.
        _library.Watcher.GameStarted += running => RefreshIfVisible();
        _library.Watcher.GameStopped += (running, end) => RefreshIfVisible();

        _library.LiveAchievements.AchievementUnlocked += OnAchievementUnlocked;
        _library.LiveAchievements.GameCompleted += OnGameCompleted;

        // Fin d'une notification : on la cache et on passe à la suivante.
        _toastTimer.Tick += (sender, e) =>
        {
            _toastTimer.Stop();
            _toast.Hide();
            ShowNextToast();
        };

        _clock.Tick += (sender, e) =>
        {
            // Le jeu est passé en plein écran exclusif pendant que l'overlay était affiché : on se retire.
            if (FullScreenDetector.IsExclusiveFullScreen())
            {
                Hide();
                return;
            }

            if (_library.Watcher.Current is RunningGame running)
            {
                _window.UpdateSessionTime(running);
            }
        };
    }

    // Les raccourcis réellement utilisés (ceux des réglages, ou ceux par défaut s'ils étaient illisibles).
    public string CurrentHotkey => _hotkeyText;
    public string CurrentHelpHotkey => _helpHotkeyText;

    // false = Windows a refusé le raccourci (déjà utilisé par une autre application).
    public bool IsHotkeyAccepted { get; private set; } = true;
    public bool IsHelpHotkeyAccepted { get; private set; } = true;

    /// <summary>
    /// Applique les réglages de l'overlay et de l'aide en jeu. Renvoie false si un des deux raccourcis
    /// est refusé par Windows (IsHotkeyAccepted / IsHelpHotkeyAccepted disent lequel).
    /// </summary>
    public bool ApplySettings()
    {
        AppSettings settings = _library.Settings;
        _library.UpdateWatcherState();

        // L'aide en jeu a besoin du détecteur de jeu : elle suit donc l'overlay.
        if (!settings.OverlayEnabled)
        {
            _hotkey?.Dispose();
            _hotkey = null;
            _helpHotkey?.Dispose();
            _helpHotkey = null;
            IsHotkeyAccepted = true;
            IsHelpHotkeyAccepted = true;
            Hide();
            return true;
        }

        _hotkey ??= CreateHotkey(Toggle);
        _helpHotkey ??= CreateHotkey(ToggleHelp);

        IsHotkeyAccepted = Register(_hotkey, settings.OverlayHotkey, AppSettings.DefaultOverlayHotkey, "de l'overlay", out _hotkeyText);
        IsHelpHotkeyAccepted = Register(_helpHotkey, settings.HelpHotkey, AppSettings.DefaultHelpHotkey, "de l'aide", out _helpHotkeyText);

        return IsHotkeyAccepted && IsHelpHotkeyAccepted;
    }

    private static GlobalHotkey CreateHotkey(Action pressed)
    {
        GlobalHotkey hotkey = new GlobalHotkey();
        hotkey.Pressed += pressed;
        return hotkey;
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
    /// Libère les raccourcis pendant qu'on en tape un nouveau dans les réglages
    /// (sinon Windows l'intercepterait avant la case de saisie). ApplySettings les remet.
    /// </summary>
    public void SuspendHotkey()
    {
        _hotkey?.Unregister();
        _helpHotkey?.Unregister();
    }

    /// <summary>
    /// Le raccourci de l'aide : ouvre la fenêtre Aide du jeu en cours, ou la ferme si elle est ouverte.
    /// Contrairement à l'overlay, elle prend le clavier et la souris (il faut pouvoir cliquer dedans) ;
    /// en la fermant, Windows rend la main au jeu.
    /// </summary>
    public void ToggleHelp()
    {
        if (_helpWindow is not null)
        {
            _helpWindow.Close();   // Closed remet _helpWindow à null
            return;
        }

        // En plein écran exclusif, une fenêtre par-dessus ferait sortir le jeu du plein écran.
        if (FullScreenDetector.IsExclusiveFullScreen())
        {
            return;
        }

        if (_library.Watcher.Current is not RunningGame running)
        {
            EnqueueToast(new ToastContent("AIDE", "Aucun jeu en cours", "Lance un jeu, puis rappuie sur " + _helpHotkeyText + ".", null, false));
            return;
        }

        // L'overlay se range : il serait par-dessus la fenêtre Aide.
        Hide();

        // Topmost : la fenêtre passe devant le jeu (en fenêtré sans bordure, il est lui-même tout devant).
        _helpWindow = new HelpWindow(_library, running.Game) { Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        _helpWindow.Closed += (sender, e) => _helpWindow = null;
        _helpWindow.Show();
        _helpWindow.Activate();
    }

    public void Toggle()
    {
        if (_window.IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
        }
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
        _clock.Stop();
        _toastTimer.Stop();
        _hotkey?.Dispose();
        _hotkey = null;
        _helpHotkey?.Dispose();
        _helpHotkey = null;
        _helpWindow?.Close();
        _window.Close();
        _toast.Close();
    }

    private void OnAchievementUnlocked(Game game, AchievementDetail achievement)
    {
        RefreshIfVisible();   // « 25 restants » devient « 24 restants »

        // Steam affiche déjà ses propres notifications : pas de doublon (réglage coché par défaut).
        if (game.Platform == Platform.Steam && _library.Settings.MuteSteamAchievementNotifications)
        {
            return;
        }

        EnqueueToast(new ToastContent("SUCCÈS DÉBLOQUÉ", achievement.Name, achievement.Description, achievement.IconUrl, false));
    }

    private void OnGameCompleted(Game game)
    {
        RefreshIfVisible();

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

            // Juste sous l'overlay s'il est affiché, sinon tout en haut à droite.
            double top = _window.IsVisible
                ? _window.Bottom + ToastGap
                : SystemParameters.WorkArea.Top + ScreenMargin;

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

    private void Show()
    {
        // En plein écran exclusif, s'afficher ferait sortir le jeu du plein écran : on ne fait rien.
        if (FullScreenDetector.IsExclusiveFullScreen())
        {
            return;
        }

        Refresh();
        _window.Show();
        _clock.Start();
    }

    private void Hide()
    {
        _clock.Stop();
        _window.Hide();
    }

    private void RefreshIfVisible()
    {
        if (_window.IsVisible)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        _window.ShowGame(_library.Watcher.Current, _hotkeyText);
    }
}

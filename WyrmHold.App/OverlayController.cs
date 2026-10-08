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

    public OverlayController(LibraryService library)
    {
        _library = library;

        // Un jeu démarre ou se ferme pendant que l'overlay est affiché : on le redessine.
        _library.Watcher.GameStarted += running => RefreshIfVisible();
        _library.Watcher.GameStopped += (running, end) => RefreshIfVisible();

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

    // Le raccourci réellement utilisé (celui des réglages, ou celui par défaut s'il était illisible).
    public string CurrentHotkey => _hotkeyText;

    /// <summary>
    /// Applique les réglages de l'overlay. Renvoie false si le raccourci est refusé par Windows
    /// (déjà utilisé par une autre application).
    /// </summary>
    public bool ApplySettings()
    {
        AppSettings settings = _library.Settings;
        _library.UpdateWatcherState();

        if (!settings.OverlayEnabled)
        {
            _hotkey?.Dispose();
            _hotkey = null;
            Hide();
            return true;
        }

        if (!HotkeyText.TryParse(settings.OverlayHotkey, out KeyGesture gesture))
        {
            Logger.Log($"Raccourci de l'overlay illisible ({settings.OverlayHotkey}) : {AppSettings.DefaultOverlayHotkey} utilisé.");
            HotkeyText.TryParse(AppSettings.DefaultOverlayHotkey, out gesture);
            _hotkeyText = AppSettings.DefaultOverlayHotkey;
        }
        else
        {
            _hotkeyText = settings.OverlayHotkey;
        }

        if (_hotkey is null)
        {
            _hotkey = new GlobalHotkey();
            _hotkey.Pressed += Toggle;
        }

        if (!_hotkey.TryRegister(gesture))
        {
            Logger.Log($"Raccourci de l'overlay refusé par Windows (déjà utilisé ?) : {_hotkeyText}");
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

    public void Dispose()
    {
        _clock.Stop();
        _hotkey?.Dispose();
        _hotkey = null;
        _window.Close();
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

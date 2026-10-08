using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace WyrmHold.App;

/// <summary>
/// Un raccourci clavier global (RegisterHotKey de Windows) : Windows nous prévient même quand
/// une autre fenêtre, le jeu par exemple, a le clavier. Attention : tant qu'il est enregistré,
/// les autres programmes ne reçoivent plus cette combinaison.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x5748;   // un numéro à nous, au choix

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;   // touche maintenue = un seul message, pas une rafale

    // Une fenêtre invisible « pour messages seulement » (parent HWND_MESSAGE) : elle n'apparaît nulle part
    // et sert uniquement à recevoir le message WM_HOTKEY que Windows envoie à chaque appui.
    private readonly HwndSource _messageWindow;
    private bool _isRegistered;

    public event Action? Pressed;

    public GlobalHotkey()
    {
        HwndSourceParameters parameters = new HwndSourceParameters("WyrmholdHotkey")
        {
            ParentWindow = new IntPtr(-3),   // HWND_MESSAGE
            WindowStyle = 0
        };

        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);
    }

    /// <summary>
    /// Enregistre la combinaison (en remplaçant l'ancienne). Renvoie false si Windows refuse :
    /// en général parce qu'une autre application utilise déjà ce raccourci.
    /// </summary>
    public bool TryRegister(KeyGesture gesture)
    {
        Unregister();

        uint modifiers = ModNoRepeat;

        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= ModAlt;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= ModControl;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= ModShift;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= ModWin;

        uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);

        _isRegistered = RegisterHotKey(_messageWindow.Handle, HotkeyId, modifiers, virtualKey);
        return _isRegistered;
    }

    public void Unregister()
    {
        if (_isRegistered)
        {
            UnregisterHotKey(_messageWindow.Handle, HotkeyId);
            _isRegistered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        _messageWindow.RemoveHook(WndProc);
        _messageWindow.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}

/// <summary>
/// Passe d'un raccourci à son texte (« Ctrl+Shift+W », celui gardé dans settings.json) et inversement.
/// </summary>
public static class HotkeyText
{
    public static bool TryParse(string text, out KeyGesture gesture)
    {
        gesture = null!;

        try
        {
            if (new KeyGestureConverter().ConvertFromInvariantString(text) is KeyGesture parsed && IsAllowed(parsed))
            {
                gesture = parsed;
                return true;
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
        {
            // Texte illisible : on renvoie false, l'appelant reprend le raccourci par défaut.
        }

        return false;
    }

    /// <summary>
    /// Fabrique le texte d'une combinaison tapée au clavier. Refuse les combinaisons sans Ctrl, Alt
    /// ni Windows : un raccourci global sur une touche simple volerait cette touche au jeu.
    /// </summary>
    public static bool TryFormat(ModifierKeys modifiers, Key key, out string text)
    {
        text = "";

        try
        {
            KeyGesture gesture = new KeyGesture(key, modifiers);

            if (!IsAllowed(gesture))
            {
                return false;
            }

            text = gesture.GetDisplayStringForCulture(CultureInfo.InvariantCulture);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsAllowed(KeyGesture gesture)
    {
        return (gesture.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0;
    }
}

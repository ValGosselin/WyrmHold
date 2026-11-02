using System.Runtime.InteropServices;

namespace Wyrmhold.Core;

/// <summary>
/// Garantit qu'une seule copie de Wyrmhold tourne pour cet utilisateur : deux copies écriraient dans la
/// même base et se disputeraient le raccourci de l'overlay. Un Mutex nommé est un verrou partagé par
/// tout Windows : le 1er programme le prend, le suivant le trouve occupé. Windows le libère tout seul si
/// le programme s'arrête, même en plantant. « Local\ » : un verrou par session Windows, comme les données.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    // ASFW_ANY : la copie qui va s'effacer autorise l'autre à passer au premier plan (Windows le refuse sinon).
    private const int AnyProcess = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showRequest;
    private RegisteredWaitHandle? _listener;

    private SingleInstance(Mutex mutex, EventWaitHandle showRequest)
    {
        _mutex = mutex;
        _showRequest = showRequest;
    }

    /// <summary>
    /// Prend le verrou, ou renvoie null si une autre copie tourne : celle-ci vient alors d'être prévenue
    /// de s'afficher. closingDelay : le temps laissé à une copie en train de se fermer pour libérer la place.
    /// </summary>
    public static SingleInstance? TryAcquire(string name, TimeSpan closingDelay)
    {
        var mutex = new Mutex(false, $@"Local\{name}");
        var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Show");

        if (TakeLock(mutex, TimeSpan.Zero))
        {
            return new SingleInstance(mutex, showRequest);
        }

        // On prévient tout de suite : l'autre copie s'affiche sans attendre le délai ci-dessous.
        AllowSetForegroundWindow(AnyProcess);
        showRequest.Set();

        if (TakeLock(mutex, closingDelay))
        {
            return new SingleInstance(mutex, showRequest);
        }

        showRequest.Dispose();
        mutex.Dispose();
        return null;
    }

    private static bool TakeLock(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // L'ancienne copie a planté sans rendre le verrou : il nous revient quand même.
            return true;
        }
    }

    /// <summary>onShowRequest est appelé (sur un autre fil) à chaque fois qu'une nouvelle copie demande à s'afficher.</summary>
    public void ListenForShowRequests(Action onShowRequest)
    {
        _listener = ThreadPool.RegisterWaitForSingleObject(
            _showRequest, (_, _) => onShowRequest(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>À appeler depuis le fil qui a pris le verrou : un Mutex se rend là où il a été pris.</summary>
    public void Dispose()
    {
        _listener?.Unregister(null);
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _showRequest.Dispose();
    }
}

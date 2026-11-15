using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Velopack;
using Wyrmhold.Core;

namespace WyrmHold.App
{
    public partial class App : Application
    {
        // Le point de départ du programme. [STAThread] : obligatoire pour une interface WPF.
        [STAThread]
        public static void Main(string[] args)
        {
            // En tout premier : pendant une installation, une mise à jour ou une désinstallation,
            // Velopack lance le programme avec des arguments spéciaux ; Run() fait alors son travail
            // et ferme le programme avant même qu'une fenêtre s'ouvre. Le reste du temps, il ne fait rien
            // (sauf installer une mise à jour déjà téléchargée).
            // Juste avant une désinstallation : Wyrmhold se retire du démarrage de Windows (sinon Windows chercherait
            // à chaque session un programme qui n'existe plus). Velopack interdit d'afficher quoi que ce soit ici.
            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(version => RemoveFromWindowsStartup())
                .Run();

            // Lancé par Windows à l'ouverture de session (voir WindowsStartup) : on démarrera caché.
            StartedByWindows = args.Contains(WindowsStartup.Argument);

            // Une seule copie à la fois. Wyrmhold est déjà ouvert (même caché dans la zone de notification) :
            // il vient de recevoir la demande de s'afficher, et cette copie-ci s'arrête sans rien ouvrir.
            using SingleInstance? instance = SingleInstance.TryAcquire(InstanceName, TimeSpan.FromSeconds(3));

            if (instance == null)
            {
                return;
            }

            var app = new App();
            app.InitializeComponent();   // lit App.xaml (la 1re fenêtre est ouverte par OnStartup)

            // Dispatcher : la demande arrive sur un autre fil ; on revient sur celui de l'interface.
            instance.ListenForShowRequests(() => app.Dispatcher.BeginInvoke(app.BringWindowToFront));
            app.Run();
        }

        // La version de développement a son propre verrou : elle peut tourner à côté de l'appli installée.
#if DEBUG
        private const string InstanceName = "Wyrmhold.Dev";
#else
        private const string InstanceName = "Wyrmhold";
#endif

        // true quand Windows a lancé Wyrmhold (argument --autostart) : ni fenêtre de mise à jour, ni fenêtre principale.
        public static bool StartedByWindows { get; private set; }

        // Démarrage caché : on a double-cliqué sur Wyrmhold pendant la recherche de mise à jour invisible.
        private bool _showWhenReady;

        // Wyrmhold installé par Velopack (false = lancé depuis Visual Studio ou un dossier de compilation).
        // Calculé à la 1re demande seulement : Velopack doit avoir fait son Run() avant (voir Main).
        public static bool IsInstalled => _isInstalled ??=
            new UpdateManager(new Velopack.Sources.GithubSource(AppInfo.RepositoryUrl, null, false)).IsInstalled;

        private static bool? _isInstalled;

        private static void RemoveFromWindowsStartup()
        {
            try
            {
                WindowsStartup.Disable();
            }
            catch (Exception ex)
            {
                Logger.Log($"Désinstallation : Wyrmhold n'a pas pu se retirer du démarrage de Windows : {ex.Message}");
            }
        }

        private void BringWindowToFront()
        {
            if (MainWindow is MainWindow window)
            {
                window.ShowFromTray();
            }
            else if (StartedByWindows)
            {
                _showWhenReady = true;   // la fenêtre s'ouvrira dès la fin de la recherche (voir StartHiddenAsync)
            }
            else
            {
                MainWindow?.Activate();   // encore sur la recherche de mise à jour
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Les plantages sont enregistrés dès le départ, avant même la première fenêtre.
            DispatcherUnhandledException += OnInterfaceCrash;
            AppDomain.CurrentDomain.UnhandledException += OnBackgroundCrash;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskError;

            // Le thème doit être appliqué AVANT l'ouverture de la première fenêtre :
            // ses styles « BasedOn » vont chercher ceux du thème au moment où elle se charge.
            ThemeManager.Apply(AppSettings.Load().Theme);

            base.OnStartup(e);

            // D'abord la recherche de mise à jour (choix de Val du 10 octobre 2026) : on n'utilise pas une version
            // qui va être remplacée. Pendant ce temps, fermer cette petite fenêtre ne doit pas quitter l'appli
            // (par défaut, WPF s'arrête quand la dernière fenêtre se ferme) : arrêt « à la main » le temps de la recherche.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Démarrage de Windows (choix de Val du 15 novembre 2026) : même recherche, mais sans rien afficher.
            if (StartedByWindows)
            {
                _ = StartHiddenAsync();
                return;
            }

            UpdateWindow updates = new UpdateWindow();
            updates.Closed += (sender, args) => OpenMainWindow(startHidden: false);
            updates.Show();
        }

        /// <summary>
        /// Lancé par Windows : la recherche de mise à jour se fait sans fenêtre (une mise à jour trouvée s'installe
        /// et Wyrmhold redémarre, toujours caché), puis Wyrmhold démarre caché près de l'horloge.
        /// </summary>
        private async Task StartHiddenAsync()
        {
            UpdateWindow updates = new UpdateWindow();   // jamais affichée : on se sert seulement de sa recherche
            await updates.CheckWithoutWindowAsync();
            updates.Close();

            OpenMainWindow(startHidden: !_showWhenReady);
        }

        /// <summary>
        /// Après la recherche de mise à jour (rien à installer, pas de réponse, ou « Passer ») : la vraie fenêtre.
        /// startHidden = true : elle se prépare (bibliothèque, overlay, suivi des parties) sans apparaître à l'écran.
        /// </summary>
        private void OpenMainWindow(bool startHidden)
        {
            MainWindow window = new MainWindow(startHidden);
            MainWindow = window;

            // Retour au comportement normal de WPF (celui d'avant) : l'appli s'arrête quand sa dernière fenêtre se ferme.
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            window.Show();
        }

        // Une erreur non rattrapée dans l'interface. On pourrait continuer, mais l'appli risquerait
        // d'être dans un état bancal : on enregistre le rapport, on prévient, puis on quitte proprement.
        private bool _isCrashing;

        private void OnInterfaceCrash(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;   // évite la fenêtre d'erreur brute de Windows

            // D'autres erreurs peuvent suivre pendant la fermeture : un seul rapport, un seul message.
            if (_isCrashing)
            {
                return;
            }

            _isCrashing = true;
            CrashReporter.Save(e.Exception, "interface");

            MessageBox.Show(
                "Wyrmhold a rencontré une erreur et doit se fermer.\n\n"
                + "Un rapport a été gardé sur ton PC : au prochain démarrage, Wyrmhold te proposera de le signaler.",
                "Wyrmhold",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            if (MainWindow is MainWindow window)
            {
                window.ExitAfterCrash();
            }
            else
            {
                Shutdown();
            }
        }

        // Une erreur dans un autre fil d'exécution : Windows ferme l'appli juste après, on ne peut que noter.
        private void OnBackgroundCrash(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
            {
                CrashReporter.Save(exception, "arrière-plan");
            }
        }

        // Une tâche « lancée sans l'attendre » (« _ = … ») qui a échoué sans que personne ne lise son erreur.
        // .NET ne ferme pas l'appli pour ça : on la note seulement dans le journal.
        private void OnUnobservedTaskError(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Log($"Erreur d'une tâche en arrière-plan : {e.Exception.GetBaseException().Message}");
            e.SetObserved();
        }
    }
}

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
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();   // lit App.xaml (dont StartupUri = MainWindow)
            app.Run();
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

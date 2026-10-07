using System.Windows;
using Wyrmhold.Core;

namespace WyrmHold.App
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Le thème doit être appliqué AVANT l'ouverture de la première fenêtre :
            // ses styles « BasedOn » vont chercher ceux du thème au moment où elle se charge.
            ThemeManager.Apply(AppSettings.Load().Theme);

            base.OnStartup(e);
        }
    }
}

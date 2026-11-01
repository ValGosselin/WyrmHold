using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// La configuration d'un émulateur (Comptes → Émulateurs → Configurer…) : son programme, ses dossiers de jeux,
/// et pour RetroArch le cœur de chaque console. Chaque changement est enregistré tout de suite ; la bibliothèque
/// est relue à la fermeture de la fenêtre (voir MainWindow.EmulatorButton_Click).
/// </summary>
public partial class EmulatorSettingsWindow : Window
{
    private const string AutomaticCore = "Automatique";

    private readonly LibraryService _library;
    private readonly EmulatorProvider _emulator;

    public EmulatorSettingsWindow(LibraryService library, Platform platform)
    {
        InitializeComponent();
        _library = library;
        _emulator = library.GetEmulator(platform);
        Title = $"{_emulator.Name} : configuration";
    }

    // true si un réglage a changé : il faut relire la bibliothèque.
    public bool HasChanges { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ConfiguredFoldersTitle.Text = $"Dossiers déclarés dans {_emulator.Name}";
        Reload();
    }

    private void Reload()
    {
        string? executable = _emulator.FindExecutable();

        ExecutableBox.Text = executable ?? "";
        ExecutableStatusText.Text = executable is null
            ? $"{_emulator.ExecutableName} n'a pas été trouvé. Clique sur « Choisir… » pour l'indiquer, ou lance {_emulator.Name} une fois puis clique sur « Détecter »."
            : _emulator.Settings.ExecutablePath is null ? "Trouvé automatiquement." : "Choisi à la main.";

        ConfiguredFoldersList.ItemsSource = _emulator.GetConfiguredFolders();
        AddedFoldersList.ItemsSource = _emulator.Settings.GameFolders.ToList();

        if (_emulator is RetroArchProvider retroArch)
        {
            CoresPanel.Visibility = Visibility.Visible;
            BuildCoreRows(retroArch);
        }
    }

    private void ChooseButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new OpenFileDialog
        {
            Title = $"Où est {_emulator.ExecutableName} ?",
            Filter = $"{_emulator.ExecutableName}|{_emulator.ExecutableName}|Programmes (*.exe)|*.exe"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _emulator.Settings.ExecutablePath = dialog.FileName;
            SaveChange();
            Reload();
        }
    }

    // Oublie le choix fait à la main : la détection automatique reprend la main.
    private void DetectButton_Click(object sender, RoutedEventArgs e)
    {
        _emulator.Settings.ExecutablePath = null;
        SaveChange();
        Reload();
    }

    private void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new OpenFolderDialog { Title = "Dossier de jeux" };

        if (dialog.ShowDialog(this) != true
            || _emulator.Settings.GameFolders.Contains(dialog.FolderName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        // Une nouvelle liste plutôt qu'un ajout : une lecture de la bibliothèque peut être en train de parcourir l'ancienne.
        _emulator.Settings.GameFolders = _emulator.Settings.GameFolders.Append(dialog.FolderName).ToList();
        SaveChange();
        Reload();
    }

    private void RemoveFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (AddedFoldersList.SelectedItem is not string folder)
        {
            MessageBox.Show("Sélectionne d'abord le dossier à retirer.", "Wyrmhold");
            return;
        }

        _emulator.Settings.GameFolders = _emulator.Settings.GameFolders.Where(other => other != folder).ToList();
        SaveChange();
        Reload();
    }

    private void BuildCoreRows(RetroArchProvider retroArch)
    {
        List<string> installed = retroArch.GetInstalledCores();

        CoresHelpText.Text = installed.Count == 0
            ? "Aucun cœur installé : les jeux ne pourront pas se lancer. Ouvre RetroArch → Mise à jour en ligne (Online Updater) → Téléchargeur de cœurs (Core Downloader)."
            : "« Automatique » prend le cœur de la playlist RetroArch, sinon le premier cœur connu installé.";

        CoreRows.Children.Clear();

        foreach (GameConsole console in RetroArchProvider.Consoles)
        {
            ComboBox choice = new ComboBox { Tag = console, Width = 280 };
            choice.Items.Add(AutomaticCore);

            foreach (string core in installed)
            {
                choice.Items.Add(core);
            }

            choice.SelectedItem = _emulator.Settings.Cores.TryGetValue(console.ToString(), out string? chosen) && installed.Contains(chosen)
                ? chosen
                : AutomaticCore;
            choice.SelectionChanged += CoreChoice_SelectionChanged;

            DockPanel row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(choice, Dock.Right);
            row.Children.Add(choice);
            row.Children.Add(new TextBlock { Text = GameConsoles.Name(console), VerticalAlignment = VerticalAlignment.Center });
            CoreRows.Children.Add(row);
        }
    }

    private void CoreChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { Tag: GameConsole console, SelectedItem: string core })
        {
            return;
        }

        if (core == AutomaticCore)
        {
            _emulator.Settings.Cores.Remove(console.ToString());
        }
        else
        {
            _emulator.Settings.Cores[console.ToString()] = core;
        }

        SaveChange();
    }

    private void SaveChange()
    {
        HasChanges = true;

        try
        {
            _library.SaveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Le réglage s'applique quand même : il ne sera juste pas retenu au prochain démarrage.
            Logger.Log($"Réglages impossibles à enregistrer : {ex.Message}");
        }
    }
}

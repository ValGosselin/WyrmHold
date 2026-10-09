using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Les onglets de l'overlay.
/// </summary>
public enum HubTab
{
    Home,
    Achievements,
    Help
}

/// <summary>
/// L'overlay en jeu, façon Steam : tout en un (succès, session, aide, bloc-notes, infos PC), en plein écran
/// par-dessus le jeu. Une fenêtre par partie : OverlayController la crée à la 1re ouverture, la cache et
/// la réaffiche (les onglets gardent leur état, ex. la page de guide ouverte), et la ferme quand le jeu se ferme.
/// Cachée, elle ne fait rien : ni horloge, ni lecture des succès.
/// </summary>
public partial class OverlayHubWindow : Window
{
    private readonly LibraryService _library;
    private readonly RunningGame _running;

    // Le même panneau que la fenêtre « Voir les succès » : il lit les succès, et l'accueil s'en sert aussi.
    private readonly AchievementsPanel _achievementsPanel;

    // Créé à la 1re visite de l'onglet Aide : le navigateur est lourd, inutile de le lancer pour rien.
    private HelpPanel? _helpPanel;

    // Chaque seconde, tant que l'overlay est affiché : durée de session, heure, processeur, mémoire.
    private readonly DispatcherTimer _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    private readonly PcUsage _pc = new PcUsage();

    // Le bloc-notes s'enregistre 1 s après la dernière frappe (et à la fermeture), pas à chaque lettre.
    private readonly DispatcherTimer _noteSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    private bool _isNoteDirty;
    private bool _isLoadingNote;

    private bool _isHiding;

    public OverlayHubWindow(LibraryService library, RunningGame running)
    {
        InitializeComponent();
        _library = library;
        _running = running;

        _achievementsPanel = new AchievementsPanel(library, running.Game) { ShowGameName = false };
        _achievementsPanel.HelpRequested += SearchHelp;
        _achievementsPanel.AchievementsChanged += UpdateHomeAchievements;
        AchievementsTab.Child = _achievementsPanel;

        _clock.Tick += (sender, e) => UpdateClock();
        _noteSaveTimer.Tick += (sender, e) => SaveNote();

        ShowGameInfo();
        LoadNote();
        SelectTab(HubTab.Home);
    }

    // La partie suivie : l'overlay d'un autre jeu doit être recréé.
    public RunningGame Running => _running;

    public HubTab CurrentTab { get; private set; }

    // Windows vient de créer la vraie fenêtre : on la retire d'Alt+Tab (ce n'est pas une fenêtre « à part »).
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        GhostWindow.HideFromAltTab(this);
    }

    /// <summary>
    /// Affiche l'overlay (sur l'onglet de la dernière fois) et lui donne le clavier et la souris.
    /// </summary>
    public void Open(string hotkeyText)
    {
        // Tout l'écran principal, barre des tâches comprise (le jeu en fenêtré sans bordure l'occupe aussi).
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        HotkeyHintText.Text = $"Échap ou {hotkeyText}";

        Show();
        Activate();

        UpdateClock();
        _clock.Start();

        // 1re ouverture : lecture des succès ; ensuite, relecture (tu as pu en débloquer en jouant).
        _ = _achievementsPanel.StartAsync();
        _achievementsPanel.Resume();
    }

    /// <summary>Cache l'overlay : Windows rend alors la main au jeu.</summary>
    public void HideHub()
    {
        if (_isHiding || !IsVisible)
        {
            return;
        }

        _isHiding = true;

        try
        {
            SaveNote();
            _clock.Stop();
            _achievementsPanel.Pause();
            Hide();
        }
        finally
        {
            _isHiding = false;
        }
    }

    /// <summary>Un succès vient d'être débloqué : si l'overlay est affiché, on relit la liste.</summary>
    public void RefreshAchievements()
    {
        if (IsVisible)
        {
            _ = _achievementsPanel.RefreshNowAsync();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _clock.Stop();
        SaveNote();
        _achievementsPanel.Stop();
        _helpPanel?.DisposeBrowser();
        base.OnClosed(e);
    }

    // ----- Onglets -----

    public void SelectTab(HubTab tab)
    {
        CurrentTab = tab;

        if (tab == HubTab.Help)
        {
            EnsureHelpPanel();
        }

        // Hidden et pas Collapsed : le navigateur de l'Aide ne finit jamais de démarrer s'il est dans un onglet
        // « Collapsed » (vérifié le 9 octobre 2026) ; avec Hidden, il démarre même si tu changes d'onglet entre-temps.
        HomeTab.Visibility = tab == HubTab.Home ? Visibility.Visible : Visibility.Hidden;
        AchievementsTab.Visibility = tab == HubTab.Achievements ? Visibility.Visible : Visibility.Hidden;
        HelpTab.Visibility = tab == HubTab.Help ? Visibility.Visible : Visibility.Hidden;

        HomeTabButton.IsChecked = tab == HubTab.Home;
        AchievementsTabButton.IsChecked = tab == HubTab.Achievements;
        HelpTabButton.IsChecked = tab == HubTab.Help;
    }

    private void HomeTabButton_Click(object sender, RoutedEventArgs e) => SelectTab(HubTab.Home);

    private void AchievementsTabButton_Click(object sender, RoutedEventArgs e) => SelectTab(HubTab.Achievements);

    private void HelpTabButton_Click(object sender, RoutedEventArgs e) => SelectTab(HubTab.Help);

    private void AllAchievements_Click(object sender, RoutedEventArgs e) => SelectTab(HubTab.Achievements);

    private void BackToGame_Click(object sender, RoutedEventArgs e) => HideHub();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideHub();
        }
    }

    // Une autre fenêtre passe devant (Alt+Tab, « Ouvrir dans mon navigateur »…) : on se range, comme Steam.
    private void Window_Deactivated(object? sender, EventArgs e)
    {
        HideHub();
    }

    // ----- Aide -----

    private void EnsureHelpPanel()
    {
        if (_helpPanel is not null)
        {
            return;
        }

        _helpPanel = new HelpPanel(_library, _running.Game, null) { ShowGameName = false };
        _helpPanel.AchievementsRequested += () => SelectTab(HubTab.Achievements);
        HelpTab.Child = _helpPanel;
        _ = _helpPanel.StartAsync();
    }

    /// <summary>« Comment l'obtenir ? » : la recherche s'ouvre dans l'onglet Aide.</summary>
    public void SearchHelp(string words)
    {
        EnsureHelpPanel();
        _helpPanel!.SearchFor(words);
        SelectTab(HubTab.Help);
    }

    private void HowToGet_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AchievementRow row)
        {
            SearchHelp(row.HelpSearchWords);
        }
    }

    // ----- Barre du haut et session -----

    private void ShowGameInfo()
    {
        Game game = _running.Game;

        GameNameText.Text = game.Name;
        GameInfoText.Text = string.Join(" · ", new[] { game.PlatformName, game.Developers ?? "" }.Where(part => part.Length > 0));
        CoverLoader.SetPath(CoverImage, game.CoverPath);

        SessionStartText.Text = _running.Start.LocalDateTime.ToString("HH:mm");
        TotalPlaytimeText.Text = game.PlaytimeMinutes > 0 ? game.PlaytimeText : "—";

        // Une date pendant cette session (Steam la met à jour au lancement) n'est pas la « dernière partie ».
        LastPlayedText.Text = game.LastPlayedUnix > 0 && game.LastPlayedUnix < _running.Start.ToUnixTimeSeconds()
            ? game.LastPlayedText
            : "—";
    }

    private void UpdateClock()
    {
        string duration = FormatDuration(_running.Elapsed);
        SessionText.Text = duration;
        SessionDurationText.Text = duration;
        ClockText.Text = DateTime.Now.ToString("HH:mm");

        if (_pc.ReadCpuPercent() is double cpu)
        {
            CpuText.Text = $"{cpu:0} %";
        }

        if (PcUsage.ReadMemory() is (double used, double total))
        {
            MemoryText.Text = $"{used:0.0} / {total:0} Go";
        }
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours} h {elapsed.Minutes:00}"
            : $"{elapsed.Minutes} min {elapsed.Seconds:00}";
    }

    // ----- Accueil : résumé des succès -----

    // Combien de succès dans chaque liste de l'accueil (le reste est dans l'onglet Succès).
    private const int SessionUnlocksShown = 5;
    private const int GoalsShown = 3;

    /// <summary>
    /// Le panneau des succès vient de lire la liste : on en tire les résumés de l'accueil.
    /// </summary>
    private void UpdateHomeAchievements(IReadOnlyList<AchievementDetail> achievements)
    {
        Game game = _running.Game;

        if (achievements.Count == 0)
        {
            HomeProgressBar.Value = 0;
            HomeProgressText.Text = "";
            HomeAchievementsStatus.Text = _achievementsPanel.StatusMessage.Length > 0
                ? _achievementsPanel.StatusMessage
                : "Pas de succès connus pour ce jeu.";
            HomeAchievementsSections.Visibility = Visibility.Collapsed;
            SessionAchievementsText.Text = "—";
            return;
        }

        HomeProgressBar.Value = game.AchievementsRatio;
        HomeProgressText.Text = game.AchievementsDetailText;
        HomeAchievementsSections.Visibility = Visibility.Visible;

        // Débloqués depuis le début de la session (la date vient de la source, ou du fichier local de Steam).
        long sessionStart = _running.Start.ToUnixTimeSeconds();
        List<AchievementDetail> sessionUnlocks = achievements
            .Where(a => a.IsUnlocked && a.UnlockedUnix >= sessionStart)
            .OrderByDescending(a => a.UnlockedUnix)
            .ToList();

        SessionAchievementsText.Text = sessionUnlocks.Count.ToString();
        SessionUnlocksList.ItemsSource = ToRows(sessionUnlocks.Take(SessionUnlocksShown));
        SessionUnlocksEmpty.Visibility = sessionUnlocks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        List<AchievementDetail> locked = achievements.Where(a => !a.IsUnlocked).ToList();

        // Prochains objectifs : d'abord ceux qui ont une barre d'avancement (les plus avancés),
        // puis les plus faciles (ceux que le plus de joueurs ont).
        List<AchievementDetail> nextGoals = locked
            .Where(a => a.HasProgress)
            .OrderByDescending(a => a.ProgressRatio)
            .Take(GoalsShown)
            .ToList();
        nextGoals.AddRange(locked
            .Except(nextGoals)
            .OrderByDescending(a => a.RarityPercent ?? -1)
            .ThenBy(a => a.Order)
            .Take(GoalsShown - nextGoals.Count));

        // Les plus rares : seulement ceux dont la rareté est connue, et pas déjà dans les objectifs.
        List<AchievementDetail> rarest = locked
            .Except(nextGoals)
            .Where(a => a.RarityPercent is not null)
            .OrderBy(a => a.RarityPercent)
            .Take(GoalsShown)
            .ToList();

        NextGoalsList.ItemsSource = ToRows(nextGoals);
        NextGoalsTitle.Visibility = nextGoals.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RarestList.ItemsSource = ToRows(rarest);
        RarestTitle.Visibility = rarest.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        HomeAchievementsStatus.Text = locked.Count == 0 ? "100 % : tous les succès sont débloqués, bravo !" : "";
    }

    // Les lignes de l'accueil ne révèlent jamais un succès caché (même règle que la liste complète).
    private static List<AchievementRow> ToRows(IEnumerable<AchievementDetail> achievements)
    {
        return achievements.Select(a => new AchievementRow(a, revealHidden: false)).ToList();
    }

    // ----- Bloc-notes -----

    private void LoadNote()
    {
        _isLoadingNote = true;

        try
        {
            NoteBox.Text = _library.GetGameNote(_running.Game);
        }
        catch (Exception ex)
        {
            Logger.Log($"Bloc-notes illisible pour {_running.Game.Name} : {ex.Message}");
        }
        finally
        {
            _isLoadingNote = false;
        }
    }

    private void NoteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingNote)
        {
            return;
        }

        _isNoteDirty = true;
        NoteStatusText.Text = "…";

        // On repart pour 1 s à chaque frappe : l'enregistrement se fait quand tu t'arrêtes d'écrire.
        _noteSaveTimer.Stop();
        _noteSaveTimer.Start();
    }

    private void SaveNote()
    {
        _noteSaveTimer.Stop();

        if (!_isNoteDirty)
        {
            return;
        }

        try
        {
            _library.SaveGameNote(_running.Game, NoteBox.Text);
            _isNoteDirty = false;
            NoteStatusText.Text = "Enregistré ✓";
        }
        catch (Exception ex)
        {
            Logger.Log($"Bloc-notes impossible à enregistrer pour {_running.Game.Name} : {ex.Message}");
            NoteStatusText.Text = "Pas enregistré (voir le journal)";
        }
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wyrmhold.Core;

namespace WyrmHold.App;

public partial class AchievementsWindow : Window
{
    private readonly LibraryService _library;
    private readonly Game _game;
    private List<AchievementDetail> _achievements = new List<AchievementDetail>();

    // Relit les succès toutes les minutes tant que la fenêtre est ouverte (les barres avancent pendant que tu joues).
    private readonly DispatcherTimer _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
    private bool _isRefreshing;

    public AchievementsWindow(LibraryService library, Game game)
    {
        InitializeComponent();
        _library = library;
        _game = game;

        Title = $"Succès — {game.Name}";
        GameNameText.Text = game.Name;

        _refreshTimer.Tick += RefreshTimer_Tick;
        Closed += (sender, e) => _refreshTimer.Stop();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_library.Settings.IsAchievementSourceEnabled(_game.Platform))
        {
            StatusText.Text = $"Les succès {_game.PlatformName} sont désactivés dans l'onglet Réglages.";
            return;
        }

        StatusText.Text = "Chargement des succès…";

        try
        {
            _achievements = await _library.GetAchievementDetailsAsync(_game);
        }
        catch (Exception ex)
        {
            Logger.Log($"Liste des succès impossible à lire pour {_game.Name} : {ex.Message}");
            StatusText.Text = "Impossible de lire les succès pour le moment. Les détails sont dans le journal.";
            return;
        }

        if (_achievements.Count == 0)
        {
            StatusText.Text = "Ce jeu n'a pas de succès.";
            return;
        }

        UpdateHeader();
        ShowAchievements();
        await LoadStatsAsync();
        _refreshTimer.Start();
    }

    /// <summary>
    /// Les statistiques sont un bonus : en cas d'erreur, on cache simplement la section.
    /// </summary>
    private async Task LoadStatsAsync()
    {
        List<GameStat> stats;

        try
        {
            stats = await _library.GetGameStatsAsync(_game);
        }
        catch (Exception ex)
        {
            Logger.Log($"Statistiques impossibles à lire pour {_game.Name} : {ex.Message}");
            stats = new List<GameStat>();
        }

        StatsList.ItemsSource = stats;
        StatsExpander.Header = $"Statistiques du jeu ({stats.Count})";
        StatsExpander.Visibility = stats.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        // Si Steam met plus d'une minute à répondre, on ne lance pas une 2e lecture par-dessus la 1re.
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;

        try
        {
            List<AchievementDetail> fresh = await _library.GetAchievementDetailsAsync(_game);

            // Un succès qui vient d'être débloqué change de groupe (« À faire » → « Débloqués ») :
            // il faut refaire la liste. Sinon, seules les barres bougent : on met les lignes à jour sur place,
            // ce qui garde ta position dans la liste.
            HashSet<string> unlockedBefore = _achievements.Where(a => a.IsUnlocked).Select(a => a.Id).ToHashSet();
            bool unlocksChanged = fresh.Count != _achievements.Count
                || !fresh.Where(a => a.IsUnlocked).Select(a => a.Id).ToHashSet().SetEquals(unlockedBefore);

            _achievements = fresh;
            UpdateHeader();

            if (unlocksChanged)
            {
                ShowAchievements();
            }
            else
            {
                UpdateRowsInPlace();
            }

            await LoadStatsAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"Actualisation des succès impossible pour {_game.Name} : {ex.Message}");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void UpdateHeader()
    {
        // GetAchievementDetailsAsync vient de mettre à jour la progression du jeu.
        ProgressBar.Value = _game.AchievementsRatio;
        ProgressText.Text = _game.AchievementsDetailText;
        RefreshInfoText.Text = $"Actualisé à {DateTime.Now:HH:mm} · se met à jour toutes les minutes tant que cette fenêtre est ouverte";
    }

    private void ShowFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowAchievements();
    }

    private void SortMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowAchievements();
    }

    private void RevealHidden_Changed(object sender, RoutedEventArgs e)
    {
        ShowAchievements();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ShowAchievements();
    }

    /// <summary>
    /// « Comment l'obtenir ? » : ouvre la fenêtre Aide avec « nom du succès + succès » déjà cherché sur Google
    /// (les boutons YouTube, forums Steam et Reddit reprennent les mêmes mots).
    /// </summary>
    private void HowToGet_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AchievementRow row)
        {
            return;
        }

        string words = $"{row.Name} succès";

        // Une aide est déjà reliée : la recherche s'y fait, sans ouvrir de 2e fenêtre.
        if (LinkedHelp is not null)
        {
            LinkedHelp.SearchFor(words);
            return;
        }

        // Pas d'Owner (voir HelpWindow.AchievementsButton_Click) ; l'aide se ferme avec cette fenêtre.
        HelpWindow window = new HelpWindow(_library, _game, words)
        {
            Topmost = Topmost,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        HelpWindow.Link(window, this, opener: this);
        window.Show();
    }

    // La fenêtre Aide reliée (au plus une), voir HelpWindow.Link.
    public HelpWindow? LinkedHelp { get; set; }

    /// <summary>
    /// Filtre, trie, puis transforme chaque succès en ligne à afficher.
    /// </summary>
    private void ShowAchievements()
    {
        // Pendant InitializeComponent, les ComboBox déclenchent déjà leur événement.
        if (!IsLoaded || _achievements.Count == 0)
        {
            return;
        }

        string show = ReadSelectedTag(ShowFilter);
        string sort = ReadSelectedTag(SortMode);
        bool revealHidden = RevealHidden.IsChecked == true;

        IEnumerable<AchievementDetail> shown = show switch
        {
            "todo" => _achievements.Where(a => !a.IsUnlocked),
            "unlocked" => _achievements.Where(a => a.IsUnlocked),
            _ => _achievements
        };

        // Rareté = pourcentage des joueurs qui l'ont : plus il est haut, plus le succès est facile.
        // Une rareté inconnue passe à la fin dans les deux sens.
        shown = sort switch
        {
            "rarest" => shown.OrderBy(a => a.RarityPercent ?? double.MaxValue),
            "game" => shown.OrderBy(a => a.Order),
            "recent" => shown.OrderByDescending(a => a.UnlockedUnix),
            _ => shown.OrderByDescending(a => a.RarityPercent ?? -1)
        };

        List<AchievementRow> rows = shown.Select(a => new AchievementRow(a, revealHidden)).ToList();

        // On cherche dans le texte affiché : un succès caché ne se trahit pas par sa vraie description.
        // Normalize retire espaces, ponctuation et majuscules, comme la recherche de la bibliothèque.
        string searchKey = NameTools.Normalize(SearchBox.Text);
        if (searchKey.Length > 0)
        {
            rows = rows.Where(row => NameTools.Normalize(row.SearchText).Contains(searchKey)).ToList();
        }

        AchievementsList.ItemsSource = rows;

        StatusText.Text = rows.Count > 0 ? ""
            : searchKey.Length > 0 ? "Aucun succès ne correspond à ta recherche."
            : show == "todo" ? "Tout est débloqué : bravo !"
            : "Aucun succès débloqué pour l'instant.";
    }

    /// <summary>
    /// Donne à chaque ligne affichée la version fraîche de son succès (même identifiant).
    /// </summary>
    private void UpdateRowsInPlace()
    {
        if (AchievementsList.ItemsSource is not List<AchievementRow> rows)
        {
            return;
        }

        Dictionary<string, AchievementDetail> freshById = _achievements
            .GroupBy(a => a.Id)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (AchievementRow row in rows)
        {
            if (freshById.TryGetValue(row.Id, out AchievementDetail? fresh))
            {
                row.Update(fresh);
            }
        }
    }

    private static string ReadSelectedTag(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    }
}

/// <summary>
/// Un succès tel qu'on l'affiche : un succès caché pas encore débloqué garde sa description secrète,
/// sauf si tu as coché « Révéler les succès cachés ».
/// INotifyPropertyChanged : quand Update() change le succès, la ligne se redessine toute seule.
/// </summary>
public class AchievementRow : INotifyPropertyChanged
{
    private const string HiddenName = "Succès caché";

    private readonly bool _revealHidden;
    private AchievementDetail _achievement;

    public AchievementRow(AchievementDetail achievement, bool revealHidden)
    {
        _achievement = achievement;
        _revealHidden = revealHidden;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool KeepSecret => _achievement.IsHidden && !_achievement.IsUnlocked && !_revealHidden;

    public string Id => _achievement.Id;

    public string Name => KeepSecret ? HiddenName : _achievement.Name;

    public string Description => KeepSecret
        ? "Description masquée : coche « Révéler les succès cachés » pour la lire."
        : _achievement.HasDescription
            ? _achievement.Description
            : "Pas de description fournie pour ce succès.";

    // Texte où la recherche regarde : le vrai nom et la vraie description, sans les messages d'aide.
    // Un succès gardé secret ne trouve rien : sa description ne doit pas se deviner.
    public string SearchText => KeepSecret ? "" : _achievement.Name + " " + _achievement.Description;

    public string? IconUrl => _achievement.IconUrl;
    public string RarityText => _achievement.RarityText;
    public string UnlockedText => _achievement.UnlockedText;

    public Visibility ProgressVisibility => _achievement.HasProgress && !_achievement.IsUnlocked
        ? Visibility.Visible
        : Visibility.Collapsed;

    // « Comment l'obtenir ? » : seulement pour un succès à faire, et jamais pour un succès gardé secret
    // (la recherche dévoilerait ce qu'il cache).
    public Visibility HowToGetVisibility => !_achievement.IsUnlocked && !KeepSecret
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double ProgressRatio => _achievement.ProgressRatio;
    public string ProgressText => _achievement.ProgressText;

    public void Update(AchievementDetail achievement)
    {
        _achievement = achievement;

        // Chaîne vide = « toutes les propriétés ont changé ».
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

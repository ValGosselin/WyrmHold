using System.Windows;

namespace WyrmHold.App;

/// <summary>
/// Un thème = un mode de base (le thème Fluent de WPF, clair ou sombre, qui habille les boutons, listes…)
/// + une palette de couleurs à nous (Themes/*.xaml) pour les cartes, badges et textes secondaires.
/// Pour ajouter un thème plus tard (boutique de thèmes) : une palette de plus et une ligne dans Themes.
/// </summary>
public record AppTheme(string Id, string Name, ThemeMode BaseMode, string ColorsFile);

public static class ThemeManager
{
    public static readonly IReadOnlyList<AppTheme> Themes = new List<AppTheme>
    {
        new AppTheme("light", "Clair", ThemeMode.Light, "Themes/LightColors.xaml"),
        new AppTheme("dark", "Sombre", ThemeMode.Dark, "Themes/DarkColors.xaml")
    };

    // La palette actuellement chargée, pour pouvoir la retirer au changement de thème.
    private static ResourceDictionary? _currentColors;

    public static AppTheme Current { get; private set; } = Themes[0];

    /// <summary>
    /// Applique un thème à toute l'application, fenêtres déjà ouvertes comprises.
    /// Un identifiant inconnu donne le premier thème (clair).
    /// </summary>
    public static void Apply(string themeId)
    {
        AppTheme theme = Themes.FirstOrDefault(t => t.Id == themeId) ?? Themes[0];

        // 1. Le thème Fluent de WPF (clair ou sombre) pour tous les contrôles standard.
        Application.Current.ThemeMode = theme.BaseMode;

        // 2. Notre palette : on remplace l'ancienne par la nouvelle. Les éléments qui utilisent
        //    {DynamicResource ...} se mettent à jour tout seuls.
        ResourceDictionary colors = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{theme.ColorsFile}", UriKind.Absolute)
        };

        ICollection<ResourceDictionary> merged = Application.Current.Resources.MergedDictionaries;

        if (_currentColors is not null)
        {
            merged.Remove(_currentColors);
        }

        merged.Add(colors);
        _currentColors = colors;
        Current = theme;
    }
}

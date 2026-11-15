using System.ComponentModel;
using System.Globalization;

namespace Wyrmhold.Core;

/// <summary>
/// Une case du calendrier : un jour, et le nombre de succès débloqués ce jour-là.
/// </summary>
public record CalendarDay(DateTime Date, int Count, bool IsInYear)
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    // Le nombre écrit dans la case (rien pour 0).
    public string CountText => Count > 0 ? Count.ToString(French) : "";

    // L'intensité de la couleur : 0 = aucun succès, puis 1 à 4. Seuils fixes, pour comparer deux années entre elles.
    public int Level => Count switch
    {
        0 => 0,
        1 => 1,
        < 5 => 2,
        < 10 => 3,
        _ => 4
    };

    public string ToolTip => Count switch
    {
        0 => $"Aucun succès le {Date.ToString("dddd d MMMM yyyy", French)}",
        1 => $"1 succès le {Date.ToString("dddd d MMMM yyyy", French)}",
        _ => $"{Count} succès le {Date.ToString("dddd d MMMM yyyy", French)}"
    };
}

/// <summary>
/// Une colonne du calendrier : une semaine, du lundi au dimanche. MonthLabel = nom du mois si la semaine contient le 1er.
/// </summary>
public record CalendarWeek(List<CalendarDay> Days, string MonthLabel);

/// <summary>
/// Le calendrier des succès de la page Statistiques : une année, une case par jour (53 semaines × 7 jours, façon GitHub),
/// avec le nombre de succès dans la case. Les flèches changent d'année sans tout recalculer.
/// </summary>
public class AchievementCalendar : INotifyPropertyChanged
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    // Nombre de succès par jour (date locale, sans l'heure).
    private readonly Dictionary<DateTime, int> _perDay;

    private readonly int _firstYear;
    private readonly int _lastYear;

    public AchievementCalendar(Dictionary<DateTime, int> perDay)
    {
        _perDay = perDay;
        _lastYear = DateTime.Today.Year;
        _firstYear = perDay.Count == 0 ? _lastYear : Math.Min(_lastYear, perDay.Keys.Min().Year);
        ShowYear(_lastYear);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Year { get; private set; }
    public List<CalendarWeek> Weeks { get; private set; } = new List<CalendarWeek>();
    public int YearCount { get; private set; }

    public string YearText => Year.ToString(CultureInfo.InvariantCulture);

    public string YearCountText => YearCount switch
    {
        0 => $"Aucun succès daté en {Year}",
        1 => $"1 succès en {Year}",
        _ => $"{YearCount.ToString("N0", French)} succès en {Year}"
    };

    public bool CanGoPrevious => Year > _firstYear;
    public bool CanGoNext => Year < _lastYear;

    public void ShowPreviousYear()
    {
        if (CanGoPrevious)
        {
            ShowYear(Year - 1);
        }
    }

    public void ShowNextYear()
    {
        if (CanGoNext)
        {
            ShowYear(Year + 1);
        }
    }

    private void ShowYear(int year)
    {
        Year = year;
        DateTime firstDay = new DateTime(year, 1, 1);
        DateTime lastDay = new DateTime(year, 12, 31);

        // La première colonne commence le lundi de la semaine du 1er janvier ; les jours d'une autre année restent vides.
        DateTime monday = firstDay.AddDays(-(((int)firstDay.DayOfWeek + 6) % 7));
        List<CalendarWeek> weeks = new List<CalendarWeek>();

        while (monday <= lastDay)
        {
            List<CalendarDay> days = Enumerable.Range(0, 7)
                .Select(i => monday.AddDays(i))
                .Select(date => new CalendarDay(date, date.Year == year ? _perDay.GetValueOrDefault(date) : 0, date.Year == year))
                .ToList();

            // Le nom du mois au-dessus de la semaine qui contient son 1er jour (« janv. », « févr. »…).
            CalendarDay? firstOfMonth = days.FirstOrDefault(d => d.IsInYear && d.Date.Day == 1);
            string label = firstOfMonth is null ? "" : firstOfMonth.Date.ToString("MMM", French);

            weeks.Add(new CalendarWeek(days, label));
            monday = monday.AddDays(7);
        }

        Weeks = weeks;
        YearCount = _perDay.Where(entry => entry.Key.Year == year).Sum(entry => entry.Value);

        // Chaîne vide = « toutes les propriétés ont changé ».
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

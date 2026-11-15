using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Wyrmhold.Core;

namespace WyrmHold.App;

/// <summary>
/// Un anneau (camembert troué) dessiné à la main : une part par StatSlice, dans l'ordre de la liste,
/// en partant du haut et dans le sens des aiguilles d'une montre. Pas de bibliothèque de graphiques à ajouter.
/// </summary>
public class DonutChart : FrameworkElement
{
    // Les parts à dessiner.
    public static readonly DependencyProperty SlicesProperty = DependencyProperty.Register(
        nameof(Slices), typeof(IEnumerable<StatSlice>), typeof(DonutChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    // L'épaisseur de l'anneau, en pixels.
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(DonutChart),
        new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<StatSlice>? Slices
    {
        get => (IEnumerable<StatSlice>?)GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        List<StatSlice> slices = Slices?.Where(s => s.Value > 0).ToList() ?? new List<StatSlice>();
        double total = slices.Sum(s => s.Value);

        // Chaque part est un arc de cercle tracé avec un trait épais : le milieu du trait est à mi-épaisseur de l'anneau.
        double radius = (Math.Min(ActualWidth, ActualHeight) - Thickness) / 2;
        Point center = new Point(ActualWidth / 2, ActualHeight / 2);

        if (radius <= 0)
        {
            return;
        }

        // Rien à montrer : un anneau gris vide.
        if (total <= 0)
        {
            drawing.DrawEllipse(null, new Pen(FindBrush("SubtleBrush"), Thickness), center, radius, radius);
            return;
        }

        double startAngle = 0;

        foreach (StatSlice slice in slices)
        {
            double sweep = 360 * slice.Value / total;
            Pen pen = new Pen(FindBrush(slice.BrushKey), Thickness);

            // Un arc ne peut pas faire un tour complet : une part à 100 % est un cercle entier.
            if (sweep >= 359.99)
            {
                drawing.DrawEllipse(null, pen, center, radius, radius);
                return;
            }

            drawing.DrawGeometry(null, pen, Arc(center, radius, startAngle, startAngle + sweep));
            startAngle += sweep;
        }
    }

    // Un arc de cercle entre deux angles en degrés (0 = en haut, sens des aiguilles d'une montre).
    private static Geometry Arc(Point center, double radius, double fromDegrees, double toDegrees)
    {
        StreamGeometry geometry = new StreamGeometry();

        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(PointAt(center, radius, fromDegrees), false, false);
            context.ArcTo(PointAt(center, radius, toDegrees), new Size(radius, radius), 0,
                isLargeArc: toDegrees - fromDegrees > 180, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point PointAt(Point center, double radius, double degrees)
    {
        // À l'écran, l'axe vertical descend : on enlève 90° pour que 0° soit en haut.
        double radians = (degrees - 90) * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private Brush FindBrush(string key) => BrushKeyConverter.Find(key);
}

/// <summary>
/// Transforme un nom de couleur du thème (« PlatformSteamBrush ») en vraie couleur, pour les carrés de la légende.
/// Une plateforme sans couleur à elle prend « PlatformOtherBrush ».
/// </summary>
public class BrushKeyConverter : IValueConverter
{
    public static Brush Find(string key)
    {
        return Application.Current.TryFindResource(key) as Brush
            ?? Application.Current.TryFindResource("PlatformOtherBrush") as Brush
            ?? Brushes.Gray;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string key ? Find(key) : Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Multiplie un nombre par le paramètre : la hauteur d'une colonne de l'histogramme = Ratio (0 à 1) × hauteur maximale.
/// </summary>
public class ScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double ratio = value is double number ? number : 0;
        double scale = double.Parse((string)parameter, CultureInfo.InvariantCulture);
        return ratio * scale;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

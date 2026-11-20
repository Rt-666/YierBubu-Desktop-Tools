using System.Windows;
using System.Windows.Media;
using YierBubu.Windows;

namespace SimulationCurtain;

internal sealed class NightScene(AnimatedClip bears, bool showBears) : FrameworkElement
{
    private static readonly Brush Sky = new LinearGradientBrush(
        Color.FromRgb(7, 10, 25), Color.FromRgb(36, 39, 68), new Point(0, 0), new Point(.25, 1));
    private static readonly Brush Moon = new SolidColorBrush(Color.FromRgb(229, 211, 174));
    private static readonly Brush FarHills = new SolidColorBrush(Color.FromRgb(28, 34, 60));
    private static readonly Brush NearHills = new SolidColorBrush(Color.FromRgb(15, 22, 39));
    private static readonly Brush Meadow = new SolidColorBrush(Color.FromRgb(10, 17, 30));
    private static readonly Brush Starlight = new SolidColorBrush(Color.FromRgb(178, 192, 226));
    private static readonly (double X, double Y, double Radius, double Opacity)[] Stars = MakeStars();
    private static readonly Geometry Far = Geometry.Parse("M0,760 C160,730 277,538 452,570 C641,604 735,714 853,680 C1015,637 1092,477 1252,501 C1458,532 1586,700 1920,641 L1920,1080 L0,1080 Z");
    private static readonly Geometry Near = Geometry.Parse("M0,899 C254,802 362,717 544,745 C718,773 754,850 918,866 C1228,893 1370,657 1580,695 C1737,724 1799,779 1920,766 L1920,1080 L0,1080 Z");
    private static readonly Geometry Ground = Geometry.Parse("M0,987 C370,871 669,1040 1091,939 C1378,870 1597,896 1920,919 L1920,1080 L0,1080 Z");
    private double elapsed;

    static NightScene()
    {
        RenderOptions.BitmapScalingModeProperty.OverrideMetadata(typeof(NightScene),
            new FrameworkPropertyMetadata(BitmapScalingMode.HighQuality));
    }

    private static (double, double, double, double)[] MakeStars()
    {
        var random = new Random(332);
        return Enumerable.Range(0, 82).Select(_ =>
            (random.NextDouble() * 1920, 45 + random.NextDouble() * 615,
                .62 + random.NextDouble() * 1.35, .25 + random.NextDouble() * .57)).ToArray();
    }

    public void SetTime(double seconds)
    {
        bool changed = !ReferenceEquals(bears.FrameAt(elapsed), bears.FrameAt(seconds));
        elapsed = seconds;
        if (showBears && changed) InvalidateVisual();
    }

    internal static Rect BearBounds(Size canvas, Rect content)
    {
        double scale = Math.Min(Math.Sqrt(canvas.Width * canvas.Height * .03 / (content.Width * content.Height)),
            Math.Min(canvas.Width * .8 / content.Width, canvas.Height * .8 / content.Height));
        return new Rect(canvas.Width * .91 - content.Width * scale, canvas.Height * .89 - content.Height * scale,
            content.Width * scale, content.Height * scale);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var canvas = RenderSize;
        if (canvas.Width <= 0 || canvas.Height <= 0) return;
        dc.DrawRectangle(Sky, null, new Rect(canvas));
        dc.PushTransform(new ScaleTransform(canvas.Width / 1920, canvas.Height / 1080));
        foreach (var (x, y, radius, opacity) in Stars)
        {
            dc.PushOpacity(opacity);
            dc.DrawEllipse(Starlight, null, new Point(x, y), radius, radius);
            dc.Pop();
        }
        dc.DrawGeometry(Moon, null, Geometry.Parse("M382,164 A66,66 0 1 0 451,253 A60,60 0 0 1 382,164 Z"));
        dc.DrawGeometry(FarHills, null, Far);
        dc.DrawGeometry(NearHills, null, Near);
        dc.DrawGeometry(Meadow, null, Ground);
        dc.Pop();

        if (!showBears) return;
        Rect content = bears.ContentBounds;
        Rect target = BearBounds(canvas, content);
        double scale = target.Width / content.Width;
        dc.DrawImage(bears.FrameAt(elapsed), new Rect(target.X - content.X * scale, target.Y - content.Y * scale,
            bears.PixelWidth * scale, bears.PixelHeight * scale));
    }
}

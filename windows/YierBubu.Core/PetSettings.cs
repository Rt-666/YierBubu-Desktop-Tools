using System.Text.Json;

namespace YierBubu.Core;

public sealed record PetSettings
{
    public double Scale { get; init; } = 1;
    public double Opacity { get; init; } = 1;
    public bool ClickThrough { get; init; }
    public bool AutoHideFullscreen { get; init; } = true;
    public string AnimationId { get; init; } = "keyboard";
    public bool AutoRotateAnimations { get; init; }
    // Screen coordinates are physical pixels, not WPF device-independent units.
    public double? X { get; init; }
    public double? Y { get; init; }

    public PetSettings Normalize() => this with
    {
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, .5, 2.5) : 1,
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, .3, 1) : 1,
        AnimationId = KeyboardStyles.Contains(KeyboardStyles.Migrate(AnimationId)) ? KeyboardStyles.Migrate(AnimationId) : AnimationId is "cuddle" or "kiss" or "dance" or "piggyback" or "sleep" or "scooter" ? AnimationId : "keyboard",
        X = X is { } x && double.IsFinite(x) ? x : null,
        Y = Y is { } y && double.IsFinite(y) ? y : null
    };
}

public sealed class SettingsStore(string path)
{
    public string Path { get; } = path;
    public string? LastError { get; private set; }
    public PetSettings Load()
    {
        try
        {
            LastError = null;
            return File.Exists(Path)
                ? (JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(Path)) ?? new()).Normalize()
                : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            LastError = e.Message;
            return new();
        }
    }
    public bool Save(PetSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            File.WriteAllText(Path + ".tmp", JsonSerializer.Serialize(settings.Normalize(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(Path + ".tmp", Path, true);
            LastError = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LastError = e.Message;
            return false;
        }
    }
}

public static class FullscreenPolicy
{
    public static bool CoversMonitor(Rect2 window, Rect2 monitor, double tolerance = 2) =>
        window.Width > 0 && window.Height > 0 && monitor.Width > 0 && monitor.Height > 0 &&
        window.X <= monitor.X + tolerance && window.Y <= monitor.Y + tolerance &&
        window.X + window.Width >= monitor.X + monitor.Width - tolerance &&
        window.Y + window.Height >= monitor.Y + monitor.Height - tolerance;

    public static bool ShouldHide(bool enabled, bool sameMonitor, bool excludedWindow, Rect2 window, Rect2 monitor) =>
        enabled && sameMonitor && !excludedWindow && CoversMonitor(window, monitor);
}

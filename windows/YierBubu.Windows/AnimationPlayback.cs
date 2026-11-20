namespace YierBubu.Windows;

/// <summary>Monotonic playback clock; hidden/suspended time does not advance a clip.</summary>
public sealed class AnimationPlayback(double now = 0)
{
    private double started = Finite(now), pausedAt, pausedDuration;
    public bool IsPaused { get; private set; }
    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
    public double Elapsed(double now) => Math.Max(0, (IsPaused ? pausedAt : Finite(now)) - started - pausedDuration);
    public void Restart(double now)
    {
        started = Finite(now); pausedAt = pausedDuration = 0; IsPaused = false;
    }
    public void Pause(double now)
    {
        if (IsPaused) return;
        pausedAt = Math.Max(started + pausedDuration, Finite(now)); IsPaused = true;
    }
    public void Resume(double now)
    {
        if (!IsPaused) return;
        pausedDuration += Math.Max(0, Finite(now) - pausedAt); IsPaused = false;
    }
    public static double SmoothStep(double elapsed, double duration = .24)
    {
        double t = duration <= 0 ? 1 : Math.Clamp(Finite(elapsed) / duration, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

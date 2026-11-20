namespace YierBubu.Core;

/// Plays while input is active, then finishes the current loop and rests at frame zero.
/// New input during a loop never rewinds it. Stores only a clock, not input content.
public sealed class InputAnimationClock
{
    private double started,stopAt;
    private bool running;
    public void Update(bool active,double now,double duration)
    {
        if(running&&now>=stopAt)running=false;
        if(active)
        {
            if(!running){started=now;running=true;}
            stopAt=double.PositiveInfinity;
        }
        else if(running&&double.IsPositiveInfinity(stopAt))
            stopAt=started+(Math.Floor(Math.Max(0,now-started)/duration)+1)*duration;
    }
    public double Elapsed(double now)=>IsAnimating(now)?Math.Max(0,now-started):0;
    public bool IsAnimating(double now)=>running&&now<stopAt;
    public void Reset(){running=false;started=stopAt=0;}
}

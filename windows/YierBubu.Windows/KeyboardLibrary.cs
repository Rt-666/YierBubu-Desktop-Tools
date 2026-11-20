using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
namespace YierBubu.Windows;

public sealed record KeyboardScene(KeyboardStyle Style,IReadOnlyList<AnimatedClip> Clips);

public sealed class KeyboardLibrary
{
    public AnimationLibrary Animations { get; }
    public KeyboardLibrary(string assetsRoot){Animations=new(assetsRoot,collection:"Keyboards");}
    public async Task<KeyboardScene> LoadAsync(string id,CancellationToken cancellationToken=default)
    {
        var style=KeyboardStyles.All.FirstOrDefault(s=>s.Id==id)??throw new AnimationLoadException("找不到这款键盘陪伴。");
        var clips=await Task.WhenAll(style.ClipIds.Select(clip=>Animations.LoadAsync(clip,cancellationToken)));
        return new(style,Array.AsReadOnly(clips));
    }
    public Task<IReadOnlyList<AnimationLoadFailure>> PreloadAsync(CancellationToken cancellationToken=default)=>Animations.PreloadAsync(cancellationToken);
}

public sealed class KeyboardScenePlayback(KeyboardScene scene)
{
    private readonly InputAnimationClock[] clocks=scene.Clips.Select(_=>new InputAnimationClock()).ToArray();
    private readonly IdleFace[] faces=scene.Clips.Select(clip=>new IdleFace(clip)).ToArray();
    private double expressionStarted=double.NegativeInfinity;
    private int mood;
    public void BeginExpression(double now){expressionStarted=now;mood++;}
    public bool IsAnimating(double now)=>clocks.Any(clock=>clock.IsAnimating(now))||now<expressionStarted+1.35+(clocks.Length-1)*.37;
    public void Reset(){foreach(var clock in clocks)clock.Reset();expressionStarted=double.NegativeInfinity;}
    public void Observe(InteractionSnapshot state,double now)
    {
        bool keys=state.Keys.Count>0,mouse=state.Buttons.Count>0||state.ScrollX!=0||state.ScrollY!=0;
        for(int i=0;i<clocks.Length;i++)clocks[i].Update(clocks.Length==1?keys||mouse:i==0?keys:mouse,now,scene.Clips[i].Duration);
    }
    public void Draw(DrawingContext dc,double now,Size size)
    {
        double gap=6,available=Math.Max(1,size.Width-12),width=(available-gap*(clocks.Length-1))/Math.Max(1,clocks.Length);
        for(int i=0;i<clocks.Length;i++)
        {
            var clip=scene.Clips[i];var bounds=clip.ContentBounds;
            var box=new Rect(6+i*(width+gap),4,width,Math.Max(1,size.Height-8));
            double fit=Math.Min(box.Width/bounds.Width,box.Height/bounds.Height);
            // A single, union-of-frames viewport avoids the character changing size or
            // jumping as its paws move. Only transparent source margins are excluded.
            double x=box.X+(box.Width-bounds.Width*fit)/2-bounds.X*fit;
            double y=box.Bottom-bounds.Bottom*fit;
            dc.PushClip(new RectangleGeometry(box));
            dc.DrawImage(clip.FrameAt(clocks[i].Elapsed(now)),new Rect(x,y,clip.PixelWidth*fit,clip.PixelHeight*fit));
            if(!clocks[i].IsAnimating(now)||clip.Id=="yier-desk")
            {
                dc.PushTransform(new TranslateTransform(x,y));dc.PushTransform(new ScaleTransform(fit,fit));
                faces[i].Draw(dc,now-expressionStarted-i*.37,mood+i);dc.Pop();dc.Pop();
            }
            dc.Pop();
        }
    }
}

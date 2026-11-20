using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace YierBubu.Windows;

public sealed class PetView:FrameworkElement
{
    private readonly SceneRenderer renderer;
    private KeyboardScene? keyboardScene;
    private KeyboardScenePlayback? keyboardPlayback;
    private PawMotion left=new(PawPose.Idle(true)),right=new(PawPose.Idle(false)),mouse=new(PawPose.IdleMouse);
    private InteractionSnapshot state=InteractionSnapshot.Empty;
    private bool suspended,blinking;
    private AnimatedClip? clip;
    private BitmapSource? outgoing,lastFrame;
    private double clipStarted,transitionStarted,pausedAt;
    private const double TransitionSeconds=.32;
    public string AnimationId=>keyboardScene?.Style.Id??clip?.Id??"keyboard";
    public PetView(Assets assets){renderer=new(assets);Focusable=false;RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);}
    private bool Transitioning(double now)=>outgoing!=null&&now-transitionStarted<TransitionSeconds;
    public bool IsAnimating(double now)=>!suspended&&(clip!=null||Transitioning(now)||(keyboardPlayback?.IsAnimating(now)??false)||left.IsAnimating(now)||right.IsAnimating(now)||mouse.IsAnimating(now));
    public void SelectAnimation(AnimatedClip? value,double now)
    {
        Select(value,null,now);
    }
    public void SelectKeyboardStyle(string id,double now)
    {
        if(id!="keyboard")throw new ArgumentException("Load the illustrated keyboard scene before selecting it",nameof(id));
        Select(null,null,now);
    }
    public void SelectKeyboardScene(KeyboardScene scene,double now)=>Select(null,scene,now);
    private void Select(AnimatedClip? value,KeyboardScene? scene,double now)
    {
        if(AnimationId==(scene?.Style.Id??value?.Id??"keyboard"))return;
        // Snapshot the current composite, including an interrupted dissolve, so rapid
        // choices never flash back to an earlier skin. Only one snapshot is retained.
        if(ActualWidth>0&&ActualHeight>0&&!suspended)
        {
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())DrawScene(dc,now);
            var frame=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(ActualWidth)),Math.Max(1,(int)Math.Ceiling(ActualHeight)),96,96,PixelFormats.Pbgra32);
            frame.Render(visual);frame.Freeze();outgoing=frame;
        }
        else outgoing=null;
        clip=value;keyboardScene=scene;keyboardPlayback=scene==null?null:new KeyboardScenePlayback(scene);
        if(!suspended)keyboardPlayback?.Observe(state,now);
        clipStarted=transitionStarted=now;lastFrame=null;
        if(suspended)pausedAt=now;
        InvalidateVisual();
    }
    public void SetInteraction(InteractionSnapshot value,double now)
    {
        state=value;
        if(clip!=null)return;
        if(keyboardPlayback!=null){keyboardPlayback.Observe(value,now);return;}
        left.Retarget(value.LeftKey is {} l?PawPose.Typing(l)!:PawPose.Idle(true),now);
        right.Retarget(value.RightKey is {} r?PawPose.Typing(r)!:PawPose.Idle(false),now);
        mouse.Retarget(value.Buttons.Count>0||value.ScrollX!=0||value.ScrollY!=0?PawPose.Mouse(value.Buttons,value.ScrollX!=0||value.ScrollY!=0):PawPose.IdleMouse,now);
        InvalidateVisual();
    }
    public void SetSuspended(bool value)
    {
        double now=AppController.Now;
        if(value&&!suspended)pausedAt=now;
        if(!value&&suspended){clipStarted+=now-pausedAt;transitionStarted+=now-pausedAt;}
        suspended=value;blinking=false;state=InteractionSnapshot.Empty;keyboardPlayback?.Reset();
        left=new(PawPose.Idle(true));right=new(PawPose.Idle(false));mouse=new(PawPose.IdleMouse);InvalidateVisual();
    }
    public void SetBlinking(bool value){blinking=value;if(value&&!suspended)keyboardPlayback?.BeginExpression(AppController.Now);if(clip==null)InvalidateVisual();}
    public void Refresh()
    {
        if(suspended)return;
        double now=AppController.Now;
        if(outgoing!=null&&!Transitioning(now))outgoing=null;
        if(clip!=null&&!Transitioning(now))
        {
            var frame=clip.FrameAt(now-clipStarted);
            if(ReferenceEquals(lastFrame,frame)&&outgoing==null)return;
            lastFrame=frame;outgoing=null;
        }
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc){base.OnRender(dc);DrawScene(dc,suspended?pausedAt:AppController.Now);}
    private void DrawScene(DrawingContext dc,double now)
    {
        double t=outgoing==null?1:Math.Clamp((now-transitionStarted)/TransitionSeconds,0,1);
        double eased=t*t*(3-2*t);
        if(outgoing!=null&&t<1){dc.PushOpacity(1-eased);dc.DrawImage(outgoing,new Rect(0,0,ActualWidth,ActualHeight));dc.Pop();}
        dc.PushOpacity(eased);
        if(keyboardPlayback!=null)keyboardPlayback.Draw(dc,now,new Size(ActualWidth,ActualHeight));
        else if(clip!=null)
        {
            var frame=clip.FrameAt(Math.Max(0,now-clipStarted));
            double fit=Math.Min((ActualWidth-8)/frame.PixelWidth,(ActualHeight-8)/frame.PixelHeight);
            double width=frame.PixelWidth*fit,height=frame.PixelHeight*fit;
            dc.DrawImage(frame,new Rect((ActualWidth-width)/2,(ActualHeight-height)/2,width,height));
        }
        else
        {
            var poses=new ScenePaws(state.LeftKey!=null||left.IsAnimating(now)?left.Pose(now):null,state.RightKey!=null||right.IsAnimating(now)?right.Pose(now):null,state.Buttons.Count>0||state.ScrollX!=0||state.ScrollY!=0||mouse.IsAnimating(now)?mouse.Pose(now):null);
            dc.PushTransform(new ScaleTransform(ActualWidth/1536,ActualHeight/1024));
            renderer.Draw(dc,state,poses,blinking);dc.Pop();
        }
        dc.Pop();
    }
}

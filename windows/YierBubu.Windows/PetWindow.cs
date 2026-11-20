using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
namespace YierBubu.Windows;

public sealed class PetWindow:Window
{
    public double Scale {get;private set;}=1;
    public bool ClickThrough {get;private set;}
    public event Action? PreferencesChanged;
    private bool ready;
    public PetWindow(PetView view,PetSettings? initial=null)
    {
        var settings=(initial??new()).Normalize();Scale=settings.Scale;ClickThrough=settings.ClickThrough;
        Title="一二布布 · 本地增强版";Width=300*Scale;Height=200*Scale;Opacity=settings.Opacity;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;Focusable=false;Content=view;
        Left=SystemParameters.WorkArea.Right-Width-24;Top=SystemParameters.WorkArea.Bottom-Height-24;
        SourceInitialized+=(_,_)=>
        {
            UpdateStyles();
            if(settings.X is {} x&&settings.Y is {} y)
                NativeMethods.SetWindowPos(Handle,0,(int)Math.Clamp(x,int.MinValue,int.MaxValue),(int)Math.Clamp(y,int.MinValue,int.MaxValue),0,0,0x0015);
            else ResetPosition();
            EnsureVisible();ready=true;
            Dispatcher.BeginInvoke(()=>{EnsureVisible();PreferencesChanged?.Invoke();});
        };
        MouseLeftButtonDown+=(_,e)=>{if(!ClickThrough&&e.ButtonState==MouseButtonState.Pressed){DragMove();EnsureVisible();PreferencesChanged?.Invoke();e.Handled=true;}};
        MouseWheel+=(_,e)=>{if(!ClickThrough&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){SetScale(Scale+(e.Delta>0?.05:-.05));e.Handled=true;}};
        MouseRightButtonUp+=(_,e)=>
        {
            if(ClickThrough||ContextMenu==null)return;
            ContextMenu.PlacementTarget=this;
            ContextMenu.Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            ContextMenu.IsOpen=true;e.Handled=true;
        };
        LocationChanged+=(_,_)=>{if(ready)PreferencesChanged?.Invoke();};
        DpiChanged+=(_,_)=>Dispatcher.BeginInvoke(()=>{EnsureVisible();PreferencesChanged?.Invoke();});
    }
    public nint Handle=>new WindowInteropHelper(this).Handle;
    private void UpdateStyles()
    {
        if(Handle==0)return;
        long style=NativeMethods.GetWindowLongPtr(Handle,-20).ToInt64()|0x08000000|0x80;
        style=ClickThrough?style|0x20:style&~0x20;
        NativeMethods.SetWindowLongPtr(Handle,-20,new nint(style));
        NativeMethods.SetWindowPos(Handle,0,0,0,0,0,0x0037);
    }
    public void SetClickThrough(bool enabled){ClickThrough=enabled;UpdateStyles();PreferencesChanged?.Invoke();}
    public void SetScale(double value)
    {
        if(!double.IsFinite(value))return;
        bool positioned=NativeMethods.GetWindowRect(Handle,out var rect);
        Scale=Math.Clamp(value,.5,2.5);Width=300*Scale;Height=200*Scale;
        if(positioned)NativeMethods.SetWindowPos(Handle,0,rect.Left,rect.Top,0,0,0x0015);
        EnsureVisible();PreferencesChanged?.Invoke();
    }
    public Point2? Position()=>NativeMethods.GetWindowRect(Handle,out var rect)?new(rect.Left,rect.Top):null;
    public void EnsureVisible()
    {
        if(Handle==0||!NativeMethods.GetWindowRect(Handle,out var rect))return;
        var info=new NativeMethods.MonitorInfo{Size=Marshal.SizeOf<NativeMethods.MonitorInfo>()};
        if(!NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(Handle,2),ref info))return;
        double factor=Math.Max(96,NativeMethods.GetDpiForWindow(Handle))/96.0;
        double fit=Math.Min(Scale,Math.Min((info.Work.Right-info.Work.Left)/(300*factor),(info.Work.Bottom-info.Work.Top)/(200*factor)));
        Width=300*fit;Height=200*fit;
        var p=WindowPlacement.Clamp(new(rect.Left,rect.Top),new(info.Work.Left,info.Work.Top,info.Work.Right-info.Work.Left,info.Work.Bottom-info.Work.Top),Width*factor,Height*factor);
        NativeMethods.SetWindowPos(Handle,0,(int)p.X,(int)p.Y,0,0,0x0015);
    }
    public void ResetPosition()
    {
        if(Handle==0)return;
        var info=new NativeMethods.MonitorInfo{Size=Marshal.SizeOf<NativeMethods.MonitorInfo>()};
        if(!NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(Handle,2),ref info))return;
        double factor=Math.Max(96,NativeMethods.GetDpiForWindow(Handle))/96.0;
        var p=WindowPlacement.BottomRight(new(info.Work.Left,info.Work.Top,info.Work.Right-info.Work.Left,info.Work.Bottom-info.Work.Top),Width*factor,Height*factor,24*factor);
        NativeMethods.SetWindowPos(Handle,0,(int)p.X,(int)p.Y,0,0,0x0015);PreferencesChanged?.Invoke();
    }
}

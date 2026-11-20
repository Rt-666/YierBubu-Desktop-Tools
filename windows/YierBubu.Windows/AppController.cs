using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Forms=System.Windows.Forms;
namespace YierBubu.Windows;

public sealed class AppController:IDisposable
{
    private readonly Application app;
    private readonly PetView view;
    private readonly PetWindow window;
    private readonly InputMonitor input=new();
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon icon;
    private readonly DispatcherTimer frames=new(){Interval=TimeSpan.FromSeconds(1.0/60)},heartbeat=new(){Interval=TimeSpan.FromSeconds(.5)},blinkEnd=new(){Interval=TimeSpan.FromSeconds(0.12)},saveDelay=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private readonly InteractionState state=new(Now);
    private StatusWindow? status;
    private bool hidden,fullscreenHidden,sessionActive=true,disposed,lastResting,applying;
    private bool IsHidden=>hidden||fullscreenHidden;
    private readonly SettingsStore settingsStore;
    private PetSettings settings;
    private SettingsWindow? settingsWindow;
    private AnimationPickerWindow? animationPicker;
    private readonly AnimationLibrary animations;
    private readonly KeyboardLibrary keyboards;
    private readonly CancellationTokenSource lifetime=new();
    private int selectionVersion;
    private double nextRotation;
    private readonly Forms.ToolStripMenuItem fullscreen=new("全屏时自动隐藏"){CheckOnClick=true},size=new("小熊大小");
    public PetSettings CurrentSettings=>settings;
    public bool IsDemoRunning=>demo.Count>0;
    private double animationUntil,lastBlink,blinkInterval=3.4;
    private readonly Queue<(double Time,InputEvent Input)> demo=[];
    private readonly Forms.ToolStripMenuItem visibility=new("隐藏小熊"),clickThrough=new("鼠标穿透"){CheckOnClick=true};
    public static double Now=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
    public AppController(Application app,Assets assets,SettingsStore? store=null)
    {
        settingsStore=store??new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"YierBubu","settings.json"));
        settings=settingsStore.Load();this.app=app;view=new(assets);window=new(view,settings);animations=new AnimationLibrary(assets.Root);keyboards=new(assets.Root);
        icon=new System.Drawing.Icon(Path.Combine(assets.Root,"YierBubu.ico"));
        tray=new Forms.NotifyIcon{Icon=icon,Text="一二布布 · 本地增强版",Visible=false};
        var menu=new Forms.ContextMenuStrip();menu.Items.Add(new Forms.ToolStripMenuItem("一二布布 · 陪你敲键盘"){Enabled=false});
        visibility.Click+=(_,_)=>ToggleVisibility();menu.Items.Add(visibility);
        clickThrough.Checked=settings.ClickThrough;clickThrough.Click+=(_,_)=>window.SetClickThrough(clickThrough.Checked);menu.Items.Add(clickThrough);
        foreach(int percent in new[]{50,75,100,125,150,200,250}){var item=new Forms.ToolStripMenuItem(percent+"%"){Checked=Math.Abs(settings.Scale-percent/100.0)<.001,Tag=percent};item.Click+=(_,_)=>window.SetScale(percent/100.0);size.DropDownItems.Add(item);}menu.Items.Add(size);
        Add(menu,"设置与自由缩放…",ShowSettings);
        Add(menu,"动画小屋 · 选一款陪伴…",ShowAnimationPicker);
        fullscreen.Checked=settings.AutoHideFullscreen;fullscreen.Click+=(_,_)=>ApplyPreferences(settings with{AutoHideFullscreen=fullscreen.Checked});menu.Items.Add(fullscreen);
        Add(menu,"恢复到屏幕右下角",()=>window.ResetPosition());Add(menu,"动作演示",StartDemo);menu.Items.Add(new Forms.ToolStripSeparator());
        Add(menu,"连接状态与真实计数…",ShowStatus);Add(menu,"重新连接",()=>{input.Start();UpdateStatus();});menu.Items.Add(new Forms.ToolStripSeparator());Add(menu,"退出一二布布",()=>app.Shutdown());
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>ShowStatus();
        window.ContextMenu=CreatePetMenu();
        input.Input+=OnInput;input.Reset+=ResetActions;frames.Tick+=(_,_)=>Tick();heartbeat.Tick+=(_,_)=>IdleTick();blinkEnd.Tick+=(_,_)=>{blinkEnd.Stop();view.SetBlinking(false);};
        window.PreferencesChanged+=ScheduleSave;saveDelay.Tick+=(_,_)=>SaveSettings();
    }
    private static void Add(Forms.ContextMenuStrip menu,string title,Action action){var item=new Forms.ToolStripMenuItem(title);item.Click+=(_,_)=>action();menu.Items.Add(item);}
    private System.Windows.Controls.ContextMenu CreatePetMenu()
    {
        var menu=new System.Windows.Controls.ContextMenu{FontFamily=new System.Windows.Media.FontFamily("Microsoft YaHei UI"),FontSize=14};
        System.Windows.Controls.MenuItem Item(string title,Action action)
        {
            var item=new System.Windows.Controls.MenuItem{Header=title};
            item.Click+=(_,_)=>{menu.IsOpen=false;action();};return item;
        }
        menu.Items.Add(Item("动画小屋 · 选一款陪伴…",ShowAnimationPicker));
        var quickAnimations=new System.Windows.Controls.MenuItem{Header="快速切换动画"};
        foreach(var style in KeyboardStyles.All){var choice=Item("键盘 · "+style.Name,()=>_ = SelectAnimationAsync(style.Id));choice.Tag=style.Id;choice.IsCheckable=true;quickAnimations.Items.Add(choice);}
        foreach(var entry in animations.Entries){var choice=Item(entry.Name,()=>_ = SelectAnimationAsync(entry.Id));choice.Tag=entry.Id;choice.IsCheckable=true;quickAnimations.Items.Add(choice);}
        menu.Items.Add(quickAnimations);
        menu.Items.Add(Item("设置与自由缩放…",ShowSettings));
        var sizes=new System.Windows.Controls.MenuItem{Header="小熊大小"};
        foreach(int percent in new[]{50,75,100,125,150,200,250})
        {
            var item=Item(percent+"%",()=>window.SetScale(percent/100.0));item.Tag=percent;item.IsCheckable=true;sizes.Items.Add(item);
        }
        menu.Items.Add(sizes);
        var passthrough=Item("鼠标穿透",()=>window.SetClickThrough(!window.ClickThrough));passthrough.IsCheckable=true;menu.Items.Add(passthrough);
        var autoHide=Item("全屏时自动隐藏",()=>ApplyPreferences(settings with{AutoHideFullscreen=!settings.AutoHideFullscreen}));autoHide.IsCheckable=true;menu.Items.Add(autoHide);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(Item("恢复到屏幕右下角",window.ResetPosition));
        menu.Items.Add(Item("动作演示",StartDemo));
        menu.Items.Add(Item("隐藏小熊",ToggleVisibility));
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(Item("退出一二布布",()=>app.Shutdown()));
        menu.Opened+=(_,_)=>
        {
            passthrough.IsChecked=window.ClickThrough;autoHide.IsChecked=settings.AutoHideFullscreen;
            foreach(System.Windows.Controls.MenuItem item in quickAnimations.Items)item.IsChecked=(string)item.Tag==settings.AnimationId;
            foreach(System.Windows.Controls.MenuItem item in sizes.Items)item.IsChecked=item.Tag is int percent&&Math.Abs(window.Scale-percent/100.0)<.001;
        };
        return menu;
    }
    public void Start()
    {
        window.Show();tray.Visible=true;input.Start();lastBlink=Now;heartbeat.Start();
        SystemEvents.SessionSwitch+=SessionChanged;SystemEvents.PowerModeChanged+=PowerChanged;SystemEvents.DisplaySettingsChanged+=DisplayChanged;
        if(!input.Listening)ShowStatus();
        nextRotation=Now+30;_ = InitializeAnimationsAsync();
    }
    private async Task InitializeAnimationsAsync()
    {
        await SelectAnimationAsync(settings.AnimationId,false);
        try{await Task.WhenAll(animations.PreloadAsync(lifetime.Token),keyboards.PreloadAsync(lifetime.Token));}catch(OperationCanceledException){}catch(Exception){ /* Individual unavailable clips remain retryable in the picker. */ }
    }
    public async Task SelectAnimationAsync(string id,bool manual=true)
    {
        if(disposed)return;
        id=KeyboardStyles.Migrate(id);
        int version=++selectionVersion;
        try
        {
            bool keyboard=KeyboardStyles.Contains(id);
            var scene=keyboard&&id!="keyboard"?await keyboards.LoadAsync(id,lifetime.Token):null;
            var selected=keyboard?null:await animations.LoadAsync(id,lifetime.Token);
            if(disposed||version!=selectionVersion)return;
            if(scene!=null)view.SelectKeyboardScene(scene,Now);else if(keyboard)view.SelectKeyboardStyle(id,Now);else view.SelectAnimation(selected,Now);
            settings=settings with{AnimationId=id,AutoRotateAnimations=keyboard&&manual?false:settings.AutoRotateAnimations};
            nextRotation=Now+30;animationPicker?.Sync(id,settings.AutoRotateAnimations);ScheduleSave();Animate();
        }
        catch(OperationCanceledException){}
        catch(Exception e)
        {
            if(disposed||version!=selectionVersion)return;
            settings=settings with{AnimationId=view.AnimationId};ScheduleSave();
            animationPicker?.Sync(view.AnimationId,settings.AutoRotateAnimations);
            tray.ShowBalloonTip(4000,"这款动画暂时无法打开",e.Message,Forms.ToolTipIcon.Info);
        }
    }
    public void SetAutoRotateAnimations(bool enabled)
    {
        settings=settings with{AutoRotateAnimations=enabled};nextRotation=Now+30;
        animationPicker?.Sync(settings.AnimationId,enabled);ScheduleSave();
    }
    public void ShowAnimationPicker()
    {
        if(animationPicker==null)
        {
            animationPicker=new AnimationPickerWindow(animations,settings.AnimationId,settings.AutoRotateAnimations,id=>_ = SelectAnimationAsync(id),SetAutoRotateAnimations,keyboards);
            animationPicker.Closed+=(_,_)=>animationPicker=null;
        }
        animationPicker.Sync(settings.AnimationId,settings.AutoRotateAnimations);animationPicker.Show();animationPicker.Activate();
    }
    private void OnInput(InputEvent value)
    {
        if(value.Repeat)return;
        if(demo.Count>0){demo.Clear();state.Reset(Now);}
        state.Handle(value,Now);Animate();
    }
    private void Animate(){animationUntil=Now+0.3;if(!IsHidden&&sessionActive){view.SetInteraction(state.Snapshot(Now),Now);frames.Start();}}
    private void Tick()
    {
        double now=Now;
        while(demo.TryPeek(out var next)&&now>=next.Time){demo.Dequeue();state.Handle(next.Input,now);animationUntil=now+0.3;}
        if(!IsHidden&&sessionActive){view.SetInteraction(state.Snapshot(now),now);view.Refresh();}
        if(demo.Count==0&&now>=animationUntil&&!view.IsAnimating(now))frames.Stop();
    }
    private void IdleTick()
    {
        UpdateFullscreen();UpdateStatus();if(IsHidden||!sessionActive)return;
        double now=Now;var snapshot=state.Snapshot(now);
        if(settings.AutoRotateAnimations&&animationPicker==null&&now>=nextRotation&&animations.Entries.Count>0)
        {
            nextRotation=now+30;int index=animations.Entries.ToList().FindIndex(e=>e.Id==settings.AnimationId);
            _ = SelectAnimationAsync(animations.Entries[(index+1)%animations.Entries.Count].Id);
        }
        if(snapshot.Resting!=lastResting){lastResting=snapshot.Resting;view.SetInteraction(snapshot,now);}
        if(now-lastBlink>=blinkInterval&&(settings.AnimationId=="keyboard-yier-desk"||snapshot.Keys.Count==0&&snapshot.Buttons.Count==0&&snapshot.ScrollX==0&&snapshot.ScrollY==0)){lastBlink=now;blinkInterval=3.2+Random.Shared.NextDouble()*3;view.SetBlinking(true);blinkEnd.Start();frames.Start();}
    }
    private void ResetActions(){demo.Clear();state.Reset(Now);frames.Stop();blinkEnd.Stop();view.SetBlinking(false);view.SetSuspended(IsHidden||!sessionActive);if(!IsHidden&&sessionActive)Animate();}
    public void ToggleVisibility()
    {
        hidden=!hidden;visibility.Text=hidden?"显示小熊":"隐藏小熊";ApplyVisibility();
    }
    private void ApplyVisibility()
    {
        nextRotation=Now+30;
        ResetActions();
        if(IsHidden)window.Hide();else{window.Show();Animate();}
    }
    private void UpdateFullscreen()
    {
        bool next=sessionActive&&settings.AutoHideFullscreen&&FullscreenDetector.IsCoveringPet(window.Handle);
        if(next==fullscreenHidden)return;
        fullscreenHidden=next;ApplyVisibility();
    }
    private void ScheduleSave()
    {
        if(disposed||applying)return;
        var position=window.Position();
        settings=settings with{Scale=window.Scale,Opacity=window.Opacity,ClickThrough=window.ClickThrough,X=position?.X??settings.X,Y=position?.Y??settings.Y};
        clickThrough.Checked=settings.ClickThrough;fullscreen.Checked=settings.AutoHideFullscreen;
        foreach(Forms.ToolStripMenuItem item in size.DropDownItems)item.Checked=item.Tag is int percent&&Math.Abs(settings.Scale-percent/100.0)<.001;
        settingsWindow?.Sync(settings);saveDelay.Stop();saveDelay.Start();
    }
    private void SaveSettings()
    {
        saveDelay.Stop();bool saved=settingsStore.Save(settings);settingsWindow?.SetSaveStatus(settingsStore.LastError);
        if(!saved&&!disposed)tray.ShowBalloonTip(5000,"设置未能保存",settingsStore.LastError??"请检查目录是否可写。",Forms.ToolTipIcon.Warning);
    }
    public void ApplyPreferences(PetSettings value)
    {
        applying=true;settings=value.Normalize();
        window.SetScale(settings.Scale);window.Opacity=settings.Opacity;window.SetClickThrough(settings.ClickThrough);
        applying=false;ScheduleSave();UpdateFullscreen();
    }
    public void ShowSettings()
    {
        if(settingsWindow==null){settingsWindow=new SettingsWindow(settings,ApplyPreferences,window.ResetPosition);settingsWindow.Closed+=(_,_)=>settingsWindow=null;}
        settingsWindow.SetSaveStatus(settingsStore.LastError);settingsWindow.Show();settingsWindow.Activate();
    }
    public void StartDemo()
    {
        if(!KeyboardStyles.Contains(settings.AnimationId))_ = SelectAnimationAsync("keyboard");
        if(hidden)ToggleVisibility();ResetActions();double start=Now;
        (double Delay,InputEvent Event)[] sequence=[
            (0.2,new(InputKind.KeyDown,Key:'a')),(0.45,new(InputKind.KeyUp,Key:'a')),
            (0.6,new(InputKind.KeyDown,Key:'s')),(0.85,new(InputKind.KeyUp,Key:'s')),
            (1,new(InputKind.KeyDown,Key:'d')),(1.25,new(InputKind.KeyUp,Key:'d')),
            (1.5,new(InputKind.KeyDown,Key:'c')),(1.9,new(InputKind.KeyUp,Key:'c')),
            (2.2,new(InputKind.MouseDown,Button:MouseButton.Left)),(2.5,new(InputKind.MouseUp,Button:MouseButton.Left)),
            (2.8,new(InputKind.MouseDown,Button:MouseButton.Right)),(3.1,new(InputKind.MouseUp,Button:MouseButton.Right)),
            (3.4,new(InputKind.MouseDown,Button:MouseButton.Middle)),(3.7,new(InputKind.MouseUp,Button:MouseButton.Middle)),
            (4,new(InputKind.Scroll,Y:4)),(4.4,new(InputKind.Scroll,Y:-4)),(4.8,new(InputKind.Scroll,X:4)),(5.2,new(InputKind.Scroll,X:-4))];
        foreach(var action in sequence)demo.Enqueue((start+action.Delay,action.Event));frames.Start();
    }
    private void ShowStatus(){if(status==null){status=new StatusWindow(()=>{input.Start();UpdateStatus();});status.Closed+=(_,_)=>status=null;}UpdateStatus();status.Show();status.Activate();}
    private void UpdateStatus(){status?.Update(input.Status,input.Activity);}
    private void Dispatch(Action action){if(!disposed)app.Dispatcher.BeginInvoke(()=>{if(!disposed)action();});}
    private void SessionChanged(object sender,SessionSwitchEventArgs e)
    {
        if(e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect)Dispatch(Suspend);
        else if(e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect)Dispatch(Resume);
    }
    private void PowerChanged(object sender,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Suspend)Dispatch(Suspend);else if(e.Mode==PowerModes.Resume)Dispatch(Resume);}
    private void DisplayChanged(object? sender,EventArgs e)=>Dispatch(()=>{window.EnsureVisible();ScheduleSave();UpdateFullscreen();});
    private void Suspend(){sessionActive=false;input.Stop();ResetActions();}
    private void Resume(){sessionActive=true;input.Start();ResetActions();}
    public void Dispose()
    {
        if(disposed)return;ScheduleSave();SaveSettings();disposed=true;lifetime.Cancel();
        SystemEvents.SessionSwitch-=SessionChanged;SystemEvents.PowerModeChanged-=PowerChanged;SystemEvents.DisplaySettingsChanged-=DisplayChanged;
        frames.Stop();heartbeat.Stop();blinkEnd.Stop();demo.Clear();input.Input-=OnInput;input.Reset-=ResetActions;input.Dispose();
        window.PreferencesChanged-=ScheduleSave;window.ContextMenu.IsOpen=false;animationPicker?.Close();settingsWindow?.Close();status?.Close();window.Close();tray.Visible=false;tray.ContextMenuStrip?.Dispose();tray.Dispose();icon.Dispose();
    }
}

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Diagnostics;
using YierBubu.Core;
using YierBubu.Windows;

internal sealed class NotVerifiedException(string message):Exception(message);

internal static class Program
{
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window,int command);
    [DllImport("user32.dll",SetLastError=true)] private static extern uint GetRegisteredRawInputDevices([Out] NativeMethods.RawInputDevice[]? devices,ref uint count,uint size);
    [STAThread]
    static int Main(string[] args)
    {
        if(args.FirstOrDefault()=="--fullscreen-fixture")
        {
            var fixtureApp=new Application();
            var fixture=new Window{Title="YierBubu fullscreen verification",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brushes.DarkSlateGray,Topmost=true,
                Content=new System.Windows.Controls.TextBlock{Text="一二布布：正在验证全屏自动隐藏，稍后自动关闭",Foreground=Brushes.White,FontSize=24,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
            fixture.SourceInitialized+=(_,_)=>NativeMethods.SetWindowPos(new WindowInteropHelper(fixture).Handle,0,int.Parse(args[1]),int.Parse(args[2]),int.Parse(args[3]),int.Parse(args[4]),0x0014);
            var timeout=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(20)};timeout.Tick+=(_,_)=>fixture.Close();timeout.Start();
            fixtureApp.Run(fixture);return 0;
        }
        string output=args.Length>0?Path.GetFullPath(args[0]):Path.GetFullPath("smoke-output");Directory.CreateDirectory(output);
        var results=new Dictionary<string,object>();int failures=0,skipped=0;
        void Test(string name,Action body){try{body();results[name]="pass";Console.WriteLine("PASS "+name);}catch(NotVerifiedException e){results[name]="not-verified: "+e.Message;skipped++;Console.WriteLine("NOT VERIFIED "+name+": "+e.Message);}catch(Exception e){results[name]=e.ToString();failures++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var assets=new Assets();
        var renderer=new SceneRenderer(assets);
        BitmapSource Frame(InteractionSnapshot s,string file){var visual=new DrawingVisual();using(var dc=visual.RenderOpen())renderer.Draw(dc,s,ScenePaws.From(s),false);var bitmap=new RenderTargetBitmap(1536,1024,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var f=File.Create(Path.Combine(output,file+".png"));encoder.Save(f);return bitmap;}
        int WhiteNear(BitmapSource b,Point2 point){int count=0;byte[] pixels=new byte[1536*1024*4];b.CopyPixels(pixels,1536*4,0);for(int y=(int)point.Y-18;y<point.Y+18;y++)for(int x=(int)point.X-18;x<point.X+18;x++){int i=(y*1536+x)*4;if(pixels[i+3]>200&&pixels[i]>235&&pixels[i+1]>235&&pixels[i+2]>235)count++;}return count;}
        Test("approved-assets-load",()=>Check(assets.Scene.PixelWidth==1536&&assets.Paws.PixelHeight==1024,"invalid dimensions"));
        Test("missing-assets-fail-clearly",()=>{try{_ = new Assets(Path.Combine(output,"missing"));throw new Exception("missing art accepted");}catch(FileNotFoundException){}});
        Frame(InteractionSnapshot.Empty,"idle");
        foreach(var key in SceneLayout.Keys){var k=key;Test("actual-paw-pixels-"+SpecialKeys.Name(k.Label)+"-"+((int)k.Label).ToString("x4"),()=>{var state=new InteractionState(0);state.Handle(new(InputKind.KeyDown,Key:k.Label),0);var b=Frame(state.Snapshot(0.01),"key-"+((int)k.Label).ToString("x4"));Check(WhiteNear(b,k.Contact)>12,"paw not at key contact");});}
        var mouseFrames=new Dictionary<MouseButton,BitmapSource>();
        foreach(var button in Enum.GetValues<MouseButton>())Test("mouse-frame-"+button,()=>{var s=new InteractionState(0);s.Handle(new(InputKind.MouseDown,Button:button),0);mouseFrames[button]=Frame(s.Snapshot(0),"mouse-"+button);});
        byte Blue(BitmapSource image,int x,int y){var pixel=new byte[4];image.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);return pixel[0];}
        Test("left-mouse-color-in-bubus-left-region",()=>Check(Blue(mouseFrames[MouseButton.Left],335,636)+20<Blue(mouseFrames[MouseButton.Right],335,636),"left button region reversed"));
        Test("right-mouse-color-in-bubus-right-region",()=>Check(Blue(mouseFrames[MouseButton.Right],245,623)+20<Blue(mouseFrames[MouseButton.Left],245,623),"right button region reversed"));
        Test("simultaneous-keys-and-mouse-frame",()=>{var s=new InteractionState(0);s.Handle(new(InputKind.KeyDown,Key:'a'),0);s.Handle(new(InputKind.KeyDown,Key:'j'),0);s.Handle(new(InputKind.MouseDown),0);Frame(s.Snapshot(0),"combined");});
        Test("ctrl-space-feedback-frame",()=>{var s=new InteractionState(0);s.Handle(new(InputKind.KeyDown,Key:SpecialKeys.LeftCtrl),0);s.Handle(new(InputKind.KeyDown,Key:SpecialKeys.Space),0);Frame(s.Snapshot(0),"ctrl-space");});
        var window=new PetWindow(new PetView(assets));window.Show();
        try {
            Test("transparent-floating-nonactivating-window",()=>{Check(window.AllowsTransparency&&window.Topmost&&!window.ShowInTaskbar&&!window.ShowActivated,"wrong window flags");Check((NativeMethods.GetWindowLongPtr(new WindowInteropHelper(window).Handle,-20).ToInt64()&0x08000000)!=0,"NOACTIVATE missing");});
            Test("scale-and-reset-position",()=>{window.SetScale(0.75);Check(window.Width==225&&window.Height==150,"75% size");window.SetScale(1.25);Check(window.Width==375&&window.Height==250,"125% size");window.SetScale(1);window.ResetPosition();Check(double.IsFinite(window.Left)&&double.IsFinite(window.Top),"position invalid");});
            Test("free-scale-preserves-position",()=>{
                NativeMethods.SetWindowPos(window.Handle,0,100,100,0,0,0x0015);var before=window.Position();
                window.SetScale(1.35);Check(Math.Abs(window.Width-405)<.01&&Math.Abs(window.Height-270)<.01,"custom scale not applied");
                Check(window.Position()==before,"scaling moved the pet unexpectedly");window.SetScale(1);
            });
            Test("style-changes-preserve-foreground-window",()=>{
                var before=GetForegroundWindow();if(before==0||before==window.Handle)throw new NotVerifiedException("no independent foreground window available");
                window.SetClickThrough(true);Check(GetForegroundWindow()==before,"pet stole focus enabling click-through");
                window.SetClickThrough(false);Check(GetForegroundWindow()==before,"pet stole focus disabling click-through");
            });
            Test("click-through-native-style",()=>{window.SetClickThrough(true);Check((NativeMethods.GetWindowLongPtr(new WindowInteropHelper(window).Handle,-20).ToInt64()&0x20)!=0,"transparent style missing");window.SetClickThrough(false);Check((NativeMethods.GetWindowLongPtr(new WindowInteropHelper(window).Handle,-20).ToInt64()&0x20)==0,"transparent style persisted");});
            Test("hide-show-keeps-window-usable",()=>{window.Hide();Check(!window.IsVisible,"did not hide");window.Show();Check(window.IsVisible,"did not show");});
            Test("raw-input-registration-and-stop",()=>{using var input=new InputMonitor();input.Start();Check(input.Listening,input.Status);input.Stop();Check(!input.Listening,"listener remained active");});
        } finally {window.Close();}
        Test("offscreen-saved-position-restores-inside-work-area",()=>{
            var pet=new PetWindow(new PetView(assets),new PetSettings{X=90000,Y=-90000,Scale=1.35});pet.Show();
            try{
                var info=new NativeMethods.MonitorInfo{Size=Marshal.SizeOf<NativeMethods.MonitorInfo>()};
                Check(NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(pet.Handle,2),ref info),"no monitor");
                Check(NativeMethods.GetWindowRect(pet.Handle,out var rect),"no window rectangle");
                Check(rect.Left>=info.Work.Left&&rect.Top>=info.Work.Top&&rect.Right<=info.Work.Right+1&&rect.Bottom<=info.Work.Bottom+1,"restored pet offscreen");
            }finally{pet.Close();}
        });
        void Pump(double seconds){var frame=new System.Windows.Threading.DispatcherFrame();var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(seconds)};timer.Tick+=(_,_)=>{frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);timer.Stop();}
        uint Registered(){uint count=0;uint result=GetRegisteredRawInputDevices(null,ref count,(uint)Marshal.SizeOf<NativeMethods.RawInputDevice>());if(result==uint.MaxValue)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());return count;}
        byte[] ViewPixels(Visual visual){var b=new RenderTargetBitmap(300,200,96,96,PixelFormats.Pbgra32);b.Render(visual);var bytes=new byte[300*200*4];b.CopyPixels(bytes,1200,0);return bytes;}
        Test("actual-controller-demo-hide-listener-and-disposal",()=>{
            var store=new SettingsStore(Path.Combine(output,"controller-settings.json"));store.Save(new PetSettings{AutoHideFullscreen=false});
            using(var controller=new AppController(app,assets,store)){
                controller.Start();Pump(0.1);var pet=app.Windows.OfType<PetWindow>().Single();var before=ViewPixels((Visual)pet.Content);
                controller.StartDemo();var demoWait=Stopwatch.StartNew();while(before.SequenceEqual(ViewPixels((Visual)pet.Content))&&demoWait.Elapsed.TotalSeconds<2)Pump(.025);
                if(!controller.IsDemoRunning)throw new NotVerifiedException("live input cancelled the demo during verification");
                Check(!before.SequenceEqual(ViewPixels((Visual)pet.Content)),"demo did not animate through dispatcher; visible="+pet.IsVisible+", size="+pet.ActualWidth+"x"+pet.ActualHeight);
                controller.ToggleVisibility();Pump(0.05);Check(!pet.IsVisible&&Registered()==2,"hiding stopped input or retained pet");
                controller.ToggleVisibility();Pump(0.05);Check(pet.IsVisible,"show failed");
            }
            Pump(0.05);Check(app.Windows.Count==0&&Registered()==0,"window or Raw Input registration survived disposal");
        });
        Test("pet-right-click-menu-opens-settings-and-controls-size",()=>{
            var store=new SettingsStore(Path.Combine(output,"context-menu-settings.json"));store.Save(new PetSettings{AutoHideFullscreen=false});
            using var controller=new AppController(app,assets,store);controller.Start();Pump(.1);
            var pet=app.Windows.OfType<PetWindow>().Single();
            void RightClick(){pet.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Right){RoutedEvent=UIElement.MouseRightButtonUpEvent,Source=pet});Pump(.1);}
            RightClick();Check(pet.ContextMenu.IsOpen,"right-click did not open menu");
            var menu=pet.ContextMenu;menu.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(menu);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(Path.Combine(output,"pet-menu.png")))encoder.Save(f);
            var items=menu.Items.OfType<System.Windows.Controls.MenuItem>().ToArray();
            items.Single(i=>i.Header.ToString()=="设置与自由缩放…").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));Pump(.1);
            Check(app.Windows.OfType<SettingsWindow>().Count()==1&&!menu.IsOpen,"menu did not open settings");app.Windows.OfType<SettingsWindow>().Single().Close();
            RightClick();var sizes=items.Single(i=>i.Header.ToString()=="小熊大小");
            sizes.Items.OfType<System.Windows.Controls.MenuItem>().Single(i=>i.Tag is int percent&&percent==125).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Check(pet.Scale==1.25,"menu size selection failed");
            RightClick();Check(sizes.Items.OfType<System.Windows.Controls.MenuItem>().Single(i=>i.Tag is int percent&&percent==125).IsChecked,"menu scale checkmark stale");
            items.Single(i=>i.Header.ToString()=="全屏时自动隐藏").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));Check(controller.CurrentSettings.AutoHideFullscreen,"menu fullscreen toggle failed");
            RightClick();items.Single(i=>i.Header.ToString()=="鼠标穿透").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));Check(pet.ClickThrough,"menu passthrough toggle failed");
            RightClick();Check(!menu.IsOpen,"click-through window intercepted right click");
            pet.SetClickThrough(false);RightClick();Check(menu.IsOpen,"menu did not recover after click-through disabled");menu.IsOpen=false;
        });
        Test("settings-ui-and-restart-persistence",()=>{
            var store=new SettingsStore(Path.Combine(output,"restart-settings.json"));
            store.Save(new PetSettings{X=100,Y=100});
            using(var controller=new AppController(app,assets,store)){
                controller.Start();Pump(.1);controller.ApplyPreferences(controller.CurrentSettings with{Scale=1.35,Opacity=.75,ClickThrough=true,AutoHideFullscreen=false});
                controller.ShowSettings();Pump(.5);
                var settingsWindow=app.Windows.OfType<SettingsWindow>().Single();
                var bitmap=new RenderTargetBitmap((int)settingsWindow.ActualWidth,(int)settingsWindow.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(settingsWindow);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(Path.Combine(output,"settings.png")))encoder.Save(f);
            }
            var saved=store.Load();Check(saved.Scale==1.35&&saved.ClickThrough&&!saved.AutoHideFullscreen&&saved.Opacity==.75,"settings did not save");
            using(var controller=new AppController(app,assets,store)){
                controller.Start();Pump(.1);var pet=app.Windows.OfType<PetWindow>().Single();
                Check(pet.Scale==1.35&&pet.ClickThrough&&pet.Opacity==.75,"restart lost preferences");
                Check(pet.Position()==new Point2(saved.X!.Value,saved.Y!.Value),"restart lost position");
            }
        });
        Test("real-fullscreen-hide-restore-and-manual-hide",()=>{
            var store=new SettingsStore(Path.Combine(output,"fullscreen-settings.json"));store.Save(new PetSettings());
            using var controller=new AppController(app,assets,store);controller.Start();Pump(.1);
            var pet=app.Windows.OfType<PetWindow>().Single();var info=new NativeMethods.MonitorInfo{Size=Marshal.SizeOf<NativeMethods.MonitorInfo>()};
            Check(NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(pet.Handle,2),ref info),"no monitor");
            var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
            foreach(var argument in new[]{"--fullscreen-fixture",info.Monitor.Left.ToString(),info.Monitor.Top.ToString(),(info.Monitor.Right-info.Monitor.Left).ToString(),(info.Monitor.Bottom-info.Monitor.Top).ToString()})start.ArgumentList.Add(argument);
            using var child=Process.Start(start)!;
            try{
                Pump(1.5);child.Refresh();NativeMethods.GetWindowThreadProcessId(GetForegroundWindow(),out uint foregroundProcess);
                if(foregroundProcess!=child.Id)throw new NotVerifiedException("fullscreen fixture could not acquire foreground");
                Check(!pet.IsVisible,"pet remained visible over fullscreen window");
                controller.ApplyPreferences(controller.CurrentSettings with{AutoHideFullscreen=false});Pump(.6);Check(pet.IsVisible,"disabling auto-hide did not show pet");
                controller.ApplyPreferences(controller.CurrentSettings with{AutoHideFullscreen=true});Pump(.6);Check(!pet.IsVisible,"reenabling auto-hide did not hide pet");
                ShowWindow(child.MainWindowHandle,6);Pump(.8);Check(pet.IsVisible,"pet did not recover after fullscreen minimized");
                controller.ToggleVisibility();ShowWindow(child.MainWindowHandle,9);Pump(.8);ShowWindow(child.MainWindowHandle,6);Pump(.8);
                Check(!pet.IsVisible,"fullscreen transition undid manual hide");
                controller.ToggleVisibility();Check(pet.IsVisible,"manual show failed");
            }finally{if(!child.HasExited){child.CloseMainWindow();Pump(.2);if(!child.HasExited)child.Kill();}}
        });
        void WaitFor(System.Threading.Tasks.Task task){var watch=Stopwatch.StartNew();while(!task.IsCompleted&&watch.Elapsed.TotalSeconds<15)Pump(.025);Check(task.IsCompleted,"async load timed out");task.GetAwaiter().GetResult();}
        Test("six-animations-transparent-timed-and-cached",()=>{
            var library=new AnimationLibrary(assets.Root);Check(library.Entries.Count==6,"expected six animations");WaitFor(library.PreloadAsync());
            Check(library.CachedCount==6&&library.CachedBytes<64L*1024*1024,"cache bound exceeded");
            foreach(var entry in library.Entries){var clip=library.Get(entry.Id);Check(clip.Frames.Count>1&&clip.Duration>0,"still image instead of animation");
                Check(ReferenceEquals(clip.FrameAt(0),clip.FrameAt(clip.Duration)),"loop boundary mismatch");
                var first=clip.Frames[0];var bytes=new byte[first.PixelWidth*first.PixelHeight*4];first.CopyPixels(bytes,first.PixelWidth*4,0);
                Check(Enumerable.Range(0,bytes.Length/4).Any(i=>bytes[i*4+3]==0),"opaque backdrop");Check(clip.Frames.All(f=>f.IsFrozen),"frame not thread safe");
                Check(ReferenceEquals(library.LoadAsync(entry.Id).GetAwaiter().GetResult(),clip),"cache not reused");}
        });
        Test("animation-switch-pause-picker-and-restart",()=>{
            var store=new SettingsStore(Path.Combine(output,"animation-settings.json"));store.Save(new PetSettings{AutoHideFullscreen=false});
            using(var controller=new AppController(app,assets,store)){
                controller.Start();Pump(.1);var pet=app.Windows.OfType<PetWindow>().Single();var view=(PetView)pet.Content;var position=pet.Position();
                WaitFor(controller.SelectAnimationAsync("cuddle"));Pump(.4);Check(view.AnimationId=="cuddle"&&pet.Position()==position,"selection moved pet or failed");
                var one=ViewPixels(view);Pump(.2);Check(!one.SequenceEqual(ViewPixels(view)),"animation did not advance");
                var a=controller.SelectAnimationAsync("kiss");var b=controller.SelectAnimationAsync("scooter");WaitFor(System.Threading.Tasks.Task.WhenAll(a,b));Pump(.4);
                Check(view.AnimationId=="scooter"&&controller.CurrentSettings.AnimationId=="scooter","outdated load replaced latest choice");
                controller.ToggleVisibility();Pump(.05);var paused=ViewPixels(view);Pump(.3);Check(paused.SequenceEqual(ViewPixels(view)),"hidden animation clock continued");
                controller.ToggleVisibility();Pump(.15);Check(view.IsAnimating(AppController.Now),"animation failed to resume");
                foreach(string id in new[]{"sleep","piggyback","dance","kiss","cuddle"}){WaitFor(controller.SelectAnimationAsync(id));Check(view.AnimationId==id,"animation failed to load: "+id);Pump(.035);}
                Pump(.4);Check(view.AnimationId=="cuddle"&&pet.Position()==position,"rapid switching lost final choice or position");
                controller.SetAutoRotateAnimations(true);Pump(30.7);
                Check(view.AnimationId=="kiss","30-second rotation failed");
                WaitFor(controller.SelectAnimationAsync("cuddle"));Pump(.4);controller.ShowAnimationPicker();Pump(.8);
                var picker=app.Windows.OfType<AnimationPickerWindow>().Single();
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(picker.ActualWidth),(int)Math.Ceiling(picker.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(picker);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"animation-picker.png")))encoder.Save(file);
                picker.Close();Check(app.Windows.OfType<AnimationPickerWindow>().Count()==0,"picker remained open");
            }
            Check(store.Load().AnimationId=="cuddle"&&store.Load().AutoRotateAnimations,"animation preference not saved");
            using(var controller=new AppController(app,assets,store)){
                controller.Start();Pump(.7);var view=(PetView)app.Windows.OfType<PetWindow>().Single().Content;Check(view.AnimationId=="cuddle","restart did not restore animation");
                WaitFor(controller.SelectAnimationAsync("keyboard"));Pump(.4);Check(view.AnimationId=="keyboard"&&!controller.CurrentSettings.AutoRotateAnimations,"keyboard return failed");
            }
        });
        Test("illustrated-keyboards-load-respond-and-settle",()=>{
            var library=new KeyboardLibrary(assets.Root);WaitFor(library.PreloadAsync());
            Check(library.Animations.CachedCount==5&&library.Animations.CachedBytes<64L*1024*1024,"keyboard cache or assets incomplete");
            byte[] Draw(KeyboardScenePlayback playback,double now,string name)
            {
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen())playback.Draw(dc,now,new Size(600,400));
                var bitmap=new RenderTargetBitmap(600,400,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);
                var pixels=new byte[600*400*4];bitmap.CopyPixels(pixels,2400,0);return pixels;
            }
            var state=new InteractionState(0);state.Handle(new(InputKind.KeyDown,Key:'a'),0);var keys=state.Snapshot(0);
            var graphics=new HashSet<string>();
            foreach(var style in KeyboardStyles.All.Where(s=>s.ClipIds.Count>0))
            {
                var load=library.LoadAsync(style.Id);WaitFor(load);var scene=load.Result;
                Check(scene.Clips.All(c=>c.Frames.Count>=6&&c.ContentBounds.Width>0&&c.ContentBounds.Height>0),"invalid keyboard frames");
                var playback=new KeyboardScenePlayback(scene);var idle=Draw(playback,0,style.Id+"-idle");
                graphics.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(idle)));
                playback.Observe(keys,1);double sample=1+scene.Clips[0].Duration*.4;
                var typing=Draw(playback,sample,style.Id+"-typing");Check(!idle.SequenceEqual(typing),"typing did not trigger new illustration: "+style.Id);
                playback.Observe(InteractionSnapshot.Empty,sample);Draw(playback,sample+.04,style.Id+"-released");
                Check(!playback.IsAnimating(4)&&idle.SequenceEqual(Draw(playback,4,style.Id+"-settled")),"typing did not naturally return to idle");
                playback.Reset();Check(!playback.IsAnimating(4),"reset retained activity");
                if(style.Id=="keyboard-yier-desk")
                {
                    playback.Observe(keys,4);var working=Draw(playback,4.2,"desk-working-open");playback.BeginExpression(4);
                    Check(!working.SequenceEqual(Draw(playback,4.2,"desk-working-blink")),"desk eyes stayed fixed during typing");playback.Reset();
                }
                playback.BeginExpression(5);
                Check(playback.IsAnimating(5.2),"idle expression did not request frames");
                var blink=Draw(playback,5.2,style.Id+"-blink");var glance=Draw(playback,5.85,style.Id+"-glance");
                Check(!idle.SequenceEqual(blink)&&!blink.SequenceEqual(glance),"eyes stayed static: "+style.Id);
                Check(idle.SequenceEqual(Draw(playback,7,style.Id+"-eyes-restored"))&&!playback.IsAnimating(7),"eyes did not settle back to original pixels");
                playback.BeginExpression(8);playback.Reset();Check(idle.SequenceEqual(Draw(playback,8.2,style.Id+"-eyes-reset")),"hidden expression was not reset");
            }
            Check(graphics.Count==6,"keyboard choices reuse the same scene");
            foreach(string id in new[]{"yier-laptop","bubu-laptop"})
            {
                var clip=library.Animations.Get(id);int softAlpha=0,greenPixels=0;
                foreach(var frame in clip.Frames){var pixels=new byte[frame.PixelWidth*frame.PixelHeight*4];frame.CopyPixels(pixels,frame.PixelWidth*4,0);
                    for(int i=0;i<pixels.Length;i+=4){if(pixels[i+3]>0&&pixels[i+3]<255)softAlpha++;if(pixels[i+3]>50&&pixels[i+1]>pixels[i+2]+40&&pixels[i+1]>pixels[i]+40)greenPixels++;}}
                Check(softAlpha>0&&greenPixels==0,"laptop atlas lost soft transparency or retained green edges: "+id);
            }
            var desk=library.Animations.Get("yier-desk");Check(desk.ContentBounds.Top>desk.PixelHeight*.2&&desk.ContentBounds.Height<desk.PixelHeight*.8,"transparent top padding was not excluded");
            var pairTask=library.LoadAsync("keyboard-duo");WaitFor(pairTask);var pair=new KeyboardScenePlayback(pairTask.Result);
            var resting=Draw(pair,0,"duo-idle");pair.Observe(keys,1);var keyFrame=Draw(pair,1.3,"duo-key");
            bool SameHalf(byte[] a,byte[] b,bool right)=>Enumerable.Range(0,400).All(y=>a.AsSpan(y*2400+(right?1200:0),1200).SequenceEqual(b.AsSpan(y*2400+(right?1200:0),1200)));
            Check(!SameHalf(resting,keyFrame,false)&&SameHalf(resting,keyFrame,true),"typing activated wrong bear");
            pair.Reset();var mouseState=new InteractionState(0);mouseState.Handle(new(InputKind.MouseDown,Button:MouseButton.Left),0);pair.Observe(mouseState.Snapshot(0),1);
            var mouseFrame=Draw(pair,1.3,"duo-mouse");Check(SameHalf(resting,mouseFrame,false)&&!SameHalf(resting,mouseFrame,true),"mouse activated wrong bear");
        });
        Test("keyboard-styles-switch-demo-preview-and-persist",()=>{
            var store=new SettingsStore(Path.Combine(output,"keyboard-styles-settings.json"));store.Save(new PetSettings{AutoHideFullscreen=false,AutoRotateAnimations=true});
            using(var controller=new AppController(app,assets,store))
            {
                controller.Start();Pump(.15);var pet=app.Windows.OfType<PetWindow>().Single();var view=(PetView)pet.Content;var position=pet.Position();
                foreach(var style in KeyboardStyles.All)
                {
                    WaitFor(controller.SelectAnimationAsync(style.Id));Pump(.36);
                    Check(view.AnimationId==style.Id&&controller.CurrentSettings.AnimationId==style.Id&&!controller.CurrentSettings.AutoRotateAnimations,"keyboard selection or rotation policy incorrect");
                    var before=ViewPixels(view);controller.StartDemo();Pump(.43);
                    if(!controller.IsDemoRunning)throw new NotVerifiedException("live input cancelled the demo during verification");
                    Check(view.AnimationId==style.Id&&!before.SequenceEqual(ViewPixels(view)),"demo lost style or failed to animate");
                    controller.ToggleVisibility();Pump(.05);controller.ToggleVisibility();Pump(.05);
                    Check(pet.Position()==position,"keyboard style moved pet");
                }
                var pending=controller.SelectAnimationAsync("sleep");WaitFor(controller.SelectAnimationAsync("keyboard-duo"));WaitFor(pending);Pump(.4);
                Check(view.AnimationId=="keyboard-duo","GIF load replaced latest keyboard choice");
                controller.ShowAnimationPicker();Pump(1.0);var picker=app.Windows.OfType<AnimationPickerWindow>().Single();
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(picker.ActualWidth),(int)Math.Ceiling(picker.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(picker);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"keyboard-picker.png")))encoder.Save(file);
                IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
                {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T match)yield return match;foreach(var nested in Descendants<T>(child))yield return nested;}}
                foreach(string group in new[]{"一二","布布","双熊"})
                {
                    var filter=Descendants<System.Windows.Controls.Button>(picker).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="筛选键盘陪伴："+group);
                    filter.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Pump(.25);
                    var expected=KeyboardStyles.All.Where(s=>s.Group==group).Select(s=>s.Id).Order().ToArray();
                    var visible=Descendants<PetView>(picker).Where(v=>v.IsVisible).Select(v=>v.AnimationId).Order().ToArray();
                    Check(expected.SequenceEqual(visible),"character filter displayed wrong bears: "+group);
                    var filtered=new RenderTargetBitmap((int)Math.Ceiling(picker.ActualWidth),(int)Math.Ceiling(picker.ActualHeight),96,96,PixelFormats.Pbgra32);filtered.Render(picker);
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(filtered));using(var file=File.Create(Path.Combine(output,"keyboard-picker-"+group+".png")))png.Save(file);
                }
                picker.WindowState=WindowState.Minimized;Pump(.1);
                Check(Descendants<PetView>(picker).All(v=>!v.IsAnimating(AppController.Now)),"minimized picker retained active keyboard clocks");
                picker.WindowState=WindowState.Normal;Pump(.4);
                picker.Close();
            }
            Check(store.Load().AnimationId=="keyboard-duo","keyboard style not saved");
            using(var controller=new AppController(app,assets,store))
            {
                controller.Start();Pump(.4);var view=(PetView)app.Windows.OfType<PetWindow>().Single().Content;
                Check(view.AnimationId=="keyboard-duo","keyboard style not restored after restart");
            }
        });
        results["environment"]=new{os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),sourceCommit=Environment.GetEnvironmentVariable("SOURCE_COMMIT")??"local",physicalInputVerified=false,windows10ClientVerified=false,windows11ClientVerified=false,scope="RenderTargetBitmap + real HWND/styles + Raw Input API registration; no synthetic system input"};
        results["summary"]=new{failures,skipped,passed=results.Count-1-failures-skipped,total=results.Count-1};
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown();return failures==0?0:1;
    }
}

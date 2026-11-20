using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using YierBubu.Windows;
using Forms = System.Windows.Forms;

namespace SimulationCurtain;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool verify = args.Length == 2 && args[0] is "--verify" or "--verify-desktop";
        try
        {
            const string fileName = "desk-break-hd.gif";
            const string hash = "04c94374d87407b0b563252cd6455e94297ec885c680782a2095d8b5f96251b1";
            var clip = AnimatedClip.Decode(new AnimationEntry("desk-break", "工位摸鱼", "", fileName, Sha256: hash),
                Path.Combine(AppContext.BaseDirectory, "Assets", fileName), AnimationLibrary.MaximumClipBytes);
            if (verify)
            {
                VerifyScene(clip, args[1]);
                if (args[0] == "--verify-desktop")
                {
                    var testApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    using var testCurtain = new CurtainController(testApp, clip);
                    testCurtain.VerifyDesktop();
                }
                return 0;
            }

            using var instance = new Mutex(true, @"Local\SimulationCurtain", out bool first);
            if (!first)
            {
                MessageBox.Show("仿真遮屏已在运行。按 Ctrl + Alt + B 切换，或查看托盘图标。", "仿真遮屏");
                return 0;
            }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var curtain = new CurtainController(app, clip);
            app.Run();
            return 0;
        }
        catch (Exception e)
        {
            if (!verify) MessageBox.Show(e.Message, "仿真遮屏");
            return 1;
        }
    }

    private static void VerifyScene(AnimatedClip clip, string output)
    {
        var scene = new NightScene(clip, true);
        foreach (var size in new[] { new Size(1920, 1080), new Size(1080, 1920), new Size(3440, 1440) })
        {
            var bear = NightScene.BearBounds(size, clip.ContentBounds);
            if (Math.Abs(bear.Width * bear.Height / (size.Width * size.Height) - .03) > .001 ||
                !new Rect(size).Contains(bear))
                throw new InvalidOperationException("角色尺寸校验失败。");
        }
        var canvas = new Size(1280, 720);
        scene.Measure(canvas);
        scene.Arrange(new Rect(canvas));
        scene.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(scene);
        var pixels = new byte[1280 * 720 * 4];
        bitmap.CopyPixels(pixels, 1280 * 4, 0);
        for (int index = 3; index < pixels.Length; index += 4)
            if (pixels[index] != 255) throw new InvalidOperationException("遮罩有透明区域。");
        if (clip.Frames.Count < 2 || clip.PixelWidth != 498 || clip.PixelHeight != 498)
            throw new InvalidOperationException("高清小熊动画缺失。");
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(output);
        png.Save(file);
    }
}

internal sealed class CurtainController : IDisposable
{
    private const uint Continuous = 0x80000000;
    private const uint KeepSystemAndDisplayAwake = 3;
    private const string Hotkey = "Ctrl + Alt + B";
    private const int ToggleHotkeyId = 1, EscapeHotkeyId = 2;
    private const int HotkeyMessage = 0x0312;
    private const uint NoRepeat = 0x4000, EscapeKey = 0x1B;
    private readonly Application app;
    private readonly AnimatedClip clip;
    private readonly HwndSource listener = new(new HwndSourceParameters("SimulationCurtainHotkey")
    {
        ParentWindow = new IntPtr(-3)
    });
    private readonly Forms.NotifyIcon tray;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Forms.ToolStripMenuItem modeMenu = new("遮住哪些屏幕");
    private readonly Forms.ToolStripMenuItem toggleMenu = new("开启遮屏");
    private readonly System.Drawing.Icon icon;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Stopwatch clock = new();
    private readonly List<(Window Window, NightScene Scene)> covers = [];
    private readonly bool hotkeyRegistered;
    private string? selectedDisplay;
    private bool active, disposed, escapeRegistered;

    public CurtainController(Application app, AnimatedClip clip)
    {
        this.app = app;
        this.clip = clip;
        listener.AddHook(OnMessage);
        hotkeyRegistered = RegisterHotKey(listener.Handle, ToggleHotkeyId, NoRepeat | 3, 0x42);
        icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "YierBubu.ico"));
        tray = new Forms.NotifyIcon { Icon = icon, Text = "一二布布 · 仿真遮屏", ContextMenuStrip = menu, Visible = true };
        toggleMenu.Click += (_, _) => Toggle();
        var exit = new Forms.ToolStripMenuItem("退出仿真遮屏");
        exit.Click += (_, _) => app.Shutdown();
        menu.Items.AddRange([toggleMenu, modeMenu, exit]);
        menu.Opening += (_, _) => FillDisplayMenu();
        tray.DoubleClick += (_, _) => Toggle();
        timer.Tick += (_, _) =>
        {
            foreach (var cover in covers) cover.Scene.SetTime(clock.Elapsed.TotalSeconds);
        };
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        if (!hotkeyRegistered) tray.ShowBalloonTip(4000, "快捷键已被占用", "请从托盘开启遮屏，用 Esc 恢复。", Forms.ToolTipIcon.Info);
    }

    private void FillDisplayMenu()
    {
        foreach (var previous in modeMenu.DropDownItems.Cast<Forms.ToolStripItem>().ToArray()) previous.Dispose();
        modeMenu.DropDownItems.Clear();
        AddChoice("所有屏幕", null);
        foreach (var screen in Forms.Screen.AllScreens.OrderByDescending(screen => screen.Primary))
            AddChoice($"{(screen.Primary ? "主屏" : "副屏")} · {screen.Bounds.Width} × {screen.Bounds.Height} · {screen.DeviceName}", screen.DeviceName);
        void AddChoice(string label, string? id)
        {
            var item = new Forms.ToolStripMenuItem(label) { Checked = selectedDisplay == id };
            item.Click += (_, _) => selectedDisplay = id;
            modeMenu.DropDownItems.Add(item);
        }
    }

    private void Toggle()
    {
        if (active) { Restore(); return; }
        try
        {
            OpenCurtain();
        }
        catch (Exception e)
        {
            Restore();
            MessageBox.Show(e.Message, "未能开启遮屏");
        }
    }

    private void OpenCurtain()
    {
        if (SetThreadExecutionState(Continuous | KeepSystemAndDisplayAwake) == 0)
            throw new InvalidOperationException("Windows 没有接受保持唤醒的请求。");
        active = true;
        escapeRegistered = RegisterHotKey(listener.Handle, EscapeHotkeyId, NoRepeat, EscapeKey);
        BuildCovers();
        toggleMenu.Text = "恢复桌面";
        modeMenu.Enabled = false;
        clock.Restart();
        timer.Start();
    }

    private void BuildCovers()
    {
        var screens = Forms.Screen.AllScreens;
        var targets = screens.Where(screen => selectedDisplay == null || screen.DeviceName == selectedDisplay).ToArray();
        if (targets.Length == 0) targets = screens;
        if (targets.Length == 0) throw new InvalidOperationException("找不到可遮住的屏幕。");
        var focusScreen = targets.FirstOrDefault(screen => screen.Primary) ?? targets[0];
        var previous = covers.ToArray();
        covers.Clear();
        try
        {
            foreach (var screen in targets)
            {
                var scene = new NightScene(clip, screen == focusScreen);
                var cover = new Window
                {
                    Title = "仿真遮屏",
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Topmost = true,
                    Background = Brushes.Black,
                    Content = scene
                };
                var bounds = screen.Bounds;
                cover.SourceInitialized += (_, _) =>
                {
                    if (!SetWindowPos(new WindowInteropHelper(cover).Handle, new IntPtr(-1),
                        bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10))
                        throw new InvalidOperationException("无法覆盖屏幕。");
                };
                cover.PreviewKeyDown += (_, e) =>
                {
                    e.Handled = true;
                    if (e.Key == Key.Escape) Restore();
                };
                var context = new System.Windows.Controls.ContextMenu();
                var restoreItem = new System.Windows.Controls.MenuItem
                {
                    Header = "恢复桌面（Esc）"
                };
                restoreItem.Click += (_, _) => Restore();
                context.Items.Add(restoreItem);
                cover.ContextMenu = context;
                covers.Add((cover, scene));
                cover.Show();
                SetWindowPos(new WindowInteropHelper(cover).Handle, new IntPtr(-1),
                    bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10);
                if (screen == focusScreen && (selectedDisplay == null || !escapeRegistered))
                    cover.Activate();
            }
        }
        finally
        {
            foreach (var cover in previous) cover.Window.Close();
        }
    }

    private void Restore()
    {
        active = false;
        if (escapeRegistered)
        {
            UnregisterHotKey(listener.Handle, EscapeHotkeyId);
            escapeRegistered = false;
        }
        timer.Stop();
        clock.Reset();
        foreach (var cover in covers) cover.Window.Close();
        covers.Clear();
        SetThreadExecutionState(Continuous);
        toggleMenu.Text = "开启遮屏";
        modeMenu.Enabled = true;
    }

    internal void VerifyDesktop()
    {
        try
        {
            OpenCurtain();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var visible = covers.Select(cover =>
            {
                if (!cover.Window.IsVisible || !GetWindowRect(new WindowInteropHelper(cover.Window).Handle, out var rect))
                    throw new InvalidOperationException("未能读取遮屏窗口。");
                return System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            }).ToHashSet();
            if (!visible.SetEquals(Forms.Screen.AllScreens.Select(screen => screen.Bounds)))
                throw new InvalidOperationException("遮罩未覆盖每块屏幕的完整范围。");
            if (!escapeRegistered) throw new InvalidOperationException("Esc 未能注册。");
            SendMessage(listener.Handle, HotkeyMessage, new IntPtr(EscapeHotkeyId), IntPtr.Zero);
            if (active || escapeRegistered || app.Windows.Count != 0)
                throw new InvalidOperationException("按 Esc 后未完全恢复桌面。");
        }
        finally
        {
            Restore();
        }
        if (app.Windows.Count != 0) throw new InvalidOperationException("恢复后仍有遮屏窗口。");
    }

    private void DisplaySettingsChanged(object? sender, EventArgs args) => app.Dispatcher.BeginInvoke(() =>
    {
        if (!active || disposed) return;
        try { BuildCovers(); }
        catch (Exception e)
        {
            Restore();
            MessageBox.Show(e.Message, "遮屏已恢复");
        }
    });

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != HotkeyMessage) return IntPtr.Zero;
        if (wParam == new IntPtr(ToggleHotkeyId))
        {
            handled = true;
            Toggle();
        }
        else if (wParam == new IntPtr(EscapeHotkeyId) && active)
        {
            handled = true;
            Restore();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        Restore();
        if (hotkeyRegistered) UnregisterHotKey(listener.Handle, ToggleHotkeyId);
        listener.Dispose();
        tray.Dispose();
        menu.Dispose();
        icon.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }
}

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace YierBubu.Windows;

/// <summary>A small, live preview library. Playback shares the application's decoded clips.</summary>
public sealed class AnimationPickerWindow : Window
{
    private static readonly Brush Ink = ColorBrush(85, 57, 47);
    private static readonly Brush Muted = ColorBrush(147, 117, 101);
    private static readonly Brush Coral = ColorBrush(191, 96, 76);
    private static readonly Brush BorderColor = ColorBrush(235, 219, 204);
    private static readonly Brush SelectedFill = ColorBrush(255, 239, 228);
    private static readonly Brush[] PreviewColors =
    [
        ColorBrush(255, 245, 230), ColorBrush(252, 238, 233), ColorBrush(240, 244, 231),
        ColorBrush(239, 241, 251), ColorBrush(250, 239, 245), ColorBrush(238, 246, 246)
    ];
    private readonly AnimationLibrary library;
    private readonly KeyboardLibrary? keyboards;
    private readonly Action<string> select;
    private readonly Action<bool> setAutoRotate;
    private readonly List<Card> cards = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly DispatcherTimer previews = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
    private readonly CheckBox rotate = new() { Content = "每 30 秒换一个小日常", VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock currentLabel = new() { Foreground = Coral, FontSize = 12 };
    private readonly List<KeyboardCard> keyboardCards = [];
    private readonly Dictionary<string, Border> keyboardFilters = [];
    private readonly Border dailyTab = new(), keyboardTab = new();
    private readonly ScrollViewer dailyPage = new(), keyboardPage = new();
    private readonly TextBlock pageHint = new() { Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 3) };
    private readonly InteractionState keyboardDemo = new(AppController.Now);
    private bool keyboardPageVisible, keyboardPreviewsActive,previewBlink;
    private string keyboardGroup = "全部";
    private double demoStarted;
    private int nextDemoEvent;
    private InteractionSnapshot? lastDemoSnapshot;
    private static readonly (double Time, InputEvent Event)[] KeyboardDemoEvents =
    [
        (.15,new(InputKind.KeyDown,Key:'a')), (.29,new(InputKind.KeyUp,Key:'a')),
        (.42,new(InputKind.KeyDown,Key:'s')), (.56,new(InputKind.KeyUp,Key:'s')),
        (.69,new(InputKind.KeyDown,Key:'d')), (.83,new(InputKind.KeyUp,Key:'d')),
        (1.02,new(InputKind.KeyDown,Key:'j')), (1.16,new(InputKind.KeyUp,Key:'j')),
        (1.29,new(InputKind.KeyDown,Key:'k')), (1.43,new(InputKind.KeyUp,Key:'k')),
        (1.56,new(InputKind.KeyDown,Key:'l')), (1.70,new(InputKind.KeyUp,Key:'l')),
        (2.05,new(InputKind.KeyDown,Key:SpecialKeys.LeftCtrl)),
        (2.16,new(InputKind.KeyDown,Key:'a')),
        (2.45,new(InputKind.KeyUp,Key:'a')), (2.55,new(InputKind.KeyUp,Key:SpecialKeys.LeftCtrl)),
        (2.90,new(InputKind.KeyDown,Key:'f')), (2.90,new(InputKind.KeyDown,Key:'j')),
        (3.12,new(InputKind.KeyUp,Key:'f')), (3.12,new(InputKind.KeyUp,Key:'j')),
        (3.45,new(InputKind.MouseDown,Button:YierBubu.Core.MouseButton.Left)),
        (3.66,new(InputKind.MouseUp,Button:YierBubu.Core.MouseButton.Left)),
        (3.90,new(InputKind.MouseDown,Button:YierBubu.Core.MouseButton.Right)),
        (4.11,new(InputKind.MouseUp,Button:YierBubu.Core.MouseButton.Right)),
        (4.40,new(InputKind.Scroll,Y:1)), (4.65,new(InputKind.Scroll,Y:-1))
    ];
    private string currentId;
    private int pickerSelectionVersion;
    private bool syncing, loaded, closed;

    private sealed class Card
    {
        public required AnimationEntry Entry { get; init; }
        public required Button Button { get; init; }
        public required Border Shell { get; init; }
        public required Image Preview { get; init; }
        public required TextBlock Placeholder { get; init; }
        public required TextBlock Badge { get; init; }
        public AnimatedClip? Clip { get; set; }
        public bool Loading { get; set; }
    }

    private sealed class KeyboardCard
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Group { get; init; }
        public required Button Button { get; init; }
        public required Border Shell { get; init; }
        public required PetView Preview { get; init; }
        public required TextBlock Placeholder { get; init; }
        public required TextBlock Badge { get; init; }
        public bool Ready { get; set; }
        public bool Loading { get; set; }
        public bool WasAnimating { get; set; }
        public bool Suspended { get; set; } = true;
    }

    public AnimationPickerWindow(AnimationLibrary library, string currentId, bool autoRotate, Action<string> select, Action<bool> setAutoRotate, KeyboardLibrary? keyboards = null)
    {
        this.library = library;
        this.keyboards = keyboards;
        this.currentId = currentId;
        this.select = select;
        this.setAutoRotate = setAutoRotate;
        Title = "一二布布 · 今天怎么陪你";
        Width = 760; Height = 880; MinWidth = 630; MinHeight = 480;
        MaxHeight = Math.Max(480, SystemParameters.WorkArea.Height - 36);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = ColorBrush(255, 250, 244);
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13; Foreground = Ink;
        var root = new Grid { Margin = new Thickness(26, 22, 26, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock { Text = "今天，让小熊怎么陪你？", FontSize = 27, FontWeight = FontWeights.SemiBold, Foreground = Ink });
        header.Children.Add(new TextBlock { Text = "看看甜甜的小日常，也让每次敲键都有小惊喜。", Foreground = Muted, Margin = new Thickness(0, 7, 0, 9) });
        header.Children.Add(currentLabel);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 17, 0, 0) };
        tabs.Children.Add(MakeTab(dailyTab, "小日常 · 6 款", () => ShowPage(false)));
        tabs.Children.Add(MakeTab(keyboardTab, $"键盘陪伴 · {KeyboardStyles.All.Count} 款", () => ShowPage(true)));
        header.Children.Add(tabs);
        root.Children.Add(header);

        var pages = new Grid(); Grid.SetRow(pages, 1); root.Children.Add(pages);
        foreach (var page in new[] { dailyPage, keyboardPage })
        {
            page.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; page.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            page.Padding = new Thickness(0, 0, 4, 0); pages.Children.Add(page);
        }
        var body = new StackPanel(); dailyPage.Content = body;
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-5, 0, -5, 0) };
        body.Children.Add(grid);
        int index = 0;
        foreach (var entry in library.Entries)
        {
            var card = MakeCard(entry, index++);
            cards.Add(card); grid.Children.Add(card.Button);
        }
        if (cards.Count == 0)
            body.Children.Add(new TextBlock { Text = "还没有找到动画。请完整保留程序文件夹后重新打开。", TextWrapping = TextWrapping.Wrap, Foreground = Muted, Margin = new Thickness(12, 30, 12, 30) });

        var keyboardBody = new StackPanel(); keyboardPage.Content = keyboardBody;
        keyboardBody.Children.Add(new TextBlock { Text = "一二、布布，或让两只小熊一起陪你", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 4) });
        keyboardBody.Children.Add(new TextBlock { Text = "每张卡片都是一种小模样，看看它们跟着敲键动起来。", Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 13) };
        foreach (string group in new[] { "全部", "一二", "布布", "双熊" })
        {
            var shell = new Border(); keyboardFilters.Add(group, shell);
            var filter = MakeTab(shell, group, () => ShowKeyboardGroup(group));
            shell.Padding = new Thickness(15, 6, 15, 6);
            filter.AutomationPropertiesName("筛选键盘陪伴：" + group); filters.Children.Add(filter);
        }
        keyboardBody.Children.Add(filters);
        var keyboardGrid = new UniformGrid { Columns = 3, Margin = new Thickness(-5, 0, -5, 0) }; keyboardBody.Children.Add(keyboardGrid);
        try
        {
            var previewAssets = new Assets();
            this.keyboards ??= new KeyboardLibrary(previewAssets.Root);
            int keyboardIndex = 0;
            foreach (var style in KeyboardStyles.All)
                keyboardGrid.Children.Add(MakeKeyboardCard(style.Id, style.Name, style.Description, style.Group, previewAssets, keyboardIndex++));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            keyboardBody.Children.Add(new TextBlock { Text = "键盘预览暂时无法打开，请完整保留程序文件夹后重试。", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 16) });
        }

        var footer = new StackPanel { Margin = new Thickness(0, 15, 0, 0) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var footerRow = new DockPanel();
        var done = BareButton(); done.Content = new Border { Background = Coral, CornerRadius = new CornerRadius(9), Padding = new Thickness(22, 9, 22, 9), Child = new TextBlock { Text = "就这样陪我", Foreground = Brushes.White } };
        done.IsCancel = true; done.Click += (_, _) => Close(); DockPanel.SetDock(done, Dock.Right); footerRow.Children.Add(done);
        rotate.VerticalAlignment = VerticalAlignment.Center; rotate.Margin = new Thickness(0, 0, 14, 0); rotate.Foreground = Ink;
        rotate.Click += (_, _) => { if (!syncing) { this.setAutoRotate(rotate.IsChecked == true); UpdateCurrentLabel(); } };
        footerRow.Children.Add(rotate); footer.Children.Add(footerRow);
        footer.Children.Add(pageHint);
        footer.Children.Add(new TextBlock { Text = "角色原作：黄小B · 一二与布布", Foreground = Muted, FontSize = 11 });
        previews.Tick += (_, _) => RefreshPreviews();
        Loaded += async (_, _) =>
        {
            if (loaded) return;
            loaded = true; UpdatePreviewLifecycle(restartKeyboardDemo: true);
            await Task.WhenAll(cards.Select(LoadCardAsync).Concat(keyboardCards.Select(LoadKeyboardCardAsync)));
        };
        StateChanged += (_, _) => UpdatePreviewLifecycle();
        IsVisibleChanged += (_, _) => UpdatePreviewLifecycle();
        Closed += (_, _) => { closed = true; UpdatePreviewLifecycle(); lifetime.Cancel(); lifetime.Dispose(); };
        ShowKeyboardGroup("全部");
        Sync(currentId, autoRotate);
        ShowPage(KeyboardStyles.All.Any(s => s.Id == currentId));
    }

    private Button MakeTab(Border shell, string title, Action click)
    {
        shell.CornerRadius = new CornerRadius(10); shell.Padding = new Thickness(17, 9, 17, 9); shell.BorderThickness = new Thickness(1);
        shell.Child = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold };
        var button = BareButton(); button.Margin = new Thickness(0, 0, 9, 0); button.Content = shell;
        button.AutomationPropertiesName(title); button.Click += (_, _) => click(); return button;
    }

    private void ShowPage(bool keyboard)
    {
        keyboardPageVisible = keyboard;
        dailyPage.Visibility = keyboard ? Visibility.Collapsed : Visibility.Visible;
        keyboardPage.Visibility = keyboard ? Visibility.Visible : Visibility.Collapsed;
        dailyTab.Background = keyboard ? Brushes.White : SelectedFill; dailyTab.BorderBrush = keyboard ? BorderColor : Coral;
        keyboardTab.Background = keyboard ? SelectedFill : Brushes.White; keyboardTab.BorderBrush = keyboard ? Coral : BorderColor;
        rotate.Visibility = keyboard ? Visibility.Collapsed : Visibility.Visible;
        pageHint.Text = keyboard
            ? "新图款随输入播放原生动作；经典款支持逐键定位。"
            : "小日常会循环播放；想让小熊跟着打字，看看「键盘陪伴」。";
        UpdatePreviewLifecycle(restartKeyboardDemo: true);
    }

    private void ShowKeyboardGroup(string group)
    {
        keyboardGroup = group;
        foreach (var filter in keyboardFilters)
        {
            bool selected = filter.Key == group;
            filter.Value.Background = selected ? SelectedFill : Brushes.White;
            filter.Value.BorderBrush = selected ? Coral : BorderColor;
        }
        foreach (var card in keyboardCards)
            card.Button.Visibility = group == "全部" || card.Group == group ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreviewLifecycle(restartKeyboardDemo: true);
    }

    private void UpdatePreviewLifecycle(bool restartKeyboardDemo = false)
    {
        bool visible = loaded && !closed && IsVisible && WindowState != WindowState.Minimized;
        if (visible) previews.Start(); else previews.Stop();
        bool keyboardActive = visible && keyboardPageVisible;
        bool restart = keyboardActive && (restartKeyboardDemo || !keyboardPreviewsActive);
        double now = AppController.Now;
        if (restart)
        {
            demoStarted = now; nextDemoEvent = 0; keyboardDemo.Reset(now); lastDemoSnapshot = null;previewBlink=false;
        }
        keyboardPreviewsActive = keyboardActive;
        foreach (var card in keyboardCards)
        {
            bool shouldRun = keyboardActive && card.Ready && card.Button.Visibility == Visibility.Visible;
            if (card.Suspended == !shouldRun && !(shouldRun && restart)) continue;
            // Suspending clears held input and the native-action clock, so filtered or
            // minimized previews cannot resume halfway through an old key press.
            card.Preview.SetSuspended(!shouldRun); card.Suspended = !shouldRun; card.WasAnimating = false;
            if (shouldRun)
            {
                card.Preview.SetInteraction(keyboardDemo.Snapshot(now), now);
                card.Preview.Refresh();
            }
        }
        if (visible) RefreshPreviews();
    }

    private Button MakeKeyboardCard(string id, string name, string description, string group, Assets assets, int index)
    {
        var view = new PetView(assets) { Width = 300, Height = 200, IsHitTestVisible = false, Visibility = Visibility.Hidden };
        view.SetSuspended(true);
        var box = new Viewbox { Child = view, Stretch = Stretch.Uniform, Margin = new Thickness(8, 7, 8, 4) };
        var preview = new Grid { Height = 150, Background = PreviewColors[(index + 1) % PreviewColors.Length] }; preview.Children.Add(box);
        var placeholder = new TextBlock { Text = "小熊正在准备…", FontSize = 12, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
        preview.Children.Add(placeholder);
        var badge = new TextBlock { FontSize = 10, Foreground = Coral, FontWeight = FontWeights.SemiBold, Margin = new Thickness(7, 3, 7, 3) };
        preview.Children.Add(new Border { Child = badge, Background = ColorBrush(255, 250, 244), CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(7) });
        var panel = new StackPanel(); panel.Children.Add(preview);
        var caption = new StackPanel { Margin = new Thickness(13, 9, 13, 12) };
        caption.Children.Add(new TextBlock { Text = name, FontSize = 15, FontWeight = FontWeights.SemiBold });
        caption.Children.Add(new TextBlock { Text = description, Foreground = Muted, FontSize = 11, Height = 32, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }); panel.Children.Add(caption);
        var shell = new Border { Child = panel, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(2), BorderBrush = BorderColor, Background = Brushes.White, ClipToBounds = true };
        var button = BareButton(); button.Content = shell; button.Margin = new Thickness(5, 0, 5, 10); button.ToolTip = name + " · " + description;
        button.AutomationPropertiesName("选择" + group + "键盘动画：" + name);
        var card = new KeyboardCard { Id = id, Name = name, Group = group, Button = button, Shell = shell, Preview = view, Placeholder = placeholder, Badge = badge }; keyboardCards.Add(card);
        button.Click += async (_, _) =>
        {
            int version = ++pickerSelectionVersion;
            if (card.Loading) return;
            if (!card.Ready) await LoadKeyboardCardAsync(card);
            if (!closed && version == pickerSelectionVersion && card.Ready) Select(card.Id);
        };
        button.MouseEnter += (_, _) => { if (currentId != id) shell.BorderBrush = ColorBrush(220, 168, 145); };
        button.MouseLeave += (_, _) => UpdateKeyboardCard(card);
        return button;
    }

    private Card MakeCard(AnimationEntry entry, int index)
    {
        var image = new Image { Height = 137, Stretch = Stretch.Uniform, Margin = new Thickness(12, 10, 12, 8), SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var placeholder = new TextBlock { Text = "小熊正在准备…", FontSize = 12, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
        var preview = new Grid { Height = 156, Background = PreviewColors[index % PreviewColors.Length] };
        preview.Children.Add(image); preview.Children.Add(placeholder);
        var badge = new TextBlock { FontSize = 10, Foreground = Coral, FontWeight = FontWeights.SemiBold, Margin = new Thickness(7, 3, 7, 3) };
        var pill = new Border { Child = badge, Background = ColorBrush(255, 250, 244), CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(7) };
        preview.Children.Add(pill);
        var panel = new StackPanel(); panel.Children.Add(preview);
        var caption = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
        caption.Children.Add(new TextBlock { Text = entry.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        caption.Children.Add(new TextBlock { Text = entry.Description, Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Height = 32, Margin = new Thickness(0, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(caption);
        var shell = new Border { Child = panel, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(2), BorderBrush = BorderColor, Background = Brushes.White, ClipToBounds = true };
        var button = BareButton(); button.Content = shell; button.Margin = new Thickness(5, 0, 5, 10); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.AutomationPropertiesName("选择动画：" + entry.Name);
        button.ToolTip = entry.Name + " · " + entry.Description;
        var card = new Card { Entry = entry, Button = button, Shell = shell, Preview = image, Placeholder = placeholder, Badge = badge };
        button.Click += async (_, _) =>
        {
            int version = ++pickerSelectionVersion;
            if (card.Loading) return;
            if (card.Clip == null) await LoadCardAsync(card);
            if (!closed && version == pickerSelectionVersion && card.Clip != null) Select(card.Entry.Id);
        };
        button.MouseEnter += (_, _) => { if (currentId != entry.Id) shell.BorderBrush = ColorBrush(220, 168, 145); };
        button.MouseLeave += (_, _) => UpdateCard(card);
        return card;
    }

    private async Task LoadCardAsync(Card card)
    {
        if (closed || card.Loading || card.Clip != null) return;
        card.Loading = true; card.Placeholder.Text = "小熊正在准备…";
        try
        {
            var clip = await library.LoadAsync(card.Entry.Id, lifetime.Token);
            if (closed) return;
            card.Clip = clip; card.Preview.Source = clip.FrameAt(elapsed.Elapsed.TotalSeconds);
            card.Placeholder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!closed) { card.Placeholder.Text = "这段动画暂时没载入\n点一下重新试试"; card.Placeholder.Visibility = Visibility.Visible; }
        }
        finally { card.Loading = false; if (!closed) UpdateCard(card); }
    }

    private async Task LoadKeyboardCardAsync(KeyboardCard card)
    {
        if (closed || card.Loading || card.Ready) return;
        card.Loading = true; card.Placeholder.Text = "小熊正在准备…";
        UpdateKeyboardCard(card);
        try
        {
            if (card.Id == "keyboard") card.Preview.SelectKeyboardStyle("keyboard", AppController.Now);
            else
            {
                if (keyboards == null) throw new IOException("键盘动画暂时无法打开。");
                var scene = await keyboards.LoadAsync(card.Id, lifetime.Token);
                if (closed) return;
                // A newly loaded card starts directly on its own artwork, without the classic scene flashing first.
                card.Preview.SelectKeyboardScene(scene, AppController.Now - 1);
            }
            if (closed) return;
            card.Ready = true; card.Preview.Visibility = Visibility.Visible;
            card.Placeholder.Visibility = Visibility.Collapsed;
            UpdatePreviewLifecycle();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!closed)
            {
                card.Placeholder.Text = "小熊还没准备好\n点一下重新试试";
                card.Placeholder.Visibility = Visibility.Visible;
            }
        }
        finally { card.Loading = false; if (!closed) UpdateKeyboardCard(card); }
    }

    private void RefreshPreviews()
    {
        if (closed || !IsVisible || WindowState == WindowState.Minimized) return;
        if (keyboardPageVisible)
        {
            double now = AppController.Now;
            // These events only update the preview model. No input is sent to Windows.
            if (now - demoStarted >= 8.5)
            {
                demoStarted = now; nextDemoEvent = 0; keyboardDemo.Reset(now);
            }
            while (nextDemoEvent < KeyboardDemoEvents.Length && now - demoStarted >= KeyboardDemoEvents[nextDemoEvent].Time)
            {
                var step = KeyboardDemoEvents[nextDemoEvent++]; keyboardDemo.Handle(step.Event, demoStarted + step.Time);
            }
            var snapshot = keyboardDemo.Snapshot(now);
            bool blinking=now-demoStarted>=6.1&&now-demoStarted<6.26;
            bool changed = lastDemoSnapshot == null || snapshot.LeftKey != lastDemoSnapshot.LeftKey || snapshot.RightKey != lastDemoSnapshot.RightKey ||
                snapshot.ScrollX != lastDemoSnapshot.ScrollX || snapshot.ScrollY != lastDemoSnapshot.ScrollY ||
                !snapshot.Keys.SetEquals(lastDemoSnapshot.Keys) || !snapshot.Buttons.SetEquals(lastDemoSnapshot.Buttons);
            foreach (var card in keyboardCards)
            {
                if (!card.Ready || card.Suspended || card.Button.Visibility != Visibility.Visible) continue;
                if (changed) card.Preview.SetInteraction(snapshot, now);
                if(blinking!=previewBlink)card.Preview.SetBlinking(blinking);
                bool animating = card.Preview.IsAnimating(now);
                if (animating || card.WasAnimating) card.Preview.Refresh();
                card.WasAnimating = animating;
            }
            lastDemoSnapshot = snapshot;
            previewBlink=blinking;
            return;
        }
        double gifElapsed = elapsed.Elapsed.TotalSeconds;
        foreach (var card in cards)
        {
            if (card.Clip == null) continue;
            var frame = card.Clip.FrameAt(gifElapsed);
            if (!ReferenceEquals(card.Preview.Source, frame)) card.Preview.Source = frame;
        }
    }

    private void Select(string id)
    {
        select(id);
        Sync(id, rotate.IsChecked == true);
    }

    public void Sync(string selectedId, bool autoRotate)
    {
        if (closed) return;
        bool selectionChanged = currentId != selectedId;
        syncing = true; currentId = selectedId; rotate.IsChecked = autoRotate;
        foreach (var card in cards) UpdateCard(card);
        foreach (var card in keyboardCards) UpdateKeyboardCard(card);
        var selectedKeyboard = keyboardCards.FirstOrDefault(c => c.Id == selectedId);
        if (selectionChanged && selectedKeyboard != null && keyboardGroup != "全部" && keyboardGroup != selectedKeyboard.Group)
            ShowKeyboardGroup(selectedKeyboard.Group);
        UpdateCurrentLabel(); syncing = false;
    }

    private void UpdateCurrentLabel()
    {
        string name = keyboardCards.FirstOrDefault(c => c.Id == currentId)?.Name ?? cards.FirstOrDefault(c => c.Entry.Id == currentId)?.Entry.Name ?? "挑选喜欢的小日常";
        currentLabel.Text = "正在陪你 · " + name + (rotate.IsChecked == true ? "   /   自动轮播已开启" : "");
    }

    private void UpdateKeyboardCard(KeyboardCard card)
    {
        bool selected = card.Id == currentId;
        card.Shell.BorderBrush = selected ? Coral : BorderColor;
        card.Shell.Background = selected ? SelectedFill : Brushes.White;
        card.Badge.Text = selected ? "陪伴中" : card.Loading ? "准备中" : !card.Ready ? "重新载入" : "动态预览";
    }

    private void UpdateCard(Card card)
    {
        bool selected = card.Entry.Id == currentId;
        card.Shell.BorderBrush = selected ? Coral : BorderColor;
        card.Shell.Background = selected ? SelectedFill : Brushes.White;
        card.Badge.Text = selected ? "陪伴中" : card.Loading ? "准备中" : card.Clip == null ? "重新载入" : "点选";
    }

    private static Brush ColorBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b)); brush.Freeze(); return brush;
    }

    private static Button BareButton()
    {
        var template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='{x:Type Button}' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Border Background='Transparent'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/></Border></ControlTemplate>");
        return new Button { Template = template, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch, FocusVisualStyle = null };
    }
}

internal static class AnimationPickerAccessibility
{
    public static void AutomationPropertiesName(this UIElement element, string name) => System.Windows.Automation.AutomationProperties.SetName(element, name);
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace YierBubu.Windows;

public sealed class SettingsWindow:Window
{
    private readonly Slider scale=new(){Minimum=50,Maximum=250,TickFrequency=1,IsSnapToTickEnabled=true};
    private readonly Slider opacity=new(){Minimum=30,Maximum=100,TickFrequency=5,IsSnapToTickEnabled=true};
    private readonly TextBlock scaleText=new(),opacityText=new(),saveText=new(){TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Brushes.DimGray};
    private readonly CheckBox clickThrough=new(){Content="鼠标穿透（开启后从托盘调整）"},fullscreen=new(){Content="同一屏幕全屏时自动隐藏"};
    private bool syncing;
    private PetSettings current=new();
    public SettingsWindow(PetSettings settings,Action<PetSettings> apply,Action reset)
    {
        Title="一二布布 · 设置";Width=390;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=new SolidColorBrush(Color.FromRgb(255,250,244));FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=14;
        var panel=new StackPanel{Margin=new Thickness(24)};Content=panel;
        panel.Children.Add(new TextBlock{Text="让小熊陪在合适的位置",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,20)});
        panel.Children.Add(scaleText);scale.Margin=new Thickness(0,10,0,18);panel.Children.Add(scale);
        panel.Children.Add(opacityText);opacity.Margin=new Thickness(0,10,0,18);panel.Children.Add(opacity);
        clickThrough.Margin=new Thickness(0,0,0,14);panel.Children.Add(clickThrough);
        fullscreen.Margin=new Thickness(0,0,0,18);panel.Children.Add(fullscreen);
        panel.Children.Add(new TextBlock{Text="右键小熊可打开设置；拖动可移动位置。\nCtrl＋滚轮可缩放，位置和设置会自动记住。\n开启穿透后，可从托盘关闭穿透。",TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,14)});
        panel.Children.Add(saveText);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};
        var restore=new Button{Content="恢复位置",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,10,0)};restore.Click+=(_,_)=>reset();actions.Children.Add(restore);
        var close=new Button{Content="完成",Padding=new Thickness(18,6,18,6),IsCancel=true};close.Click+=(_,_)=>Close();actions.Children.Add(close);panel.Children.Add(actions);
        void Changed(){if(syncing)return;current=current with{Scale=scale.Value/100,Opacity=opacity.Value/100,ClickThrough=clickThrough.IsChecked==true,AutoHideFullscreen=fullscreen.IsChecked==true};UpdateLabels();apply(current);}
        scale.ValueChanged+=(_,_)=>Changed();opacity.ValueChanged+=(_,_)=>Changed();clickThrough.Click+=(_,_)=>Changed();fullscreen.Click+=(_,_)=>Changed();
        Sync(settings);
    }
    private void UpdateLabels(){scaleText.Text=$"大小  {scale.Value:0}%";opacityText.Text=$"不透明度  {opacity.Value:0}%";}
    public void Sync(PetSettings settings)
    {
        syncing=true;current=settings;scale.Value=settings.Scale*100;opacity.Value=settings.Opacity*100;
        clickThrough.IsChecked=settings.ClickThrough;fullscreen.IsChecked=settings.AutoHideFullscreen;UpdateLabels();syncing=false;
    }
    public void SetSaveStatus(string? error)=>saveText.Text=error==null?"已自动保存到本机":"设置未能保存："+error;
}

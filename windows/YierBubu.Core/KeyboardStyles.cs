namespace YierBubu.Core;

public sealed record KeyboardStyle(string Id,string Name,string Description,string Group,IReadOnlyList<string> ClipIds);

public static class KeyboardStyles
{
    public static IReadOnlyList<KeyboardStyle> All { get; }=Array.AsReadOnly(new KeyboardStyle[]
    {
        new("keyboard-yier","一二 · 专心敲字","坐在电脑前，小爪子轮流敲敲。","一二",new[]{"yier-office"}),
        new("keyboard-yier-desk","一二 · 快乐工位","完整小书桌，开心地陪你开工。","一二",new[]{"yier-desk"}),
        new("keyboard-yier-laptop","一二 · 抱抱电脑","抱着小电脑，露出粉粉小脚掌。","一二",new[]{"yier-laptop"}),
        new("keyboard-bubu","布布 · 认真办公","只让布布陪你，双爪忙忙碌碌。","布布",new[]{"bubu-office"}),
        new("keyboard-bubu-laptop","布布 · 抱抱电脑","圆圆布布抱电脑，陪你慢慢忙。","布布",new[]{"bubu-laptop"}),
        new("keyboard-duo","双熊 · 并肩开工","敲键唤一二，点鼠标唤布布。","双熊",new[]{"yier-laptop","bubu-laptop"}),
        new("keyboard","双熊 · 经典键鼠","原版同桌：48键定位与鼠标联动。","双熊",Array.Empty<string>())
    });
    public static bool Contains(string? id)=>All.Any(style=>style.Id==id);
    public static string Migrate(string id)=>id switch
    {
        "keyboard-hearts"=>"keyboard-yier",
        "keyboard-stars"=>"keyboard-bubu",
        "keyboard-ripple"=>"keyboard-duo",
        _=>id
    };
}

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace YierBubu.Windows;

// Render the original eye pixels as a small articulated layer; source art stays intact.
public sealed class IdleFace
{
    private sealed record Eye(Rect Area,BitmapSource Pixels,Brush Skin);
    private readonly Eye[] eyes;
    public IdleFace(AnimatedClip clip)
    {
        Rect[] areas=clip.Id switch
        {
            "yier-office"=>[new(89,89,19,19),new(142,91,19,19)],
            "bubu-office"=>[new(89,119,19,19),new(142,121,19,19)],
            "yier-laptop"=>[new(56,83,20,21),new(115,83,20,21)],
            "bubu-laptop"=>[new(51,86,16,16),new(113,86,17,16)],
            "yier-desk"=>[new(252,429,43,43),new(401,431,45,46)],
            _=>[]
        };
        double scale=clip.Id=="yier-desk"?clip.PixelWidth/640.0:1;
        var frame=clip.Frames[0];
        eyes=areas.Select(area=>
        {
            var rect=new Int32Rect((int)Math.Round(area.X*scale),(int)Math.Round(area.Y*scale),(int)Math.Round(area.Width*scale),(int)Math.Round(area.Height*scale));
            var pixels=new CroppedBitmap(frame,rect);pixels.Freeze();
            byte[] p=new byte[4];frame.CopyPixels(new Int32Rect(rect.X+rect.Width/2,rect.Y-3,1,1),p,4,0);
            var skin=new SolidColorBrush(Color.FromRgb(p[2],p[1],p[0]));skin.Freeze();
            return new Eye(new Rect(rect.X,rect.Y,rect.Width,rect.Height),pixels,skin);
        }).ToArray();
    }
    public void Draw(DrawingContext dc,double age,int mood)
    {
        if(age<0||age>1.35)return;
        double Blink(double start)=>age<start||age>start+.24?0:Math.Sin((age-start)/.24*Math.PI);
        double closed=Math.Max(Blink(.08),mood%4==3?Blink(.40):0);
        double glance=age<.36?0:Math.Sin(Math.Clamp((age-.36)/.99,0,1)*Math.PI);
        foreach(var eye in eyes)
        {
            var area=eye.Area;double dx=area.Width*.12*glance*(mood%2==0?1:-1),dy=area.Height*.06*glance;
            dc.DrawEllipse(eye.Skin,null,new Point(area.X+area.Width/2,area.Y+area.Height/2),area.Width/2+1,area.Height/2+1);
            double height=area.Height*(1-.88*closed);
            var target=new Rect(area.X+dx,area.Y+(area.Height-height)/2+dy,area.Width,height);
            dc.PushClip(new EllipseGeometry(target));dc.DrawImage(eye.Pixels,target);dc.Pop();
        }
    }
}

namespace YierBubu.Core;

public readonly record struct Point2(double X,double Y)
{
    public static Point2 Lerp(Point2 a,Point2 b,double t)=>new(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);
}
public sealed record KeyGeometry(char Label,Point2 Center,Point2 Across,Point2 Depth,bool LeftPaw)
{
    public Point2 SurfaceCenter(bool pressed)=>new(Center.X,Center.Y+(pressed?2:-6));
    public Point2[] SurfaceCorners(bool pressed)=>Corners(pressed?2:-6);
    public Point2[] Corners(double offset=0)=>new (double U,double V)[]{(-1,-1),(1,-1),(1,1),(-1,1)}.Select(p=>new Point2(Center.X+p.U*Across.X+p.V*Depth.X,Center.Y+p.U*Across.Y+p.V*Depth.Y+offset)).ToArray();
    public Point2 Contact=>new(Center.X-Depth.X*Math.Sign(Depth.Y)*0.72,Center.Y+2-Math.Abs(Depth.Y)*0.72);
    public bool ContainsSurface(Point2 point,bool pressed)
    {
        var c=SurfaceCenter(pressed);double x=point.X-c.X,y=point.Y-c.Y,det=Across.X*Depth.Y-Across.Y*Depth.X;
        return Math.Abs((x*Depth.Y-y*Depth.X)/det)<=1&&Math.Abs((Across.X*y-Across.Y*x)/det)<=1;
    }
}
public static class SceneLayout
{
    public const double Width=1536,Height=1024;
    public static IReadOnlyList<KeyGeometry> Keys {get;}=BuildKeys();
    private static KeyGeometry[] BuildKeys()
    {
        var keys=new List<KeyGeometry>();
        void Row(int row, params (char Key,double Width)[] entries)
        {
            double x=0;
            foreach(var (key,width) in entries)
            {
                double center=x+width/2;
                // Rotate the complete keyboard plane 180 degrees about its center.
                // Space is nearest Yier, numbers nearest the viewer; labels rotate too.
                keys.Add(new(key,new(1192-60*center+31*row,992.8-18*center-34*row),
                    new(-60*(width-.12)/2,-18*(width-.12)/2),new(13,-14.3),ScanCodes.LeftKeys.Contains(key)));
                x+=width;
            }
        }
        Row(0,[.."1234567890".Select(k=>(k,1.0)),(SpecialKeys.Backspace,1.6)]);
        Row(1,[(SpecialKeys.Tab,1.2),.."qwertyuiop".Select(k=>(k,1.0))]);
        Row(2,[.."asdfghjkl".Select(k=>(k,1.0)),(SpecialKeys.Enter,1.9)]);
        Row(3,[(SpecialKeys.LeftShift,1.3),.."zxcvbnm".Select(k=>(k,1.0)),(SpecialKeys.RightShift,1.3),(SpecialKeys.Up,1)]);
        Row(4,(SpecialKeys.LeftCtrl,1.2),(SpecialKeys.Space,4),(SpecialKeys.RightCtrl,1.2),
            (SpecialKeys.Left,1),(SpecialKeys.Down,1),(SpecialKeys.Right,1));
        return keys.ToArray();
    }
    public static KeyGeometry? Key(char label)=>Keys.FirstOrDefault(k=>k.Label==label);
    public static Point2 MouseContact(MouseButton b)=>b switch{MouseButton.Left=>new(324,622),MouseButton.Right=>new(255,592),_=>new(280,601)};
    public static Point2[] MouseRegion(MouseButton b)=>b switch{
        MouseButton.Left=>[new(356,553),new(395,564),new(407,591),new(350,637),new(286,653),new(307,601)],
        MouseButton.Right=>[new(250,577),new(314,553),new(356,553),new(307,601),new(224,633)],
        _=>[new(254,619),new(295,578),new(306,587),new(265,628)]};
    public static bool MouseContains(Point2 p,MouseButton button)
    {
        var points=MouseRegion(button);bool inside=false;
        for(int i=0;i<points.Length;i++){var a=points[i];var b=points[(i+1)%points.Length];if((a.Y>p.Y)!=(b.Y>p.Y)&&p.X<(b.X-a.X)*(p.Y-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;}
        return inside;
    }
}

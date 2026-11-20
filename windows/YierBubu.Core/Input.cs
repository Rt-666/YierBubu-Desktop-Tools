namespace YierBubu.Core;

public enum InputKind { KeyDown, KeyUp, MouseDown, MouseUp, Scroll }
public enum MouseButton { Left, Right, Middle }
public readonly record struct InputEvent(InputKind Kind, char Key='\0', MouseButton Button=MouseButton.Left, double X=0, double Y=0, bool Repeat=false);

public static class ScanCodes
{
    public const string VisibleKeys="1234567890qwertyuiopasdfghjklzxcvbnm"+SpecialKeys.All;
    // Paws are named by their screen position. Yier faces the viewer, so the
    // physical right half of the keyboard belongs to the screen-left paw.
    public const string LeftKeys="67890yuiophjklnm"+SpecialKeys.RightHand;
    public static bool TryMap(ushort code,bool e0,bool e1,out char key)
    {
        key='\0';if(e1)return false;
        if(e0)
        {
            key=code switch {0x1d=>SpecialKeys.RightCtrl,0x48=>SpecialKeys.Up,0x50=>SpecialKeys.Down,0x4b=>SpecialKeys.Left,0x4d=>SpecialKeys.Right,_=>'\0'};
            return key!='\0';
        }
        key=code switch {
            >=0x02 and <=0x0b => "1234567890"[code-0x02],
            >=0x10 and <=0x19 => "qwertyuiop"[code-0x10],
            >=0x1e and <=0x26 => "asdfghjkl"[code-0x1e],
            >=0x2c and <=0x32 => "zxcvbnm"[code-0x2c],
            0x39=>SpecialKeys.Space,0x1c=>SpecialKeys.Enter,0x0e=>SpecialKeys.Backspace,0x0f=>SpecialKeys.Tab,
            0x2a=>SpecialKeys.LeftShift,0x36=>SpecialKeys.RightShift,0x1d=>SpecialKeys.LeftCtrl,_ => '\0'};
        return key!='\0';
    }
}
/// Keeps only current physical keys, including invisible modifiers, for repeat detection.
public sealed class KeyboardNormalizer
{
    private readonly HashSet<int> held=[];
    public InputEvent Normalize(ushort scan,ushort flags)
    {
        int token=scan|((flags&6)<<16);
        ScanCodes.TryMap(scan,(flags&2)!=0,(flags&4)!=0,out var key);
        if((flags&1)!=0){held.Remove(token);return new(InputKind.KeyUp,Key:key);}
        bool repeat=!held.Add(token);
        return new(InputKind.KeyDown,Key:key,Repeat:repeat);
    }
    public void Reset()=>held.Clear();
}
public sealed class InputActivity
{
    public long KeyPresses {get;private set;}
    public long LeftClicks {get;private set;}
    public long RightClicks {get;private set;}
    public long MiddleClicks {get;private set;}
    public long ScrollEvents {get;private set;}
    public void Record(InputEvent input)
    {
        switch(input.Kind){
            case InputKind.KeyDown when !input.Repeat:KeyPresses++;break;
            case InputKind.MouseDown:
                switch(input.Button){case MouseButton.Left:LeftClicks++;break;case MouseButton.Right:RightClicks++;break;case MouseButton.Middle:MiddleClicks++;break;}break;
            case InputKind.Scroll when double.IsFinite(input.X)&&double.IsFinite(input.Y)&&(input.X!=0||input.Y!=0):ScrollEvents++;break;
        }
    }
}

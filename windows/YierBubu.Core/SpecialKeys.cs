namespace YierBubu.Core;

// Private-use identifiers keep physical keys separate from typed characters.
public static class SpecialKeys
{
    public const char Space='\uE001', Enter='\uE002', Backspace='\uE003', Tab='\uE004',
        LeftShift='\uE005', RightShift='\uE006', LeftCtrl='\uE007', RightCtrl='\uE008',
        Up='\uE009', Down='\uE00A', Left='\uE00B', Right='\uE00C';
    public const string All="\uE001\uE002\uE003\uE004\uE005\uE006\uE007\uE008\uE009\uE00A\uE00B\uE00C";
    public const string LeftHand="\uE004\uE005\uE007";
    public const string RightHand="\uE001\uE002\uE003\uE006\uE008\uE009\uE00A\uE00B\uE00C";
    public static bool IsModifier(char key) => key is LeftShift or RightShift or LeftCtrl or RightCtrl;
    public static string Name(char key) => key switch
    {
        Space=>"Space", Enter=>"Enter", Backspace=>"Bksp", Tab=>"Tab",
        LeftShift or RightShift=>"Shift", LeftCtrl or RightCtrl=>"Ctrl",
        Up=>"↑", Down=>"↓", Left=>"←", Right=>"→", _=>key.ToString()
    };
}

namespace LocalDps;

public static class WindowPreferencesSelfTests
{
    public static void Run()
    {
        string root=Path.Combine(AppContext.BaseDirectory,"LocalDps-window-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(root,"window-preferences.json");
        void Require(bool value,string message){if(!value)throw new Exception("Window preferences: "+message);}
        try
        {
            Require(WindowPreferences.Load(path)==new WindowPreferences(),"fresh install uses readable full view");
            var preferences=new WindowPreferences{Compact=true,AlwaysOnTop=true,Opacity=.65,Left=-1200,Top=30,Width=700,Height=900,CompactWidth=340,CompactHeight=330};
            preferences.Save(path);
            Require(WindowPreferences.Load(path)==preferences,"mode, pin, opacity, both sizes and negative-monitor position survive restart");
            var invalid=preferences with {Opacity=0,Width=0,Height=double.NaN,CompactWidth=double.PositiveInfinity,CompactHeight=1,Left=double.NaN,Top=double.NegativeInfinity};
            invalid.Save(path);
            var normalized=WindowPreferences.Load(path);
            Require(normalized.Opacity==.3&&normalized.Width==460&&normalized.Height==790&&normalized.CompactWidth==320&&normalized.CompactHeight==180,"invalid dimensions and opacity cannot make the window invisible or unusably small");
            Require(normalized.Left is null&&normalized.Top is null,"nonfinite positions are discarded before serialization");
            Require((preferences with {Opacity=double.NaN}).Normalize().Opacity==1,"nonfinite opacity falls back to opaque");
            File.WriteAllText(path,new string('x',8193));
            bool rejected=false;try{WindowPreferences.Load(path);}catch(InvalidDataException){rejected=true;}
            Require(rejected,"oversized settings rejected");
            File.WriteAllText(path,"not json");
            rejected=false;try{WindowPreferences.Load(path);}catch(System.Text.Json.JsonException){rejected=true;}
            Require(rejected,"malformed settings reported to caller for safe fallback");
            Require(Directory.GetFiles(root).Length==1,"atomic saves leave no temporary files");
        }
        finally
        {
            if(File.Exists(path))File.Delete(path);
            if(Directory.Exists(root))Directory.Delete(root);
        }
    }
}

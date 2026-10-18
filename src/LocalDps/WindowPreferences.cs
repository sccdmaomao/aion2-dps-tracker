using System.Text.Json;

namespace LocalDps;

public sealed record WindowPreferences
{
    public bool Compact {get;init;}
    public bool AlwaysOnTop {get;init;}
    public double Opacity {get;init;}=1;
    public double? Left {get;init;}
    public double? Top {get;init;}
    public double Width {get;init;}=580;
    public double Height {get;init;}=790;
    public double CompactWidth {get;init;}=320;
    public double CompactHeight {get;init;}=330;
    public static string FilePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDps","window-preferences.json");

    public WindowPreferences Normalize()=>this with
    {
        Opacity=Bound(Opacity,.3,1,1),
        Width=Bound(Width,460,4000,580),Height=Bound(Height,520,4000,790),
        CompactWidth=Bound(CompactWidth,260,1200,320),CompactHeight=Bound(CompactHeight,180,2000,330),
        Left=Left.HasValue&&double.IsFinite(Left.Value)?Left:null,
        Top=Top.HasValue&&double.IsFinite(Top.Value)?Top:null
    };
    static double Bound(double value,double min,double max,double fallback)=>double.IsFinite(value)?Math.Clamp(value,min,max):fallback;

    public static WindowPreferences Load(string? path=null)
    {
        path??=FilePath;
        if(!File.Exists(path))return new();
        if(new FileInfo(path).Length>8192)throw new InvalidDataException("Window preferences are too large.");
        return (JsonSerializer.Deserialize<WindowPreferences>(File.ReadAllText(path))??throw new InvalidDataException("Invalid window preferences.")).Normalize();
    }

    public void Save(string? path=null)
    {
        path??=FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(Normalize()));File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}

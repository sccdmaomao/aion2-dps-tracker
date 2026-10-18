using System.Text.Json;
namespace LocalDps;
public sealed record TrackingProfile(string SelfName)
{
    public static string FilePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDps","track-people.json");
    public static TrackingProfile Load(string? path=null,string? legacyPath=null)
    {
        path??=FilePath;legacyPath??=Path.Combine(AppContext.BaseDirectory,"track-people.json");
        string source=File.Exists(path)?path:legacyPath;
        if(!File.Exists(source))return new("");
        if(new FileInfo(source).Length>8192)throw new InvalidDataException("Tracking profile is too large.");
        var profile=JsonSerializer.Deserialize<TrackingProfile>(File.ReadAllText(source),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Invalid tracking profile.");
        return Create(profile.SelfName);
    }
    public static TrackingProfile Create(string self)
    {
        bool Valid(string n)=>n.Length is >=1 and <=40&&n.Any(char.IsLetter)&&n.All(c=>char.IsLetterOrDigit(c)||c is '_' or '-');
        self=(self??"").Trim();if(self.Length>0&&!Valid(self))throw new InvalidDataException("Enter your exact character name.");
        return new(self);
    }
    public void Apply(CombatScope scope){scope.ConfirmedSelfName=SelfName;}
    public void Save(string? path=null)
    {
        path??=FilePath;Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}

using System.Text.Json;
namespace LocalDps;
public sealed record TrackingProfile(string SelfName)
{
    public static string FilePath=>Path.Combine(AppContext.BaseDirectory,"track-people.json");
    public static TrackingProfile Load()
    {
        if(!File.Exists(FilePath))return new("");
        if(new FileInfo(FilePath).Length>8192)throw new InvalidDataException("Tracking profile is too large.");
        var profile=JsonSerializer.Deserialize<TrackingProfile>(File.ReadAllText(FilePath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Invalid tracking profile.");
        return Create(profile.SelfName);
    }
    public static TrackingProfile Create(string self)
    {
        bool Valid(string n)=>n.Length is >=1 and <=40&&n.Any(char.IsLetter)&&n.All(c=>char.IsLetterOrDigit(c)||c is '_' or '-');
        self=(self??"").Trim();if(self.Length>0&&!Valid(self))throw new InvalidDataException("Enter your exact character name.");
        return new(self);
    }
    public void Apply(CombatScope scope){scope.ConfirmedSelfName=SelfName;}
    public void Save()=>File.WriteAllText(FilePath,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
}

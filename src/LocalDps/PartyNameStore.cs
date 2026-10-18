using System.Text.Json;
using LocalDps.Protocol;
namespace LocalDps;

/// <summary>Names only: never stores party membership or combat entity IDs.</summary>
public static class PartyNameStore
{
    public static string FilePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDps","known-character-names.json");
    public static KnownCharacterName[] Load(string? path=null)
    {
        path??=FilePath;if(!File.Exists(path))return Array.Empty<KnownCharacterName>();
        if(new FileInfo(path).Length>128*1024)throw new InvalidDataException("Character-name cache exceeds its size limit.");
        var names=JsonSerializer.Deserialize<KnownCharacterName[]>(File.ReadAllText(path))??Array.Empty<KnownCharacterName>();
        if(names.Length>512)throw new InvalidDataException("Character-name cache has too many entries.");
        return names.Where(n=>n is not null).ToArray();
    }
    public static void Save(KnownCharacterName[] names,string? path=null)
    {
        path??=FilePath;Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(names.Take(512)));File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}

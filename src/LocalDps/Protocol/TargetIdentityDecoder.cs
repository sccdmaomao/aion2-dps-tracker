using System.Buffers.Binary;
using System.Reflection;
using System.IO;
using System.Text.Json;

namespace LocalDps.Protocol;

/// <summary>Resolves entity IDs through observed NPC spawn templates; entity IDs are never used as template IDs.</summary>
public sealed class TargetIdentityDecoder
{
    private readonly Dictionary<int,int> templates = new();
    private readonly Dictionary<int,string> names = new();
    private readonly Dictionary<int,long> seen = new();
    private readonly IReadOnlyDictionary<int,string> catalog;
    public IReadOnlyDictionary<int,int> Templates => templates;
    public IReadOnlyDictionary<int,string> Names => names;
    public string? CatalogWarning {get;}
    public TargetIdentityDecoder(IReadOnlyDictionary<int,string>? catalog = null,string? catalogPath=null)
    {
        if(catalog is not null){this.catalog=catalog;return;}
        this.catalog=ReadCatalog(out var warning,catalogPath);CatalogWarning=warning;
    }
    public string? Resolve(int targetId) => names.GetValueOrDefault(targetId);
    public void Reset() { templates.Clear();names.Clear();seen.Clear(); }
    public void Observe(ReadOnlySpan<byte> record,long timestamp)
    {
        int p=0;
        if(!Var(record,ref p,out int declared) || declared<6 || p+2>record.Length) return;
        byte op=record[p++],family=record[p++];
        if(family!=0x36 || op is not (0x40 or 0x41 or 0x42))return;
        if(!Var(record,ref p,out int entity) || entity<=0 || p+3>record.Length)return;
        p+=3; // flag byte and two-byte flags field precede optional two-byte prefix.
        foreach(int start in new[]{p+2,p})
        {
            if(start+6>record.Length)continue;
            int template=BinaryPrimitives.ReadInt32LittleEndian(record[start..]);
            if(template<1_000_000 || template>10_000_000 || record[start+5]!=2)continue;
            if(templates.Count>=8192 && !templates.ContainsKey(entity))
            {
                int oldest=seen.MinBy(x=>x.Value).Key;templates.Remove(oldest);names.Remove(oldest);seen.Remove(oldest);
            }
            templates[entity]=template;seen[entity]=timestamp;
            if(catalog.TryGetValue(template,out var name) && !string.IsNullOrWhiteSpace(name))names[entity]=name;
            else names.Remove(entity);
            return;
        }
    }
    private static IReadOnlyDictionary<int,string> ReadCatalog(out string? warning,string? catalogPath)
    {
        warning=null;
        var names=new Dictionary<int,string>();
        var assembly=typeof(TargetIdentityDecoder).Assembly;
        var resource=assembly.GetManifestResourceNames().FirstOrDefault(x=>x.EndsWith(".target-names.json",StringComparison.Ordinal));
        if(resource is not null)
        {
            using var input=assembly.GetManifestResourceStream(resource);
            if(input is not null)names=JsonSerializer.Deserialize<Dictionary<int,string>>(input)??new();
        }
        string path=catalogPath??Path.Combine(AppContext.BaseDirectory,"target-names.json");
        if(!File.Exists(path))return names;
        try
        {
            if(new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException("Target catalog exceeds 2 MiB.");
            var custom=JsonSerializer.Deserialize<Dictionary<int,string>>(File.ReadAllText(path))??throw new InvalidDataException("Target catalog must be a JSON object.");
            if(custom.Count>20000||custom.Any(p=>p.Key is <1_000_000 or >10_000_000||string.IsNullOrWhiteSpace(p.Value)||p.Value.Length>200||p.Value.Any(char.IsControl)))throw new InvalidDataException("Invalid target catalog entry.");
            foreach(var pair in custom)names[pair.Key]=pair.Value;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException){warning="Target catalog ignored: "+ex.Message;}
        return names;
    }
    private static bool Var(ReadOnlySpan<byte> data,ref int p,out int value)
    {
        value=0;
        for(int shift=0;shift<35;shift+=7)
        {
            if(p>=data.Length)return false;byte b=data[p++];if(shift==28 && (b&0xf0)!=0)return false;
            value|=(b&127)<<shift;if((b&128)==0)return value>=0;
        }
        return false;
    }
}

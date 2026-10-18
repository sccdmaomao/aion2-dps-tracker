using System.Buffers.Binary;
using System.Text;
namespace LocalDps.Protocol;

public sealed record KnownCharacterName(ulong CharacterId,int ServerId,uint ClassId,string Name);

/// <summary>Name lookup only. A social directory never establishes membership.</summary>
internal sealed class CharacterNameDirectory
{
    private byte[] snapshot=Array.Empty<byte>();
    private readonly Dictionary<(ulong,int,uint),KnownCharacterName> known=new();
    public long Version {get;private set;}
    public KnownCharacterName[] KnownNames=>known.Values.ToArray();
    private static readonly UTF8Encoding Utf8=new(false,true);
    public void Reset()=>snapshot=Array.Empty<byte>();
    public void Remember(ulong id,int server,uint job,string name)
    {
        if(id is 0 or >0xffffffffffff||server is <1 or >65535||job is <1 or >64||string.IsNullOrWhiteSpace(name)||name.Length>40||!name.Any(char.IsLetter)||name.Any(c=>!char.IsLetterOrDigit(c)&&c is not('_' or '-')))return;
        var key=(id,server,job);
        if(known.TryGetValue(key,out var old)&&old.Name==name)return;
        if(known.Count>=512&&!known.ContainsKey(key))known.Remove(known.Keys.First());
        known[key]=new(id,server,job,name);Version++;
    }
    public void Replace(ReadOnlySpan<byte> body)
    {
        snapshot=body.Length is >=3 and <=65536&&body[0]==0&&body[1]==0&&body[2] is >0 and <=100?body.ToArray():Array.Empty<byte>();
    }
    public bool TryResolve(ulong character,int server,uint job,out string name)
    {
        name="";int matches=0;
        // Entry tails vary. Match only the exact persistent ID/server/class from
        // a separate party update, with the observed account/name/level anchors.
        // Never enumerate directory names into the party roster.
        for(int p=12;p+13<snapshot.Length;p++)
        {
            if(snapshot[p-9] is not(0 or 2)||BinaryPrimitives.ReadUInt64LittleEndian(snapshot.AsSpan(p-8))==0)continue;
            ulong id=0;for(int k=0;k<6;k++)id|=(ulong)snapshot[p+k]<<(k*8);
            if(id!=character||BinaryPrimitives.ReadUInt16LittleEndian(snapshot.AsSpan(p+6))!=server||BinaryPrimitives.ReadUInt32LittleEndian(snapshot.AsSpan(p+8))!=job)continue;
            int n=snapshot[p+12],start=p+13;
            if(n is <1 or >90||start+n+29>snapshot.Length)continue;
            uint level=BinaryPrimitives.ReadUInt32LittleEndian(snapshot.AsSpan(start+n));
            if(level is <1 or >1000)continue;
            string candidate;
            try{candidate=Utf8.GetString(snapshot,start,n);}catch(DecoderFallbackException){continue;}
            if(candidate.Length is <1 or >40||!candidate.Any(char.IsLetter)||candidate.Any(c=>!char.IsLetterOrDigit(c)&&c is not('_' or '-')))continue;
            if(++matches>1){name="";if(known.Remove((character,server,job)))Version++;return false;}
            name=candidate;
        }
        if(matches==1){Remember(character,server,job,name);return true;}
        if(known.TryGetValue((character,server,job),out var cached)){name=cached.Name;return true;}
        return false;
    }
}

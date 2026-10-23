using System.Buffers.Binary;
using System.Text;
namespace LocalDps.Protocol;

public sealed record CharacterStats(int? ItemLevel,int? CombatPower,uint? ClassId=null);

/// <summary>Session-only stats; these observations never grant party membership.</summary>
public sealed class CharacterStatsDecoder
{
    private readonly Dictionary<(ulong,int),RoomCharacterDetails> room=new();
    private readonly Dictionary<int,(string Name,int Server,uint Job,CharacterStats Stats)> actors=new();
    private byte[] directory=Array.Empty<byte>();
    public void Reset(){room.Clear();actors.Clear();directory=Array.Empty<byte>();}
    public CharacterStats? ForMember(PartyMemberIdentity member)
    {
        CharacterStats? social=DirectoryStats(member.Name,member.CharacterId,member.ServerId,null,null);
        if(member.CharacterId is ulong character&&member.ServerId is int server&&room.TryGetValue((character,server),out var found)&&found.Character.Name==member.Name)
            return new(found.ItemLevel>0?found.ItemLevel:social?.ItemLevel,found.CombatPower??social?.CombatPower,found.Character.ClassId);
        return social;
    }
    public CharacterStats? ForSelf(EntityIdentityDecoder identities)
    {
        if(identities.LocalPlayerId is not int actor||!actors.TryGetValue(actor,out var own)||identities.Names.GetValueOrDefault(actor)!=own.Name)return null;
        var found=room.Values.Where(x=>x.Character.Name==own.Name&&(x.Character.ServerId==own.Server||x.CurrentServerId==own.Server)).Take(2).ToArray();
        var social=DirectoryStats(own.Name,null,null,own.Job,own.Stats.ItemLevel);
        return found.Length==1?new(own.Stats.ItemLevel??(found[0].ItemLevel>0?found[0].ItemLevel:social?.ItemLevel),found[0].CombatPower??social?.CombatPower,own.Job):new(own.Stats.ItemLevel??social?.ItemLevel,social?.CombatPower,own.Job);
    }
    public void Observe(ReadOnlySpan<byte> record,EntityIdentityDecoder identities)
    {
        int p=0;if(!ReadVar(record,ref p,out int declared)||p+2>record.Length||declared-4+p!=record.Length)return;
        byte op=record[p++],family=record[p++];
        if(op==5&&family==0x8a)
        {
            var body=record[p..];
            directory=body.Length is >=3 and <=65536&&body[0]==0&&body[1]==0&&body[2] is >0 and <=100?body.ToArray():Array.Empty<byte>();
            return;
        }
        if(op==2&&family==0x97)
        {
            foreach(var detail in RoomCharacterNames.ReadDetails(record[p..]))
            {
                var key=(detail.Character.CharacterId,detail.Character.ServerId);
                if(room.Count>=512&&!room.ContainsKey(key))room.Remove(room.Keys.First());
                room[key]=detail;
            }
        }
        if(op!=0x33||family!=0x36||!ReadVar(record,ref p,out int actor)||actor!=identities.LocalPlayerId||p+6>record.Length)return;
        p+=4;if(record[p] is not(7 or 0x37 or 0x3f))return;
        int nameLength=record[p+1];p+=2+nameLength;
        if(p+6>record.Length||!identities.Names.TryGetValue(actor,out var name))return;
        int server=BinaryPrimitives.ReadUInt16LittleEndian(record[p..]);
        uint job=BinaryPrimitives.ReadUInt32LittleEndian(record[(p+2)..]);
        int? itemLevel=null;
        for(int start=p+2;start<=p+12&&start+16<=record.Length;start++)
        {
            int level=BinaryPrimitives.ReadInt32LittleEndian(record[start..]);
            int gear=BinaryPrimitives.ReadInt32LittleEndian(record[(start+4)..]);
            if(level is >=1 and <=99&&gear is >0 and <=99999&&gear==BinaryPrimitives.ReadInt32LittleEndian(record[(start+8)..])&&level==BinaryPrimitives.ReadInt32LittleEndian(record[(start+12)..]))
            {itemLevel=gear;break;}
        }
        if(actors.Count>=8192&&!actors.ContainsKey(actor))actors.Remove(actors.Keys.First());
        actors[actor]=(name,server,job,new(itemLevel,null));
    }
    private CharacterStats? DirectoryStats(string name,ulong? character,int? server,uint? job,int? itemLevel)
    {
        // Party members require their persistent ID and home server. For proven
        // self, require an exact name, class and item-level match; conflicting
        // directory entries remain unavailable. No directory data grants scope.
        if(character is null&&(job is null||itemLevel is null))return null;
        byte[] expected=Encoding.UTF8.GetBytes(name);CharacterStats? result=null;int matches=0;
        for(int p=12;p+13<directory.Length;p++)
        {
            if(directory[p-9] is not(0 or 2)||BinaryPrimitives.ReadUInt64LittleEndian(directory.AsSpan(p-8))==0)continue;
            ulong id=0;for(int k=0;k<6;k++)id|=(ulong)directory[p+k]<<(8*k);
            int home=BinaryPrimitives.ReadUInt16LittleEndian(directory.AsSpan(p+6));
            uint entryJob=BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(p+8));
            if(id==0||home is <1001 or >3000||entryJob is <1 or >64||character.HasValue&&id!=character||server.HasValue&&home!=server||job.HasValue&&entryJob!=job)continue;
            int n=directory[p+12],end=p+13+n;
            if(n!=expected.Length||end+29>directory.Length||!directory.AsSpan(p+13,n).SequenceEqual(expected))continue;
            int level=BinaryPrimitives.ReadInt32LittleEndian(directory.AsSpan(end));
            if(level is <1 or >99)continue;
            int tail=end+5;byte state=directory[end+4];
            if(state==1){if(tail>=directory.Length||directory[tail++]!=1)continue;}
            else if(state is not(0 or 2))continue;
            if(directory[p-9]==2)
            {
                if(tail>=directory.Length)continue;
                int alias=directory[tail++];if(alias>90||tail+alias>directory.Length)continue;tail+=alias;
            }
            if(tail+24>directory.Length)continue;
            int gear=BinaryPrimitives.ReadInt32LittleEndian(directory.AsSpan(tail+12));
            int power=BinaryPrimitives.ReadInt32LittleEndian(directory.AsSpan(tail+16));
            if(gear is <1 or >99999||power is <1 or >999999999||BinaryPrimitives.ReadInt32LittleEndian(directory.AsSpan(tail+20))!=0||itemLevel.HasValue&&gear!=itemLevel)continue;
            if(++matches>1)return null;
            result=new(gear,power,entryJob);
        }
        return result;
    }
    private static bool ReadVar(ReadOnlySpan<byte> data,ref int p,out int value)
    {
        value=0;for(int shift=0;shift<35&&p<data.Length;shift+=7){byte b=data[p++];if(shift==28&&(b&0xf8)!=0)return false;value|=(b&127)<<shift;if((b&128)==0)return true;}return false;
    }
}

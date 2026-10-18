using System.Buffers.Binary;
using System.Text;
namespace LocalDps.Protocol;

internal sealed record RoomCharacterDetails(KnownCharacterName Character,int ItemLevel,int? CombatPower,int? CurrentServerId);

/// <summary>Recruiting-room identities are name metadata only, never membership.</summary>
internal static class RoomCharacterNames
{
    private static readonly UTF8Encoding Utf8=new(false,true);
    public static KnownCharacterName[] Read(ReadOnlySpan<byte> body)=>ReadDetails(body).Select(x=>x.Character).ToArray();
    public static RoomCharacterDetails[] ReadDetails(ReadOnlySpan<byte> body)
    {
        if(body.Length<28)return Array.Empty<RoomCharacterDetails>();
        int p=5+body[4];if(p+19>body.Length)return Array.Empty<RoomCharacterDetails>();
        int slots=body[p];if(slots is <1 or >10)return Array.Empty<RoomCharacterDetails>();p+=15;
        if(body[p+3]!=slots)return Array.Empty<RoomCharacterDetails>();p+=4;
        var choices=new byte[slots];var found=new byte[slots];int readings=0;
        Walk(body,p,1,slots,choices,found,ref readings);
        if(readings!=1)return Array.Empty<RoomCharacterDetails>();
        var names=new List<RoomCharacterDetails>();
        for(int slot=1;slot<=slots;slot++)
        {
            int end=SlotEnd(body,p,slot,found[slot-1],out var name);if(end<0)return Array.Empty<RoomCharacterDetails>();
            if(name is not null)
            {
                int nameEnd=p+11+body[p+10];
                int gear=BinaryPrimitives.ReadInt32LittleEndian(body[(nameEnd+8)..]);
                var candidates=new HashSet<(int Server,int Power)>();
                for(int start=nameEnd+12;start<=nameEnd+14&&start+11<=end;start++)
                    // This stats block carries the current server, which can
                    // differ from the character's home server in this slot.
                    if(BinaryPrimitives.ReadUInt16LittleEndian(body[start..]) is >=1001 and <=3000&&body[start+2]==7&&body[start+3]==0x10&&body[start+4]==4&&body[start+9]==0&&body[start+10]==0)
                    {
                        int cp=BinaryPrimitives.ReadInt32LittleEndian(body[(start+5)..]);
                        if(cp is >0 and <=999999999)candidates.Add((BinaryPrimitives.ReadUInt16LittleEndian(body[start..]),cp));
                    }
                names.Add(new(name,gear,candidates.Count==1?candidates.Single().Power:null,candidates.Count==1?candidates.Single().Server:null));
            }
            p=end;
        }
        return names.ToArray();
    }
    private static void Walk(ReadOnlySpan<byte> body,int p,int slot,int slots,byte[] choices,byte[] found,ref int readings)
    {
        if(readings>1)return;
        if(slot>slots){if(body.Length-p==2&&body[p]==0){if(readings++==0)choices.CopyTo(found,0);}return;}
        for(byte state=0;state<=1;state++)
        {
            int end=SlotEnd(body,p,slot,state,out _);if(end<0)continue;
            choices[slot-1]=state;Walk(body,end,slot+1,slots,choices,found,ref readings);
        }
    }
    private static int SlotEnd(ReadOnlySpan<byte> b,int p,int slot,int state,out KnownCharacterName? name)
    {
        name=null;if(p+11>b.Length)return -1;
        byte flags=b[p];if(b[p+1]!=slot||(flags!=0&&((flags&12)!=12||(flags&0xe1)!=0)))return -1;
        uint id=BinaryPrimitives.ReadUInt32LittleEndian(b[(p+2)..]);
        if(BinaryPrimitives.ReadUInt16LittleEndian(b[(p+6)..])!=0)return -1;
        int server=BinaryPrimitives.ReadUInt16LittleEndian(b[(p+8)..]),n=b[p+10],text=p+11,end=text+n;
        if(n>90||end+12>b.Length)return -1;
        uint job=BinaryPrimitives.ReadUInt32LittleEndian(b[end..]),level=BinaryPrimitives.ReadUInt32LittleEndian(b[(end+4)..]),gear=BinaryPrimitives.ReadUInt32LittleEndian(b[(end+8)..]);
        if(flags==0){if(id!=0||server!=0||n!=0||job!=0||level!=0||gear!=0)return -1;}
        else
        {
            if(id==0||server==0||n==0||job is <1 or >64||level is <1 or >99||gear>99999)return -1;
            string value;try{value=Utf8.GetString(b.Slice(text,n));}catch(DecoderFallbackException){return -1;}
            if(!value.Any(char.IsLetter)||value.Any(c=>!char.IsLetterOrDigit(c)&&c is not('_' or '-')))return -1;
            name=new(id,server,job,value);
        }
        end+=12+state+(flags==0?0:4);if(end+10>b.Length)return -1;
        int extras=b[end+9];if(extras>8)return -1;
        end+=10+5*extras+((flags&16)!=0?8:0)+2;
        return end<=b.Length?end:-1;
    }
}

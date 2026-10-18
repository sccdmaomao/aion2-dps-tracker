using System.Buffers.Binary;
using System.Text;
namespace LocalDps.Protocol;

/// <summary>Recruiting-room identities are name metadata only, never membership.</summary>
internal static class RoomCharacterNames
{
    private static readonly UTF8Encoding Utf8=new(false,true);
    public static KnownCharacterName[] Read(ReadOnlySpan<byte> body)
    {
        if(body.Length<28)return Array.Empty<KnownCharacterName>();
        int p=5+body[4];if(p+19>body.Length)return Array.Empty<KnownCharacterName>();
        int slots=body[p];if(slots is <1 or >10)return Array.Empty<KnownCharacterName>();p+=15;
        if(body[p+3]!=slots)return Array.Empty<KnownCharacterName>();p+=4;
        var choices=new byte[slots];var found=new byte[slots];int readings=0;
        Walk(body,p,1,slots,choices,found,ref readings);
        if(readings!=1)return Array.Empty<KnownCharacterName>();
        var names=new List<KnownCharacterName>();
        for(int slot=1;slot<=slots;slot++)
        {
            int end=SlotEnd(body,p,slot,found[slot-1],out var name);if(end<0)return Array.Empty<KnownCharacterName>();
            if(name is not null)names.Add(name);p=end;
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

using System.Buffers.Binary;
using System.Text;
namespace LocalDps.Protocol;

public static class PartyStartupSelfTests
{
    public static void Run()
    {
        byte[] Frame(byte op,byte family,byte[] body)
        {
            int length=body.Length+6;var prefix=new List<byte>();
            do{byte b=(byte)(length&127);length>>=7;prefix.Add((byte)(b|(length>0?128:0)));}while(length>0);
            return prefix.Concat(new[]{op,family}).Concat(body).ToArray();
        }
        byte[] Periodic(ulong id=410001,string guild="TestGuild")
        {
            var guildName=Encoding.UTF8.GetBytes(guild);
            var b=new byte[63+guildName.Length];for(int i=0;i<6;i++)b[i]=(byte)(id>>(i*8));
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6),1102);b[8]=9;b[36]=45;b[48]=(byte)guildName.Length;guildName.CopyTo(b,49);b[^1]=1;
            return Frame(0x1c,0x92,b);
        }
        byte[] Directory(int server=1102,int job=9)
        {
            byte[] name=Encoding.UTF8.GetBytes("隊友");var b=new byte[3+1+8+6+2+4+1+name.Length+29];
            b[2]=1;b[4]=123;ulong id=410001;for(int i=0;i<6;i++)b[12+i]=(byte)(id>>(i*8));
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(18),(ushort)server);
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(20),job);b[24]=(byte)name.Length;name.CopyTo(b,25);b[25+name.Length]=45;
            return Frame(5,0x8a,b);
        }
        var party=new PartyRosterDecoder();party.Observe(Directory(),1);
        Require(!party.IsAuthoritative&&party.Members.Count==0,"social directory cannot establish membership");
        party.Observe(Periodic(),2);
        Require(party.RecoveredAtStartup&&party.Members.Single() is {Name:"隊友",EntityId:0,CharacterId:410001},"periodic party update recovers existing peer and exact directory name");
        party.Observe(new byte[]{8,0x13,0x92,0,0},3);party.Observe(Periodic(),4);
        Require(party.Members.Count==0&&!party.RecoveredAtStartup,"delayed periodic packet cannot restore a departed party");
        var known=party.KnownNames;
        party.Reset();Require(!party.IsAuthoritative&&party.Members.Count==0,"remembered names cannot preserve membership across reset");
        party.Observe(Periodic(),5);
        Require(party.Members.Single().Name=="隊友","learned name survives capture reset without another directory");
        party=new PartyRosterDecoder();party.RestoreKnownNames(known);
        Require(party.Members.Count==0&&!party.IsAuthoritative,"restoring name cache cannot establish membership");
        party.Observe(Periodic(),5);Require(party.Members.Single().Name=="隊友","name cache resolves an out-of-range member on restart");
        party=new PartyRosterDecoder();party.Observe(Periodic(),5);
        Require(party.Members.Single().Name=="Unknown party member","missing name does not invent a name or combat ID");
        party.Observe(Directory(),6);Require(party.Members.Single().Name=="隊友","late directory resolves existing periodic member");
        party=new PartyRosterDecoder();party.Observe(Directory(1101),7);party.Observe(Periodic(),8);
        Require(party.Members.Single().Name=="Unknown party member","name resolution requires exact server");
        party=new PartyRosterDecoder();party.Observe(Directory(job:10),9);party.Observe(Periodic(),10);
        Require(party.Members.Single().Name=="Unknown party member","name resolution requires exact class");
        party.Reset();var malformed=Periodic();malformed[^1]=0;party.Observe(malformed,11);
        Require(!party.IsAuthoritative,"unrecognized periodic layout cannot establish membership");
        party.Observe(Periodic(),12);party.Observe(Periodic(410002),13);party.Observe(Periodic(410003),13);party.Observe(Periodic(410004),13);
        Require(party.IsAuthoritative&&party.Members.Count==4,"periodic recovery retains all four teammates in a five-player party");
        party.Observe(Periodic(410005),13);
        Require(!party.IsAuthoritative&&party.Members.Count==0,"more than four peers exceeds the five-player party limit");
        party.Reset();party.Observe(Frame(0x0d,0x92,new byte[]{3,5}),14);party.Observe(Periodic(),15);
        Require(!party.IsAuthoritative&&party.Members.Count==0,"unknown explicit roster outranks periodic recovery");
        foreach(string guild in new[]{"","GuildName","公會"})
        {
            party.Reset();party.Observe(Directory(),16);party.Observe(Periodic(guild:guild),17);
            Require(party.RecoveredAtStartup&&party.Members.Single().Name=="隊友","length-delimited optional guild field: "+guild);
        }
    }
    static void Require(bool value,string message){if(!value)throw new Exception("Party startup: "+message);}
}

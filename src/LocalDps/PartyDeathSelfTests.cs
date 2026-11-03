using System.Text;
using LocalDps.Protocol;

namespace LocalDps;

public static class PartyDeathSelfTests
{
    public static void Run()
    {
        var scope=new CombatScope();scope.Observe(Named(0x33,500,"Hero"),0);scope.Observe(Join(),0);scope.Observe(Named(0x44,600,"Ally"),0);
        scope.Observe(Named(0x44,700,"Nearby"),0);
        var encounters=new EncounterTracker{DisplayName=scope.Name,DescribePlayers=rows=>scope.DisplayRows(rows)};encounters.Start();
        encounters.Add(new CombatHit(1000,500,900,11010001,100,false));var first=encounters.Latest!;
        var tracker=new PartyDeathTracker(scope,encounters);
        void Hp(int actor,int hp,long time)=>tracker.Observe(Health(actor,hp),time);
        PlayerRow[] Rows()=>tracker.Describe(scope.DisplayRows(encounters.View(first).Players),first);
        Hp(500,0,1100);Require(tracker.ObservedDeaths==0,"initial zero is not a confirmed death");
        Hp(500,100,1200);Hp(500,0,2000);Hp(500,0,2000);Hp(500,0,2100);
        Hp(500,100,3000);Hp(500,0,4000);encounters.AdvanceTo(4000);
        Require(Rows().Single(r=>r.ActorId==500).Deaths==2,"resurrection permits another death; duplicate zero does not");
        Hp(600,100,1200);Hp(600,0,2500);
        Require(Rows().Single(r=>r.ActorId==600) is {Deaths:1,Hits:0},"idle or healing-only party member is counted");
        Hp(700,100,1200);Hp(700,0,2600);Hp(900,100,1200);Hp(900,0,2600);
        Require(tracker.ObservedDeaths==3&&!Rows().Any(r=>r.ActorId==700),"nearby players and NPCs do not enter party death counts");
        Hp(500,100,1500);Hp(500,0,4500);Require(tracker.ObservedDeaths==3,"out-of-order positive HP cannot manufacture a revival");
        tracker.Observe(Health(500,100).AsSpan(0,10),4600);
        tracker.Observe(Health(500,100,tail:26),4600);
        tracker.Observe(Health(500,101,max:100),4600);
        tracker.Observe(Health(500,100,max:0),4600);
        Hp(500,0,4700);Require(tracker.ObservedDeaths==3,"malformed and unsupported health records cannot prime a death");
        Hp(500,100,4800);Hp(500,0,4800);Require(tracker.ObservedDeaths==4,"ordered HP transitions in one TCP chunk can share receive time");

        scope.Observe(Frame(new byte[]{0x13,0x92,0,0}),5000);Hp(600,100,5100);Hp(600,0,5200);encounters.AdvanceTo(5200);
        Require(Rows().Single(r=>r.ActorId==600) is {Actor:"Ally",Deaths:1,Hits:0,ClassId:9},"leaving party preserves death-only history and class metadata");
        Require(tracker.ObservedDeaths==4,"departed member's future health does not count");
        scope.Observe(Join(),5300);Hp(600,0,5400);Require(tracker.ObservedDeaths==4,"new membership cannot inherit the old positive HP baseline");
        scope.Observe(Named(0x44,600,"Replacement"),5500);Hp(600,100,5600);Hp(600,0,5700);
        Require(tracker.ObservedDeaths==4,"reused entity with a different name cannot borrow membership");
        Require(Rows().Count(r=>r.Actor=="Ally")==1&&Rows().Single(r=>r.Actor=="Ally").Deaths==1,"unmapped current row does not duplicate or hide its captured history");

        encounters.AdvanceTo(20000);encounters.Add(new CombatHit(30000,500,901,11010001,50,false));
        var second=tracker.Describe(scope.DisplayRows(encounters.View(encounters.Latest).Players),encounters.Latest);
        Require(second.Single(r=>r.ActorId==500).Deaths==0,"next encounter starts at zero");
        Require(Rows().Single(r=>r.ActorId==500).Deaths==3,"historical count stays with the original fight");
        Require(tracker.Describe([new PlayerRow(null,"Distant",0,0,0,0)],encounters.Latest).Single().DeathText=="—","unmapped member remains unavailable");
        tracker.Reset();Require(tracker.ObservedDeaths==0&&Rows().Single(r=>r.ActorId==500).Deaths==0,"capture reset clears counters and baselines");
        Hp(500,100,40000);Hp(500,0,71000);Require(tracker.ObservedDeaths==0,"stale positive HP is not used as death proof");
    }

    static byte[] Join()
    {
        var body=new byte[148];body[0]=3;body[1]=2;BitConverter.GetBytes(12345).CopyTo(body,2);
        BitConverter.GetBytes((ushort)1101).CopyTo(body,6);BitConverter.GetBytes((ushort)1101).CopyTo(body,8);
        body[10]=36;Encoding.ASCII.GetBytes("00000000-0000-0000-0000-000000000001").CopyTo(body,11);
        body[47]=1;BitConverter.GetBytes((ushort)1102).CopyTo(body,53);body[55]=4;Encoding.UTF8.GetBytes("Ally").CopyTo(body,56);body[60]=9;
        return Frame(new byte[]{0x0d,0x92}.Concat(body).ToArray());
    }
    static byte[] Named(byte opcode,int actor,string name)
    {
        var bytes=Encoding.UTF8.GetBytes(name);
        return Frame(new[]{opcode,(byte)0x36}.Concat(V(actor)).Concat(new byte[]{0,0,0,0,7,(byte)bytes.Length}).Concat(bytes).ToArray());
    }
    static byte[] Health(int actor,int hp,int max=100,int tail=25)=>Frame(new byte[]{0x1b,0x92}.Concat(V(actor)).Concat(V(hp)).Concat(V(max)).Concat(new byte[tail]).ToArray());
    static byte[] Frame(byte[] body)=>V(body.Length+4).Concat(body).ToArray();
    static byte[] V(int value){var bytes=new List<byte>();do{int b=value&127;value>>=7;bytes.Add((byte)(b|(value>0?128:0)));}while(value>0);return bytes.ToArray();}
    static void Require(bool value,string label){if(!value)throw new Exception("Party deaths: "+label);}
}

using LocalDps.Protocol;

namespace LocalDps;

public static class EncounterDefeatSelfTests
{
    public static void Run()
    {
        var health=new TargetHealthDecoder();
        var tracker=new EncounterTracker{FindTargetDeath=health.FindDeath};tracker.Start();
        void Hp(int target,int hp,long time)=>health.Observe(Health(target,hp),time);
        void Hit(long time,int target=900,int damage=100)=>tracker.Add(new CombatHit(time,500,target,11010001,damage,false));
        Hp(900,1000,0);Hit(0);Hit(4000,damage:200);Hp(900,0,5000);tracker.AdvanceTo(5000);
        var first=tracker.Latest!;
        Require(tracker.State(first)=="Defeated"&&tracker.EndMs(first)==5000&&tracker.Seconds(first)==5,"confirmed death finishes a single-target fight immediately");
        Require(tracker.View(first).Players.Single().Dps==60,"DPS uses death time rather than idle grace");
        tracker.AdvanceTo(6000);
        Require(tracker.View(first).Players.Single().Dps==60&&tracker.Seconds(first)==5,"idle traffic cannot reduce defeated target DPS");
        Hit(5047);
        Require(tracker.Encounters.Count==1&&tracker.View(first).Damage==400&&tracker.View(first).Players.Single().Dps==80&&tracker.EndMs(first)==5000,"final skewed hit counts without extending timer");
        Hit(5200,901);
        Require(tracker.Encounters.Count==2&&tracker.Latest!.Meter.TotalDamage==100&&tracker.View(first).Damage==400,"new target after defeat starts a fresh fight inside idle window");
        Hit(1000,damage:200);
        Require(tracker.Encounters.Count==2&&tracker.View(first).Damage==600&&tracker.Latest!.Meter.TotalDamage==100,"late identity hit cannot bridge a confirmed death into the next fight");
        tracker.AdvanceTo(100000);
        Require(tracker.EndMs(first)==5000&&tracker.View(first,900).Seconds==5,"historical defeated target stays frozen");

        // A selected dead target stops while another captured target remains alive.
        health.Reset();tracker.Reset();tracker.Start();
        Hp(900,1000,1000);Hp(901,1000,1000);Hit(1000,900);Hit(2000,901);
        Hp(900,0,3000);tracker.AdvanceTo(4000);var multi=tracker.Latest!;
        Require(tracker.State(multi)=="In combat"&&tracker.Seconds(multi)==3,"one defeated target does not stop other targets");
        Require(tracker.State(multi,900)=="Defeated"&&tracker.View(multi,900).Seconds==2&&tracker.View(multi,900).Players.Single().Dps==50,"selected target freezes independently");
        Require(tracker.State(multi,901)=="In combat"&&tracker.View(multi,901).Seconds==3,"living target keeps running");
        Hp(901,0,6000);tracker.AdvanceTo(6000);
        Require(tracker.State(multi)=="Defeated"&&tracker.Seconds(multi)==5,"all-target fight freezes when the last captured target dies");
        tracker.AdvanceTo(30000);
        Require(tracker.View(multi,900).Seconds==2&&tracker.View(multi,901).Seconds==5&&tracker.View(multi).Seconds==5,"switching target views preserves each frozen end time");

        // A confirmed new life must not be mistaken for a delayed killing hit.
        health.Reset();tracker.Reset();tracker.Start();
        Hp(900,1000,0);Hit(0);Hp(900,0,1000);tracker.AdvanceTo(1000);
        var oldLife=tracker.Latest!;Hp(900,1000,1100);Hit(1150);
        Require(tracker.Encounters.Count==2&&tracker.State(oldLife)=="Defeated"&&tracker.State(tracker.Latest!)=="In combat","respawn within skew window starts a new fight");
        health.Reset();tracker.Reset();tracker.Start();
        Hp(900,1000,0);Hit(0);Hp(900,0,1000);tracker.AdvanceTo(1000);Hit(1300);
        Require(tracker.Encounters.Count==2,"hit after death skew cannot reopen finished fight");

        // Delayed identity recovery is still assigned using event timestamps.
        health.Reset();tracker.Reset();tracker.Start();
        Hp(900,1000,0);Hit(2000);Hp(900,0,5000);tracker.AdvanceTo(5000);var delayed=tracker.Latest!;
        Hit(1000,damage:200);
        Require(tracker.Encounters.Count==1&&tracker.View(delayed).Damage==300&&tracker.View(delayed).Seconds==4,"late earlier hit adjusts start but retains confirmed end");

        health.Reset();tracker.Reset();tracker.Start();
        Hit(1000);Hp(900,0,2000);Hp(901,1000,1000);Hp(901,0,2000);tracker.AdvanceTo(3000);
        Require(tracker.State(tracker.Latest!)=="In combat"&&tracker.Seconds(tracker.Latest!)==2,"unconfirmed zero or unrelated target death cannot stop fight");
        tracker.AdvanceTo(20000);
        Require(tracker.State(tracker.Latest!)=="Complete"&&tracker.Seconds(tracker.Latest!)==15,"missing death evidence retains idle timeout");

        var row=new PlayerRow(500,"Hero",100,1234.56,1,100);
        Require(row.DpsText==1234.56.ToString("N0")&&row.Dps==1234.56,"DPS presentation rounds to whole numbers without changing precision");
    }
    static byte[] Health(int target,int hp)
    {
        var entity=new List<byte>();int n=target;
        do{int part=n&127;n>>=7;entity.Add((byte)(part|(n>0?128:0)));}while(n>0);
        var body=new byte[]{0,0x8d}.Concat(entity).Concat(new byte[]{2,1,0}).Concat(BitConverter.GetBytes(hp)).Concat(new byte[4]).ToArray();
        return new byte[]{(byte)(body.Length+4)}.Concat(body).ToArray();
    }
    static void Require(bool condition,string label){if(!condition)throw new Exception("Encounter defeat: "+label);}
}

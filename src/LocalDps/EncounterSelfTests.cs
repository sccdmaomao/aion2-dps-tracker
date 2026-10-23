using LocalDps.Protocol;
namespace LocalDps;
public static class EncounterSelfTests
{
    static void Assert(bool value,string label){if(!value)throw new Exception("Encounter: "+label);}
    static CombatHit Hit(long time,int actor=1,int target=10,long damage=100)=>new(time,actor,target,1,damage,false);
    public static void Run()
    {
        var tracker=new EncounterTracker();tracker.Start();tracker.AdvanceTo(1000);
        Assert(tracker.Latest is null,"idle traffic does not start combat");
        tracker.Add(Hit(2000));var first=tracker.Latest!;
        Assert(tracker.Seconds(first)==0&&tracker.View(first).Players.Single().Dps==100,"first hit starts clock with safe DPS floor");
        tracker.AdvanceTo(7000);
        Assert(tracker.Seconds(first)==5&&tracker.View(first).Players.Single().Dps==20,"clock and DPS advance between hits");
        tracker.Add(Hit(8000,2,11,500));tracker.AdvanceTo(12000);
        var target=tracker.View(first,11);
        Assert(target.Damage==500&&target.Seconds==10&&target.Players.Single().Dps==50,"target filter shares full encounter time");
        Assert(first.Meter.Targets(elapsedSeconds:10).Single(t=>t.TargetId==11).Dps==50,"exported target DPS uses same denominator");
        tracker.AdvanceTo(23000);
        Assert(tracker.State(first)=="Complete"&&tracker.Seconds(first)==21,"idle timeout includes ending grace period");
        tracker.AdvanceTo(100000);
        Assert(tracker.Seconds(first)==21,"completed timer freezes");
        tracker.Add(Hit(100000));var second=tracker.Latest!;
        Assert(tracker.Encounters.Count==2&&second.Meter.TotalDamage==100,"next fight starts with fresh totals");
        tracker.Add(Hit(5000,1,10,300));
        Assert(first.Meter.TotalDamage==900&&second.Meter.TotalDamage==100,"late identity hit goes to previous fight");
        tracker.Stop();tracker.AdvanceTo(102000);
        Assert(tracker.State(second)=="Stopped","stopped recording is not labelled live combat");
        tracker.Reset();tracker.Add(Hit(0));tracker.Add(Hit(15000));
        Assert(tracker.Encounters.Count==2,"exact timeout boundary starts new encounter");
        tracker.Reset();tracker.Add(Hit(0));tracker.Add(Hit(20000,2));tracker.Add(Hit(10000,3));
        Assert(tracker.Encounters.Count==1&&tracker.Latest!.Meter.TotalDamage==300&&tracker.Latest.Meter.TotalHits==3,"late bridging hit merges without double counting");
        var view=tracker.View(tracker.Latest,allowedActors:new HashSet<int>{2});
        Assert(view.Damage==100&&view.Seconds==20&&view.Players.Single().Dps==5,"actor filtering retains encounter clock");
        tracker.AdvanceTo(-1000);Assert(tracker.ClockMs==20000,"out of order chunks cannot rewind clock");
        tracker.Reset();for(int i=0;i<EncounterTracker.HistoryLimit+5;i++)tracker.Add(Hit(i*30000));
        Assert(tracker.Encounters.Count==EncounterTracker.HistoryLimit,"history is bounded");
        tracker.Add(Hit(0));Assert(tracker.Encounters.Count==EncounterTracker.HistoryLimit&&tracker.Encounters.Sum(e=>e.Meter.TotalHits)==EncounterTracker.HistoryLimit,"expired history cannot be resurrected by delayed hits");
        tracker.Reset();Assert(tracker.Latest is null&&tracker.ClockMs is null&&!tracker.Running,"clear resets clock and history");
        string observedName="Teammate";
        tracker.DisplayName=_=>observedName;
        tracker.DescribePlayers=rows=>rows.Select(r=>r with {ItemLevel=2500,CombatPower=125000,ClassId=34}).ToArray();
        tracker.Add(Hit(0,1));tracker.Add(Hit(20000,2));
        observedName="Reused entity";
        tracker.Add(Hit(10000,3));
        var saved=tracker.View(tracker.Latest).Players;
        Assert(saved.Single(r=>r.ActorId==1) is {Actor:"Teammate",ItemLevel:2500,CombatPower:125000,ClassId:34}
            &&saved.Single(r=>r.ActorId==2).Actor=="Teammate","merged history retains captured names and stats despite later entity mapping changes");
    }
}

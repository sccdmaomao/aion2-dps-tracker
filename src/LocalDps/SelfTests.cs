using LocalDps.Protocol;
namespace LocalDps;
public static class SelfTests
{
    public static void Run()
    {
        LocalDps.Capture.CaptureSelfTests.Run();
        LocalDps.Capture.AdapterSelection.SelfTest();
        LocalDps.Protocol.ProtocolSelfTests.Run();
        PipelineSelfTests.Run();
        ScopeSelfTests.Run();
        PartyProtocolSelfTests.Run();
        PartyStartupSelfTests.Run();
        PartyNameStoreSelfTests.Run();
        TrackingProfileSelfTests.Run();
        WindowPreferencesSelfTests.Run();
        CharacterStatsSelfTests.Run();
        FivePlayerSelfTests.Run();
        EncounterSelfTests.Run();
        Assert(LocalDps.Capture.GameFlowDirection.IsServerToClient("[203.0.113.10]:13328>[192.168.1.10]:55020",new HashSet<int>()),"offline server direction");
        Assert(!LocalDps.Capture.GameFlowDirection.IsServerToClient("[192.168.1.10]:55020>[203.0.113.10]:13328",new HashSet<int>{13328}),"outgoing identity excluded");
        Assert(!LocalDps.Capture.GameFlowDirection.IsServerToClient("invalid",new HashSet<int>{13328}),"malformed flow excluded");
        TargetSelfTests.Run();
        TargetCatalogSelfTests.Run();
        TargetHealthSelfTests.Run();
        var rules=new SkillRules(new[]{11010001},new[]{12010000},new[]{13010000});
        var known=new CombatHit(0,100,200,11010001,100,false);
        Assert(!rules.Include(known),"exact direct exclusion");
        Assert(rules.Include(known with{SkillId=11010002}),"exact exclusions do not expand to base IDs");
        Assert(rules.Include(known with{IsDot=true}),"direct exact exclusion does not bleed into DoT");
        Assert(!rules.Include(known with{SkillId=12010013}),"heal base exclusion");
        Assert(!rules.Include(known with{SkillId=13010013,IsDot=true}),"periodic non-damage exclusion");
        Assert(rules.Include(known with{SkillId=14010013}),"unknown skill retained");
        Assert(SkillRules.LoadBundled().Include(known with{SkillId=11010001}),"bundled known damage retained");
        new LocalDps.Capture.TcpStreamAssembler().ProcessFrame(new byte[4],DateTimeOffset.UnixEpoch,0);
        var meter=new Meter();
        meter.Add(new CombatHit(1000,1,9,100,1200,false));
        meter.Add(new CombatHit(3000,2,9,101,800,false));
        meter.Add(new CombatHit(5000,1,9,100,800,false));
        var rows=meter.Players();
        Assert(meter.TotalDamage==2800&&meter.TotalHits==3,"damage/hits");
        Assert(rows[0].ActorId==1&&rows[0].Damage==2000&&rows[0].Dps==500,"DPS and ranking");
        Assert(Math.Abs(rows.Sum(x=>x.Share)-100)<0.00001,"shares");
        Assert(meter.Skills().Single(x=>x.ActorId==1).Hits==2,"skill aggregation");
        meter.Add(new CombatHit(9000,99,88,100,50000,false));
        var personal=meter.View(1,9);
        Assert(personal.Damage==2000&&personal.Hits==2&&personal.Seconds==4&&personal.Players.Single().Dps==500,"nearby actors/targets excluded from personal scope and duration");
        Assert(meter.View(null,9).Damage==2800,"target filtering retains multiple attackers");
        Assert(meter.View(1,88).Hits==0,"unmatched actor/target scope empty");
        meter.TargetName=id=>id==9?"Training target":"Other target";
        var target=meter.Targets(allowedActors:new HashSet<int>{1,2}).Single();
        Assert(target.Target=="Training target"&&target.Damage==2800&&target.Dps==700&&target.Hits==3&&target.Seconds==4,"per-target DPS excludes unrelated actors and target durations");
        meter.Add(new CombatHit(11000,1,10,102,100,false));
        var targets=meter.Targets(1,new HashSet<int>{1});
        Assert(targets.Single(t=>t.TargetId==9).Dps==500&&targets.Single(t=>t.TargetId==10).Dps==100,"separate target clocks and single-hit target floor");
        meter.Reset();Assert(meter.TotalDamage==0&&meter.Players().Length==0,"reset");
        meter.Add(new CombatHit(0,1,9,1,100,false));Assert(meter.Players()[0].Dps==100,"single hit duration");
        System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"self-test-passed.txt"),"Protocol, TCP reconstruction and meter tests passed at "+DateTimeOffset.Now);
    }
    static void Assert(bool condition,string label){if(!condition)throw new Exception("Failed: "+label);}
}

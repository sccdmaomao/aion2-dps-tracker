using LocalDps.Protocol;
namespace LocalDps;
public static class TargetHealthSelfTests
{
    static void Assert(bool value,string label){if(!value)throw new Exception("Target health: "+label);}
    public static void Run()
    {
        // Exact server records from the local five-player dungeon recording.
        var alive=Convert.FromHexString("14008DECE6010201006000000000000000");
        var zero=Convert.FromHexString("14008DECE6010201000000000000000000");
        var removal=Convert.FromHexString("0B4236ECE6010003");
        var decoder=new TargetHealthDecoder();
        decoder.Observe(zero,1000);Assert(decoder.Deaths.Count==0,"zero without prior positive HP is unconfirmed");
        decoder.Observe(alive,1100);decoder.Observe(removal,1200);
        Assert(decoder.Deaths.Count==0,"removal alone does not confirm death");
        decoder.Observe(zero,1300);decoder.Observe(zero,1400);
        Assert(decoder.Deaths.Single()==new TargetDeath(29548,1300),"positive-to-zero transition and duplicate suppression");
        Assert(decoder.FindDeath(29548,1000,1250)?.TimestampMs==1300,"matches target in encounter");
        Assert(decoder.FindDeath(29548,1000,1347)?.TimestampMs==1300,"final hit can follow zero HP by a small receive-time skew");
        Assert(decoder.FindDeath(31276,1000,1250) is null,"unrelated target excluded");
        decoder.Observe(alive,1450);
        Assert(decoder.FindDeath(29548,1400,1500) is null,"positive HP prevents respawn damage inheriting old death");
        Assert(decoder.FindDeath(29548,1000,2000) is null,"later hits outside the skew window invalidate old death");
        Assert(decoder.FindDeath(29548,-20000,-15000) is null,"later fight death cannot finish an earlier encounter");
        decoder.Reset();decoder.Observe(alive,1000);decoder.Observe(zero,32000);
        Assert(decoder.Deaths.Count==0,"stale positive HP is not proof");
        decoder.Reset();decoder.Observe(alive,2000);decoder.Observe(zero,1000);
        Assert(decoder.Deaths.Count==0,"out of order old zero cannot kill a live target");
        decoder.Observe(zero.AsSpan(0,zero.Length-1),2100);
        var bad=(byte[])zero.Clone();bad[7]=2;decoder.Observe(bad,2200);
        Assert(decoder.Deaths.Count==0&&decoder.UpdatesSeen==1,"truncated or unsupported layout ignored");
        decoder.Reset();Assert(decoder.Deaths.Count==0&&decoder.UpdatesSeen==0,"reset");
    }
}

using System.Text;

namespace LocalDps.Protocol;

public static class ChainedCombatSelfTests
{
    public static void Run()
    {
        var first=Body(500,900,1200,11010001,7);
        var second=Body(600,901,350,12010001,8);
        var third=Body(500,900,1200,11010001,7);
        var packet=Chain(first,second,third);
        var decoder=new GameProtocolDecoder();
        int observed=0;var callbacks=new List<CombatHit>();
        decoder.RecordObserved+=(ReadOnlySpan<byte> _,long __)=>observed++;
        decoder.HitDecoded+=callbacks.Add;
        var hits=decoder.Feed("chain",packet,12345);
        Require(hits.Count==3&&hits.Sum(h=>h.Damage)==2750,"every chained hit counted once");
        Require(hits.Select(h=>h.ActorId).SequenceEqual(new[]{500,600,500})&&
            hits.Select(h=>h.TargetId).SequenceEqual(new[]{900,901,900})&&
            hits.Select(h=>h.SkillId).SequenceEqual(new[]{11010001,12010001,11010001})&&
            hits.Select(h=>h.EventCounter).SequenceEqual(new[]{7,8,7}),"per-hit identity, target, skill and counter");
        Require(hits.All(h=>h.TimestampMs==12345)&&observed==1&&callbacks.SequenceEqual(hits),"one record observation and one callback per accepted hit");
        Require(decoder.RejectedRecords==0&&decoder.RejectionReasons.Count==0,"probes have no rejection side effects");

        // A chain marker may follow the existing tail, its leading field, or
        // the damage directly. A terminal 1,0 pair alone is not another hit.
        foreach(var tail in new[]{new byte[]{1,0},new byte[]{1},Array.Empty<byte>()})
        {
            decoder.Reset();
            var result=decoder.Feed("tails",Chain(Body(500,900,1200,tail:tail),second),0);
            Require(result.Count==2&&result.Sum(h=>h.Damage)==1550,"continuation at a supported tail boundary");
        }
        decoder.Reset();
        Require(decoder.Feed("single",Chain(first),0).Count==1,"ordinary terminal 1,0 is not a chain");
        for(int mode=0;mode<4;mode++)
        {
            decoder.Reset();
            var result=decoder.Feed("layouts",Chain(Body(500,900,1200,mode:mode),Body(600,901,350,mode:mode)),0);
            Require(result.Count==2&&result.Sum(h=>h.Damage)==1550,"existing direct header layouts in chained hits");
        }

        // Every possible TCP split, including inside the marker and header.
        for(int split=0;split<=packet.Length;split++)
        {
            decoder.Reset();
            var before=decoder.Feed("split",packet.AsSpan(0,split),100);
            var after=decoder.Feed("split",packet.AsSpan(split),100);
            Require(before.Count+after.Count==3&&before.Concat(after).Sum(h=>h.Damage)==2750,"split chained record");
        }
        foreach(var framing in new[]{GameFraming.LegacyCombat,GameFraming.WidthAware})
        {
            decoder.Reset();decoder.Framing=framing;
            var wide=Chain(framing,Enumerable.Repeat(first,8).ToArray());
            Require(wide[0]>=128,"multi-byte test frame");
            var result=decoder.Feed("wide",wide.Concat(Chain(framing,second)).ToArray(),0);
            Require(result.Count==9&&result.Sum(h=>h.Damage)==9950,"wide chain and following frame boundary");
        }
        decoder.Framing=GameFraming.LegacyCombat;
        decoder.Reset();
        var extras=Body(500,900,1200,tail:new byte[]{1,2,1,0});
        var extraHits=decoder.Feed("extras",Chain(extras,second),0);
        Require(extraHits.Count==2&&extraHits[0].Damage==1201,"extra-hit values containing 1,0 stay values");

        // Unsupported tails are not searched for marker-like bytes. A damaged
        // continuation also cannot consume the next independently framed record.
        foreach(var trailer in new[]{new byte[]{2,0},new byte[]{0x7f,1,0},new byte[]{3,0,50,1,0}})
        {
            decoder.Reset();
            var body=new byte[]{4,0x38}.Concat(first).Concat(trailer).Concat(second).ToArray();
            var result=decoder.Feed("trailer",Frame(body).Concat(Chain(third)).ToArray(),0);
            Require(result.Count==2&&result.Sum(h=>h.Damage)==2400,"no scan through unknown or healing trailer");
        }
        for(int cut=0;cut<second.Length-2;cut++)
        {
            decoder.Reset();
            var body=new byte[]{4,0x38}.Concat(first).Concat(new byte[]{1,0}).Concat(second.Take(cut)).ToArray();
            var result=decoder.Feed("short",Frame(body).Concat(Chain(third)).ToArray(),0);
            Require(result.Count==2&&result.Sum(h=>h.Damage)==2400,"truncated continuation preserves first and next framed hit");
        }
        decoder.Reset();
        Require(decoder.Feed("invalid",Chain(Body(500,500,1200),second),0).Count==0,"invalid leading hit cannot resynchronize inside its record");
        decoder.Reset();
        Require(decoder.Feed("invalid-next",Chain(first,Body(600,901,350,999)),0).Count==1,"invalid continuation skill rejected");
        decoder.Reset();
        Require(decoder.Feed("missing-extra",Chain(first,Body(600,901,350,tail:new byte[]{1,2,100})),0).Count==1,"missing continuation extra-hit value rejected");
        decoder.Reset();
        Require(decoder.Feed("bounded",Chain(Enumerable.Repeat(first,65).ToArray()),0).Count==64,"bounded chained hit count");

        decoder.Reset();decoder.IsDamageSkill=skill=>skill!=11010001;
        Require(decoder.Feed("skill-filter",packet,0).Single().ActorId==600&&decoder.FilteredHits==2,"filtered leading skill does not hide later damage");
        decoder.IsDamageSkill=null;decoder.Reset();
        var rules=new SkillRules(new[]{12010001},Array.Empty<int>(),Array.Empty<int>());
        decoder.IncludeHit=rules.Include;
        Require(decoder.Feed("rules",packet,0).Count==2&&decoder.FilteredHits==1,"each chained hit passes damage classification");
        decoder.IncludeHit=null;

        var scope=new CombatScope();scope.Observe(Named(0x33,500,"Hero"),0);scope.Observe(Named(0x44,600,"Nearby"),0);
        var scopedDecoder=new GameProtocolDecoder();var meter=new Meter();
        var attribution=new PartyHitAttribution(scope,meter.Add);
        scopedDecoder.HitDecoded+=attribution.Add;
        scopedDecoder.Feed("scope",packet,0);
        Require(meter.TotalHits==2&&meter.TotalDamage==2400&&scope.ExcludedHits==1,"chained damage still requires verified self or party ownership");

        decoder.Reset();
        var compressed=LiteralBlock(packet);
        var bundle=V(compressed.Length+10).Concat(new byte[]{255,255}).Concat(BitConverter.GetBytes(packet.Length)).Concat(compressed).ToArray();
        Require(decoder.Feed("bundle",bundle,0).Count==3&&decoder.BundlesDecoded==1,"chained hits inside LZ4 bundle");
        decoder.Reset();
        var batch=Frame(new byte[]{0x3d,0x38}.Concat(packet).Concat(Chain(second)).ToArray());
        Require(decoder.Feed("batch",batch,0).Count==4,"chained hits inside framed batch");
    }

    static byte[] Body(int actor,int target,int damage,int skill=11010001,int counter=1,byte[]? tail=null,int mode=0)
        =>V(target).Concat(new byte[]{(byte)(4+mode),0}).Concat(V(actor)).Concat(BitConverter.GetBytes(skill))
            .Concat(new byte[]{(byte)counter,0}).Concat(new byte[mode switch{0=>8,1=>13,2=>11,_=>15}])
            .Concat(new byte[]{0}).Concat(V(damage)).Concat(tail??new byte[]{1,0}).ToArray();

    static byte[] Chain(params byte[][] hits)=>Chain(GameFraming.LegacyCombat,hits);
    static byte[] Chain(GameFraming framing,params byte[][] hits)
    {
        var body=new List<byte>{4,0x38};
        for(int i=0;i<hits.Length;i++){if(i>0)body.AddRange(new byte[]{1,0});body.AddRange(hits[i]);}
        return Frame(body.ToArray(),framing);
    }
    static byte[] Frame(byte[] body,GameFraming framing=GameFraming.LegacyCombat)
    {
        int declared=body.Length+4;
        if(framing==GameFraming.LegacyCombat)
            while(declared!=body.Length+V(declared).Length+3)declared=body.Length+V(declared).Length+3;
        return V(declared).Concat(body).ToArray();
    }
    static byte[] Named(byte opcode,int id,string name)
    {
        var text=Encoding.UTF8.GetBytes(name);
        return Frame(new byte[]{opcode,0x36}.Concat(V(id)).Concat(new byte[]{0,0,0,0,7,(byte)text.Length}).Concat(text).ToArray());
    }
    static byte[] LiteralBlock(byte[] bytes)
    {
        var encoded=new List<byte>{0xf0};int remainder=bytes.Length-15;
        while(remainder>=255){encoded.Add(255);remainder-=255;}
        encoded.Add((byte)remainder);encoded.AddRange(bytes);return encoded.ToArray();
    }
    static byte[] V(int n)
    {
        var bytes=new List<byte>();
        do{int part=n&127;n>>=7;bytes.Add((byte)(part|(n>0?128:0)));}while(n>0);
        return bytes.ToArray();
    }
    static void Require(bool condition,string label){if(!condition)throw new Exception("Chained combat: "+label);}
}

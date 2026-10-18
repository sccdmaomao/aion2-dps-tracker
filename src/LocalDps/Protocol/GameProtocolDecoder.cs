using System.Buffers.Binary;
using System.IO;

namespace LocalDps.Protocol;

public sealed record CombatHit(long TimestampMs, int ActorId, int TargetId, int SkillId,
    long Damage, bool IsDot, int EventCounter = 0, bool IsCritical = false);
public enum GameFraming { LegacyCombat, WidthAware }
public delegate void GameRecordHandler(ReadOnlySpan<byte> record,long timestampMs);

/// <summary>Independent, bounded decoder for ordered Aion 2 game TCP streams.
/// Packet layouts inferred from static artifact analysis; live captures must validate them.</summary>
public sealed class GameProtocolDecoder
{
    public event GameRecordHandler? RecordObserved;
    public event Action<CombatHit>? HitDecoded;
    private readonly Dictionary<string, List<byte>> streams = new();
    private readonly HashSet<(int,int,int,int)> poolStreams = new();
    private readonly Dictionary<ushort,long> opcodeCounts=new();
    private readonly Dictionary<string,long> rejectionReasons=new();
    public IReadOnlyDictionary<string,long> OpcodeCounts=>opcodeCounts.ToDictionary(p=>p.Key.ToString("X4"),p=>p.Value);
    public IReadOnlyDictionary<string,long> RejectionReasons=>new Dictionary<string,long>(rejectionReasons);
    public long RejectedRecords { get; private set; }
    public long BundlesDecoded { get; private set; }
    public long RecordsFramed { get; private set; }
    public long FilteredHits { get; private set; }
    /// <summary>Optional local skill metadata filter; return false for known healing/buff skills.</summary>
    public Func<int, bool>? IsDamageSkill { get; set; }
    public Func<CombatHit, bool>? IncludeHit { get; set; }
    /// <summary>Recovered combat parsing advances declared-3; a separate identity assembler
    /// uses declared-4+varint width. Keep these hypotheses explicit until real traffic resolves them.</summary>
    public GameFraming Framing { get; set; }=GameFraming.LegacyCombat;
    public void Reset() { streams.Clear(); poolStreams.Clear(); opcodeCounts.Clear(); rejectionReasons.Clear(); RejectedRecords = BundlesDecoded = RecordsFramed = FilteredHits = 0; }

    public IReadOnlyList<CombatHit> Feed(string streamId, ReadOnlySpan<byte> orderedTcpPayload, long timestampMs)
    {
        if (!streams.TryGetValue(streamId, out var bytes))
        {
            if (streams.Count >= 128) streams.Remove(streams.Keys.First());
            streams[streamId] = bytes = new();
        }
        if (bytes.Count + orderedTcpPayload.Length > 524288) bytes.Clear();
        bytes.AddRange(orderedTcpPayload.ToArray());
        var output = new List<CombatHit>();
        var data = bytes.ToArray(); int pos = 0;
        while (pos < data.Length)
        {
            int cursor = pos;
            if (!Var(data, ref cursor, out int declared)) { if (data.Length-pos >= 5) { pos++; continue; } break; }
            if (declared < 6 || declared > 65546) { pos++; continue; }
            if (cursor + 2 > data.Length) break;
            bool bundle=data[cursor]==255 && data[cursor+1]==255;
            int physical = PhysicalLength(declared,cursor-pos,bundle);
            if (physical < cursor-pos+2) { pos++; continue; }
            bool knownFamily = data[cursor+1] is 0x36 or 0x37 or 0x38 or 0x8d or 0x92 or 0x97 || (data[cursor]==255 && data[cursor+1]==255);
            if (!knownFamily) { pos++; continue; }
            if (pos + physical > data.Length) break;
            Parse(data.AsSpan(pos, physical), timestampMs, output, 0);
            RecordsFramed++;
            pos += physical;
        }
        if (pos > 0) bytes.RemoveRange(0, pos);
        return output;
    }

    private void Parse(ReadOnlySpan<byte> record, long time, List<CombatHit> output, int depth)
    {
        if (depth > 4) return;
        int p=0; if (!Var(record,ref p,out int declared) || p+2>record.Length) return;
        byte code=record[p++], family=record[p++];
        ushort opcode=(ushort)((code<<8)|family);
        opcodeCounts[opcode]=opcodeCounts.GetValueOrDefault(opcode)+1;
        RecordObserved?.Invoke(record,time);
        if (code==255 && family==255)
        {
            if (p+4>record.Length) return;
            int size=BinaryPrimitives.ReadInt32LittleEndian(record[p..]); p+=4;
            int compressed=declared-10;
            if(size<1 || size>1_000_000 || compressed<1 || p+compressed>record.Length) { RejectedRecords++; return; }
            var decoded=Lz4(record.Slice(p,compressed),size);
            if(decoded is null) { RejectedRecords++; return; }
            BundlesDecoded++;
            int start=0;
            while(start<decoded.Length)
            {
                if(decoded[start]==0) { start++; continue; }
                int q=start;
                if(!Var(decoded,ref q,out int n)) break;
                bool childBundle=q+1<decoded.Length && decoded[q]==255 && decoded[q+1]==255;
                int length=PhysicalLength(n,q-start,childBundle);
                if(length<q-start+2 || start+length>decoded.Length) break;
                Parse(decoded.AsSpan(start,length),time,output,depth+1); start+=length;
            }
            return;
        }
        if(family!=0x38) return;
        if(code==0x3d)
        {
            for(int count=0;count<64 && p<record.Length;count++)
            {
                int start=p,q=p;
                if(!Var(record,ref q,out int n)) break;
                bool childBundle=q+1<record.Length && record[q]==255 && record[q+1]==255;
                int size=PhysicalLength(n,q-start,childBundle);
                if(size<q-start+2 || start+size>record.Length) break;
                Parse(record.Slice(start,size),time,output,depth+1); p+=size;
            }
            return;
        }
        try
        {
            CombatHit? hit = code switch { 4 => Direct(record,ref p,time), 5=>Dot(record,ref p,time,declared), _=>null };
            if(hit is not null)
            {
                if((IsDamageSkill?.Invoke(hit.SkillId) ?? true) && (IncludeHit?.Invoke(hit) ?? true)){output.Add(hit);HitDecoded?.Invoke(hit);}
                else FilteredHits++;
            }
        }
        catch(InvalidDataException) { RejectedRecords++; Reject("truncated-combat-record"); }
    }

    private CombatHit? Reject(string reason){rejectionReasons[reason]=rejectionReasons.GetValueOrDefault(reason)+1;return null;}
    private CombatHit? Direct(ReadOnlySpan<byte> data,ref int p,long time)
    {
        int target=Read(data,ref p), sw=Read(data,ref p);
        if((sw&15)<4) return Reject("non-damage-switch");
        _=Read(data,ref p); int actor=Read(data,ref p);
        if(p+5>data.Length) throw new InvalidDataException();
        int skill=BinaryPrimitives.ReadInt32LittleEndian(data[p..]); int counter=data[p+4]; p+=5;
        int type=Read(data,ref p);
        if(type is 36 or 37 or 39 or 40 or 41 or 42) return Reject("no-damage-outcome");
        int flags=p, mode=sw&3, width=mode switch { 0=>8, 1=>13, 3=>15, _=>0 };
        if(mode==2) { int q=p+1; _=Read(data,ref q); width=10+q-(p+1); }
        if(p+width>data.Length) throw new InvalidDataException(); p+=width;
        _=Read(data,ref p); int damage=Read(data,ref p);
        int loop= p<data.Length ? Read(data,ref p):0;
        long extra=0;
        if(p<data.Length)
        {
            int count=Read(data,ref p);
            if(count is >=1 and <=10) for(int i=0;i<count && p<data.Length;i++) { int part=Read(data,ref p); if(part is >0 and <10_000_000) extra+=part; }
        }
        if(loop==0 && BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(flags+width-4,4))>16) return Reject("phantom-flags");
        if(actor<100 || target<=0 || actor==target) return Reject("invalid-or-self-entity");
        if(damage<=0 || damage>10_000_000) return Reject("invalid-damage-magnitude");
        if(type>72 || !PlausibleSkill(skill)) return Reject("invalid-skill-or-outcome");
        return new(time,actor,target,skill,damage+extra,false,counter);
    }

    private CombatHit? Dot(ReadOnlySpan<byte> data,ref int p,long time,int declared)
    {
        int target=Read(data,ref p); if(p>=data.Length) throw new InvalidDataException();
        byte form=data[p++]; int actor=Read(data,ref p), instance=Read(data,ref p);
        if(p+4>data.Length) throw new InvalidDataException();
        int skill=BinaryPrimitives.ReadInt32LittleEndian(data[p..])/100; p+=4;
        if(form==8 && p+4==data.Length) return null;
        int damage=Read(data,ref p); var pool=(target,actor,instance,skill/10000*10000);
        int q=p;
        if((form&1)!=0 && Var(data,ref q,out int amount) && q+4<=data.Length && BinaryPrimitives.ReadInt32LittleEndian(data[q..])/10000==skill/10000)
        {
            if(poolStreams.Count>8192) poolStreams.Clear(); poolStreams.Add(pool);
            (actor,target)=(target,actor); damage=amount;
        }
        else if(form is 10 or 11 && poolStreams.Contains(pool)) return null;
        if(actor<100 || target<=0 || actor==target || damage<=0 || damage>10_000_000 || !PlausibleSkill(skill)) return null;
        return new(time,actor,target,skill,damage,true,instance);
    }
    private static bool PlausibleSkill(int skill)=>skill is >=11_000_000 and <=20_000_000 or >=1_000_000 and <=5_000_000 or >=100_000 and <=200_000;
    private int PhysicalLength(int declared,int width,bool bundle)=>bundle||Framing==GameFraming.WidthAware ? declared-4+width : declared-3;
    private static int Read(ReadOnlySpan<byte> d,ref int p)=>Var(d,ref p,out int n)?n:throw new InvalidDataException();
    private static bool Var(ReadOnlySpan<byte> d,ref int p,out int value)
    {
        value=0; int initial=p;
        for(int shift=0;shift<35;shift+=7)
        {
            if(p>=d.Length) { p=initial; return false; }
            byte b=d[p++]; if(shift==28 && (b&0xf0)!=0) {p=initial;return false;}
            value|=(b&127)<<shift; if((b&128)==0) return value>=0;
        }
        p=initial; return false;
    }
    private static byte[]? Lz4(ReadOnlySpan<byte> input,int expected)
    {
        byte[] output=new byte[expected]; int i=0,o=0;
        while(i<input.Length)
        {
            int token=input[i++], literals=token>>4;
            if(literals==15) { int ext; do {if(i>=input.Length)return null; ext=input[i++]; literals+=ext;}while(ext==255); }
            if(i+literals>input.Length || o+literals>expected)return null;
            input.Slice(i,literals).CopyTo(output.AsSpan(o)); i+=literals;o+=literals;
            if(i==input.Length)return o==expected?output:null;
            if(i+2>input.Length)return null;
            int distance=input[i]|input[i+1]<<8;i+=2;
            if(distance==0 || distance>o)return null;
            int match=token&15;
            if(match==15) {int ext;do{if(i>=input.Length)return null;ext=input[i++];match+=ext;}while(ext==255);}
            match+=4;if(o+match>expected)return null;
            for(int k=0;k<match;k++) {output[o]=output[o-distance];o++;}
        }
        return o==expected?output:null;
    }
}

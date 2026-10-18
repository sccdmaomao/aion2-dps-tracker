using System.Buffers.Binary;
namespace LocalDps.Protocol;

public sealed record TargetDeath(int TargetId,long TimestampMs)
{
    public long? AliveAgainMs {get;internal set;}
}

// The observed 00 8D record reports entity, 02 01 00, HP:u32, and an
// opaque four-byte trailer. Removal (42 36) alone never establishes death.
public sealed class TargetHealthDecoder
{
    readonly Dictionary<int,(uint Hp,long Time)> health=new();
    readonly List<TargetDeath> deaths=new();
    public IReadOnlyList<TargetDeath> Deaths=>deaths;
    public long UpdatesSeen {get;private set;}
    public void Reset(){health.Clear();deaths.Clear();UpdatesSeen=0;}
    public void Observe(ReadOnlySpan<byte> record,long timestamp)
    {
        int p=0;
        if(!Var(record,ref p,out int declared)||declared-4+p!=record.Length||p+2>record.Length)return;
        if(record[p++]!=0||record[p++]!=0x8d||!Var(record,ref p,out int entity)||entity<=0)return;
        if(record.Length-p!=11||record[p]!=2||record[p+1]!=1||record[p+2]!=0)return;
        uint hp=BinaryPrimitives.ReadUInt32LittleEndian(record[(p+3)..]);
        if(hp>500_000_000)return; // Larger/other layouts need separate validation.
        if(health.TryGetValue(entity,out var previous))
        {
            if(timestamp<previous.Time)return;
            if(hp>0&&previous.Hp==0)
            {
                var previousDeath=deaths.LastOrDefault(d=>d.TargetId==entity&&d.AliveAgainMs is null);
                if(previousDeath is not null)previousDeath.AliveAgainMs=timestamp;
            }
            if(hp==0&&previous.Hp>0&&timestamp-previous.Time<=30_000)
            {
                if(deaths.Count>=4096)deaths.RemoveAt(0);
                deaths.Add(new(entity,timestamp));
            }
        }
        else if(health.Count>=8192)health.Remove(health.MinBy(x=>x.Value.Time).Key);
        health[entity]=(hp,timestamp);UpdatesSeen++;
    }
    public TargetDeath? FindDeath(int targetId,long firstHitMs,long lastHitMs)=>
        // The recording delivers one final hit 47ms after zero HP. Allow a
        // small receive-time skew, while later positive HP invalidates reuse.
        deaths.FirstOrDefault(d=>d.TargetId==targetId&&d.TimestampMs>=firstHitMs-250&&d.TimestampMs>=lastHitMs-250&&d.TimestampMs<=lastHitMs+15_000&&(d.AliveAgainMs is null||d.AliveAgainMs>lastHitMs));
    static bool Var(ReadOnlySpan<byte> bytes,ref int p,out int value)
    {
        value=0;
        for(int i=0;i<5&&p<bytes.Length;i++){byte b=bytes[p++];if(i==4&&(b&0xf8)!=0)return false;value|=(b&127)<<(i*7);if((b&128)==0)return true;}
        return false;
    }
}

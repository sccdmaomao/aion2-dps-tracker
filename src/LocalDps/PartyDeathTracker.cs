using LocalDps.Protocol;

namespace LocalDps;

/// <summary>Observed, verified-party deaths; health data never grants membership.</summary>
public sealed class PartyDeathTracker(CombatScope scope,EncounterTracker encounters)
{
    sealed record Health(string Name,int Hp,long Time);
    sealed record Death(PlayerRow Player,long Time);
    readonly Dictionary<int,Health> health=new();
    readonly List<Death> deaths=new();
    long membership=-1;
    public int ObservedDeaths=>deaths.Count;
    public void Reset(){health.Clear();deaths.Clear();membership=-1;}

    public void Observe(ReadOnlySpan<byte> record,long timestamp)
    {
        int p=0;
        if(!Var(record,ref p,out int declared)||declared-4+p!=record.Length||p+2>record.Length||record[p++]!=0x1b||record[p++]!=0x92)return;
        if(!Var(record,ref p,out int actor)||actor<=0||!Var(record,ref p,out int hp)||!Var(record,ref p,out int max))return;
        // Observed framed 1B 92: entity, current HP, max HP (varints), then
        // 25 opaque bytes. Other shapes need evidence before being accepted.
        if(record.Length-p!=25||max is <=0 or >50_000_000||hp>max)return;
        if(membership!=scope.MembershipVersion){health.Clear();membership=scope.MembershipVersion;}
        foreach(int id in health.Keys.Where(id=>!scope.IncludesActor(id)).ToArray())health.Remove(id);
        if(!scope.IncludesActor(actor))return;
        string name=scope.Name(actor);
        if(health.TryGetValue(actor,out var previous)&&previous.Name==name)
        {
            if(timestamp<previous.Time)return;
            if(hp==0&&previous.Hp>0&&timestamp-previous.Time<=30_000)
            {
                var player=scope.DisplayRows(Array.Empty<PlayerRow>()).FirstOrDefault(r=>r.ActorId==actor&&r.Actor==name);
                if(player is not null)
                {
                    if(deaths.Count>=4096)deaths.RemoveAt(0);
                    deaths.Add(new(player,timestamp));
                }
            }
        }
        health[actor]=new(name,hp,timestamp);
    }

    public PlayerRow[] Describe(PlayerRow[] rows,EncounterTracker.Encounter? encounter)
    {
        var selected=encounter is null?Array.Empty<Death>():deaths.Where(d=>d.Time>=encounter.FirstMs&&d.Time<=encounters.EndMs(encounter)).ToArray();
        var result=rows.ToList();
        // Retain a healer/idle member who died without dealing damage, even
        // after they leave. Historical rows do not authorize future capture.
        foreach(var death in selected)
            if(!result.Any(r=>r.ActorId==death.Player.ActorId&&r.Actor==death.Player.Actor))
            {
                int unresolved=result.FindIndex(r=>r.ActorId is null&&r.Actor==death.Player.Actor);
                var historical=death.Player with {Status="Death recorded"};
                if(unresolved>=0)result[unresolved]=historical;else result.Add(historical);
            }
        return result.Select(row=>row with {Deaths=row.ActorId is null?null:selected.Count(d=>d.Player.ActorId==row.ActorId&&d.Player.Actor==row.Actor)})
            .OrderByDescending(row=>row.Damage).ToArray();
    }
    static bool Var(ReadOnlySpan<byte> data,ref int p,out int value)
    {
        value=0;
        for(int shift=0;shift<35&&p<data.Length;shift+=7){byte b=data[p++];if(shift==28&&(b&0xf8)!=0)return false;value|=(b&127)<<shift;if((b&128)==0)return true;}
        return false;
    }
}

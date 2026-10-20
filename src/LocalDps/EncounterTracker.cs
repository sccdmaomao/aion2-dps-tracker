using LocalDps.Protocol;
namespace LocalDps;

// Event timestamps, rather than callback order, assign hits to fights. Identity
// recovery can release a burst of older hits after a later fight has begun.
public sealed class EncounterTracker
{
    public const long IdleTimeoutMs=15_000;
    public const int HistoryLimit=50;
    public sealed class Encounter(int id,Meter meter)
    {
        public int Id {get;}=id;
        public Meter Meter {get;}=meter;
        internal Dictionary<int,PlayerRow> Players {get;}=new();
        public long FirstMs=>Meter.FirstMs!.Value;
        public long LastMs=>Meter.LastMs!.Value;
    }
    readonly List<Encounter> encounters=new();
    int nextId=1;
    long? oldestRetainedMs;
    public long? ClockMs {get;private set;}
    public bool Running {get;private set;}
    public Func<int,string> DisplayName {get;set;}=id=>$"Unknown entity {id}";
    public Func<int,string> TargetName {get;set;}=id=>$"Unknown target {id}";
    public Func<PlayerRow[],PlayerRow[]> DescribePlayers {get;set;}=rows=>rows;
    public Func<int,long,long,TargetDeath?> FindTargetDeath {get;set;}=(_,_,_)=>null;
    public IReadOnlyList<Encounter> Encounters=>encounters;
    public Encounter? Latest=>encounters.LastOrDefault();
    public void AdvanceTo(long timestampMs){ClockMs=ClockMs.HasValue?Math.Max(ClockMs.Value,timestampMs):timestampMs;}
    public void Start()=>Running=true;
    public void Stop()=>Running=false;
    public void Add(CombatHit hit)
    {
        if(hit.Damage<=0||(oldestRetainedMs.HasValue&&hit.TimestampMs<oldestRetainedMs.Value))return;
        AdvanceTo(hit.TimestampMs);
        var matches=encounters.Where(e=>hit.TimestampMs>e.FirstMs-IdleTimeoutMs&&hit.TimestampMs<e.LastMs+IdleTimeoutMs&&CanJoin(e,hit)).ToArray();
        // Identity recovery may release old damage after the next fight starts.
        // A confirmed end is a boundary, not an idle gap that a late hit bridges.
        if(matches.FirstOrDefault(e=>DefeatedAt(e) is not null) is {} completed)matches=[completed];
        Encounter encounter;
        if(matches.Length==0)
        {
            encounter=new(nextId++,new Meter{DisplayName=id=>DisplayName(id),TargetName=id=>TargetName(id)});
            encounters.Add(encounter);
        }
        else
        {
            encounter=matches[0];
            foreach(var other in matches.Skip(1))
            {
                encounter.Meter.Merge(other.Meter);
                foreach(var (id,row) in other.Players)encounter.Players.TryAdd(id,row);
                encounters.Remove(other);
            }
        }
        encounter.Meter.Add(hit);
        // Retain metadata when the hit is accepted, before party membership or
        // transient entity mappings change. These rows never grant capture scope.
        foreach(var row in DescribePlayers(encounter.Meter.View(actorId:hit.ActorId).Players))
            if(row.ActorId==hit.ActorId&&row.Hits>0)encounter.Players[hit.ActorId]=row;
        encounters.Sort((a,b)=>a.FirstMs.CompareTo(b.FirstMs));
        while(encounters.Count>HistoryLimit){oldestRetainedMs=encounters[0].LastMs+IdleTimeoutMs;encounters.RemoveAt(0);}
    }
    TargetDeath? Death(Encounter encounter,int targetId)=>encounter.Meter.TargetTimes(targetId) is {} times
        ?FindTargetDeath(targetId,times.First,times.Last):null;
    long? DefeatedAt(Encounter encounter,int? targetId=null)
    {
        if(targetId is int selected)return Death(encounter,selected)?.TimestampMs;
        var ids=encounter.Meter.TargetIds;
        var deaths=ids.Select(id=>Death(encounter,id)).ToArray();
        return deaths.Length>0&&deaths.All(d=>d is not null)?deaths.Max(d=>d!.TimestampMs):null;
    }
    bool CanJoin(Encounter encounter,CombatHit hit)
    {
        if(DefeatedAt(encounter) is not long end||hit.TimestampMs<=end)return true;
        // Final damage can arrive just after zero HP. It must belong to a
        // defeated target's same life; a new target starts a fresh fight.
        var death=Death(encounter,hit.TargetId);
        return death is not null&&hit.TimestampMs<=death.TimestampMs+250&&
            (death.AliveAgainMs is null||hit.TimestampMs<death.AliveAgainMs);
    }
    public long EndMs(Encounter encounter,int? targetId=null)=>Math.Clamp(ClockMs??encounter.LastMs,encounter.FirstMs,
        Math.Max(encounter.FirstMs,DefeatedAt(encounter,targetId)??encounter.LastMs+IdleTimeoutMs));
    public double Seconds(Encounter encounter,int? targetId=null)=>(EndMs(encounter,targetId)-encounter.FirstMs)/1000.0;
    public string State(Encounter encounter,int? targetId=null)=>DefeatedAt(encounter,targetId) is long death&&ClockMs>=death?"Defeated"
        :ClockMs>=encounter.LastMs+IdleTimeoutMs?"Complete":Running?"In combat":"Stopped";
    public MeterView View(Encounter? encounter,int? targetId=null,IReadOnlySet<int>? allowedActors=null)
    {
        if(encounter is null)return new([],[],0,0,0,null,null);
        var view=encounter.Meter.View(targetId:targetId,allowedActors:allowedActors,elapsedSeconds:Seconds(encounter,targetId));
        return view with {Players=view.Players.Select(row=>row.ActorId is int id&&encounter.Players.TryGetValue(id,out var saved)
            ?row with {Actor=saved.Actor,ItemLevel=saved.ItemLevel,CombatPower=saved.CombatPower}:row).ToArray()};
    }
    public void Reset(){encounters.Clear();nextId=1;oldestRetainedMs=null;ClockMs=null;Running=false;}
}

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
    public IReadOnlyList<Encounter> Encounters=>encounters;
    public Encounter? Latest=>encounters.LastOrDefault();
    public void AdvanceTo(long timestampMs){ClockMs=ClockMs.HasValue?Math.Max(ClockMs.Value,timestampMs):timestampMs;}
    public void Start()=>Running=true;
    public void Stop()=>Running=false;
    public void Add(CombatHit hit)
    {
        if(hit.Damage<=0||(oldestRetainedMs.HasValue&&hit.TimestampMs<oldestRetainedMs.Value))return;
        AdvanceTo(hit.TimestampMs);
        var matches=encounters.Where(e=>hit.TimestampMs>e.FirstMs-IdleTimeoutMs&&hit.TimestampMs<e.LastMs+IdleTimeoutMs).ToArray();
        Encounter encounter;
        if(matches.Length==0)
        {
            encounter=new(nextId++,new Meter{DisplayName=id=>DisplayName(id),TargetName=id=>TargetName(id)});
            encounters.Add(encounter);
        }
        else
        {
            encounter=matches[0];
            foreach(var other in matches.Skip(1)){encounter.Meter.Merge(other.Meter);encounters.Remove(other);}
        }
        encounter.Meter.Add(hit);
        encounters.Sort((a,b)=>a.FirstMs.CompareTo(b.FirstMs));
        while(encounters.Count>HistoryLimit){oldestRetainedMs=encounters[0].LastMs+IdleTimeoutMs;encounters.RemoveAt(0);}
    }
    public long EndMs(Encounter encounter)=>Math.Clamp(ClockMs??encounter.LastMs,encounter.FirstMs,encounter.LastMs+IdleTimeoutMs);
    public double Seconds(Encounter encounter)=>(EndMs(encounter)-encounter.FirstMs)/1000.0;
    public string State(Encounter encounter)=>ClockMs>=encounter.LastMs+IdleTimeoutMs?"Complete":Running?"In combat":"Stopped";
    public MeterView View(Encounter? encounter,int? targetId=null,IReadOnlySet<int>? allowedActors=null)=>
        encounter is null?new([],[],0,0,0,null,null):encounter.Meter.View(targetId:targetId,allowedActors:allowedActors,elapsedSeconds:Seconds(encounter));
    public void Reset(){encounters.Clear();nextId=1;oldestRetainedMs=null;ClockMs=null;Running=false;}
}

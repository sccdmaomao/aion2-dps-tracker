using LocalDps.Protocol;
namespace LocalDps;
public sealed record PlayerRow(int? ActorId,string Actor,long Damage,double Dps,int Hits,double Share,string Status="",int? ItemLevel=null,int? CombatPower=null)
{
    public string DisplayActor=>$"{Actor} ({ItemLevel?.ToString("N0")??"—"} / {CombatPower?.ToString("N0")??"—"})";
    public bool DpsAvailable=>ActorId.HasValue;
    public string DpsText=>DpsAvailable?Dps.ToString("N0"):"—";
    public string ShareText=>DpsAvailable?$"{Share:N1}% damage":"—";
}
public sealed record SkillRow(int ActorId,int SkillId,long Damage,int Hits);
public sealed record TargetRow(int TargetId,string Target,long Damage,double Dps,int Hits,double Seconds);
public sealed record MeterView(PlayerRow[] Players,SkillRow[] Skills,long Damage,int Hits,double Seconds,long? FirstMs,long? LastMs);
public sealed class Meter
{
    public Func<int,string> DisplayName {get;set;}=id=>$"Unknown entity {id}";
    public Func<int,string> TargetName {get;set;}=id=>$"Unknown target {id}";
    sealed class Contribution { public long Damage,First,Last;public int Hits; }
    readonly Dictionary<(int Actor,int Target,int Skill),Contribution> contributions=new();
    readonly Dictionary<int,(long Damage,int Hits)> actors=new();
    readonly Dictionary<(int,int),(long Damage,int Hits)> skills=new();
    public long? FirstMs {get;private set;}
    public long? LastMs {get;private set;}
    public long TotalDamage {get;private set;}
    public int TotalHits {get;private set;}
    public double DurationSeconds => FirstMs.HasValue ? Math.Max(1,(LastMs!.Value-FirstMs.Value)/1000.0) : 0;
    public void Add(CombatHit hit)
    {
        if(hit.Damage<=0) return;
        var contributionKey=(hit.ActorId,hit.TargetId,hit.SkillId);
        if(!contributions.TryGetValue(contributionKey,out var contribution))contributions[contributionKey]=contribution=new Contribution{First=hit.TimestampMs,Last=hit.TimestampMs};
        contribution.Damage=checked(contribution.Damage+hit.Damage);contribution.Hits++;
        contribution.First=Math.Min(contribution.First,hit.TimestampMs);contribution.Last=Math.Max(contribution.Last,hit.TimestampMs);
        FirstMs=FirstMs.HasValue?Math.Min(FirstMs.Value,hit.TimestampMs):hit.TimestampMs;
        LastMs=LastMs.HasValue?Math.Max(LastMs.Value,hit.TimestampMs):hit.TimestampMs;
        var old=actors.GetValueOrDefault(hit.ActorId);
        actors[hit.ActorId]=(checked(old.Damage+hit.Damage),old.Hits+1);
        var key=(hit.ActorId,hit.SkillId); var sk=skills.GetValueOrDefault(key);
        skills[key]=(checked(sk.Damage+hit.Damage),sk.Hits+1);
        TotalDamage=checked(TotalDamage+hit.Damage); TotalHits++;
    }
    public PlayerRow[] Players()=>actors.Select(p=>new PlayerRow(p.Key,DisplayName(p.Key),p.Value.Damage,p.Value.Damage/DurationSeconds,p.Value.Hits,TotalDamage==0?0:p.Value.Damage*100.0/TotalDamage)).OrderByDescending(p=>p.Damage).ToArray();
    public SkillRow[] Skills()=>skills.Select(p=>new SkillRow(p.Key.Item1,p.Key.Item2,p.Value.Damage,p.Value.Hits)).OrderByDescending(p=>p.Damage).ToArray();
    public int[] ActorIds=>actors.Keys.Order().ToArray();
    public int[] TargetIds=>contributions.Keys.Select(x=>x.Target).Distinct().Order().ToArray();
    public (long First,long Last)? TargetTimes(int targetId)
    {
        var selected=contributions.Where(p=>p.Key.Target==targetId).Select(p=>p.Value).ToArray();
        return selected.Length==0?null:(selected.Min(c=>c.First),selected.Max(c=>c.Last));
    }
    public TargetRow[] Targets(int? actorId=null,IReadOnlySet<int>? allowedActors=null,double? elapsedSeconds=null)
    {
        return contributions.Where(p=>(!actorId.HasValue||p.Key.Actor==actorId)&&(allowedActors is null||allowedActors.Contains(p.Key.Actor)))
            .GroupBy(p=>p.Key.Target).Select(g=>
            {
                long damage=g.Sum(p=>p.Value.Damage);double seconds=elapsedSeconds??Math.Max(1,(g.Max(p=>p.Value.Last)-g.Min(p=>p.Value.First))/1000.0);
                return new TargetRow(g.Key,TargetName(g.Key),damage,damage/Math.Max(1,seconds),g.Sum(p=>p.Value.Hits),seconds);
            }).OrderByDescending(t=>t.Damage).ToArray();
    }
    public MeterView View(int? actorId=null,int? targetId=null,IReadOnlySet<int>? allowedActors=null,double? elapsedSeconds=null)
    {
        var selected=contributions.Where(p=>(!actorId.HasValue||p.Key.Actor==actorId)&&(!targetId.HasValue||p.Key.Target==targetId)&&(allowedActors is null||allowedActors.Contains(p.Key.Actor))).ToArray();
        if(selected.Length==0)return new(Array.Empty<PlayerRow>(),Array.Empty<SkillRow>(),0,0,elapsedSeconds??0,null,null);
        long first=selected.Min(p=>p.Value.First),last=selected.Max(p=>p.Value.Last),total=selected.Sum(p=>p.Value.Damage);
        double seconds=elapsedSeconds??Math.Max(1,(last-first)/1000.0);
        var players=selected.GroupBy(p=>p.Key.Actor).Select(g=>new PlayerRow(g.Key,DisplayName(g.Key),g.Sum(p=>p.Value.Damage),g.Sum(p=>p.Value.Damage)/Math.Max(1,seconds),g.Sum(p=>p.Value.Hits),g.Sum(p=>p.Value.Damage)*100.0/total)).OrderByDescending(p=>p.Damage).ToArray();
        var bySkill=selected.GroupBy(p=>(p.Key.Actor,p.Key.Skill)).Select(g=>new SkillRow(g.Key.Actor,g.Key.Skill,g.Sum(p=>p.Value.Damage),g.Sum(p=>p.Value.Hits))).OrderByDescending(p=>p.Damage).ToArray();
        return new(players,bySkill,total,selected.Sum(p=>p.Value.Hits),seconds,first,last);
    }
    public void Merge(Meter other)
    {
        foreach(var (key,value) in other.contributions)
        {
            if(!contributions.TryGetValue(key,out var current))contributions[key]=current=new Contribution{First=value.First,Last=value.Last};
            current.Damage=checked(current.Damage+value.Damage);current.Hits+=value.Hits;
            current.First=Math.Min(current.First,value.First);current.Last=Math.Max(current.Last,value.Last);
        }
        foreach(var (key,value) in other.actors){var old=actors.GetValueOrDefault(key);actors[key]=(checked(old.Damage+value.Damage),old.Hits+value.Hits);}
        foreach(var (key,value) in other.skills){var old=skills.GetValueOrDefault(key);skills[key]=(checked(old.Damage+value.Damage),old.Hits+value.Hits);}
        if(other.FirstMs.HasValue)FirstMs=FirstMs.HasValue?Math.Min(FirstMs.Value,other.FirstMs.Value):other.FirstMs;
        if(other.LastMs.HasValue)LastMs=LastMs.HasValue?Math.Max(LastMs.Value,other.LastMs.Value):other.LastMs;
        TotalDamage=checked(TotalDamage+other.TotalDamage);TotalHits+=other.TotalHits;
    }
    public void Reset(){actors.Clear();skills.Clear();contributions.Clear();FirstMs=LastMs=null;TotalDamage=0;TotalHits=0;}
}

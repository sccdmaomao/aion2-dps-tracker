using LocalDps.Protocol;
namespace LocalDps;

/// <summary>Names are observations; self and party membership require separate proof.</summary>
public sealed class CombatScope
{
    public EntityIdentityDecoder Identity {get;}=new();
    public PartyRosterDecoder Party {get;}=new();
    public CharacterStatsDecoder Statistics {get;}=new();
    private string? confirmedSelfName;
    private int? partySetSelfId;
    private bool partyInferenceInvalidated;
    private readonly Dictionary<int,long> partyStatusActors=new();
    public string? ConfirmedSelfName {get=>confirmedSelfName;set{if(value!=confirmedSelfName){partySetSelfId=null;partyInferenceInvalidated=false;partyStatusActors.Clear();}confirmedSelfName=value;}}
    public string? SelfProof=>Identity.LocalPlayerProof??(partySetSelfId.HasValue?"Unique remaining actor in a complete five-player party status stream after all four current teammates were independently mapped.":LocalPlayerId.HasValue?"Unique observed character-name match.":null);
    public long ExcludedHits {get;private set;}
    private HashSet<(ulong? Character,int? Server)>? stableChunkMembers;
    private HashSet<(ulong?,int?)> previousMembers=new();
    public long MembershipVersion {get;private set;}
    private HashSet<(ulong?,int?)> CurrentMembers()=>Party.IsAuthoritative?Party.Members.Select(m=>(m.CharacterId,m.ServerId)).ToHashSet():new();
    public void BeginChunk()=>stableChunkMembers=CurrentMembers();
    public void EndChunk()=>stableChunkMembers=null;
    public bool CanAwaitPartyIdentity(CombatHit hit)=>Party.RecoveredAtStartup&&Party.Members.Count==4&&stableChunkMembers is {Count:4};
    public bool ReleaseAwaitingHit(CombatHit hit)
    {
        if(!AllowedActors().Contains(hit.ActorId))return false;
        ExcludedHits=Math.Max(0,ExcludedHits-1);return true;
    }
    public int? LocalPlayerId
    {
        get
        {
            if(Identity.LocalPlayerId is int proven)return proven;
            if(partySetSelfId is int partySelf)return partySelf;
            if(string.IsNullOrWhiteSpace(ConfirmedSelfName))return null;
            var matches=Identity.Names.Where(p=>string.Equals(p.Value,ConfirmedSelfName,StringComparison.Ordinal)).Take(2).ToArray();
            return matches.Length==1?matches[0].Key:null;
        }
    }
    public HashSet<int> AllowedActors()
    {
        var ids=new HashSet<int>();if(LocalPlayerId is int self)ids.Add(self);
        if(Party.IsAuthoritative)foreach(var member in Party.Members)
        {
            var matches=Identity.Names.Where(p=>string.Equals(p.Value,member.Name,StringComparison.Ordinal)).Take(2).ToArray();
            if(matches.Length==1)ids.Add(matches[0].Key);
        }
        return ids;
    }
    public bool Include(CombatHit hit)
    {
        bool allowed=AllowedActors().Contains(hit.ActorId);
        if(allowed&&hit.ActorId!=LocalPlayerId&&stableChunkMembers is not null)
            allowed=Party.Members.Any(m=>stableChunkMembers.Contains((m.CharacterId,m.ServerId))&&Identity.Names.TryGetValue(hit.ActorId,out var name)&&name==m.Name);
        if(!allowed)ExcludedHits++;return allowed;
    }
    public string Name(int id)=>Identity.Names.TryGetValue(id,out string? name)?name:id==partySetSelfId&&ConfirmedSelfName is not null?ConfirmedSelfName:$"Unknown entity {id}";
    public string Label(int id)=>Name(id);
    public PlayerRow[] DisplayRows(PlayerRow[] damageRows,int? selectedActor=null)
    {
        var rows=new List<PlayerRow>();
        int? self=LocalPlayerId;
        string? selfName=self.HasValue?Name(self.Value):ConfirmedSelfName;
        void Add(int? id,string name,string state,CharacterStats? stats)
        {
            var damage=id.HasValue?damageRows.FirstOrDefault(r=>r.ActorId==id):null;
            if(selectedActor.HasValue&&id!=selectedActor)state="Outside selected attacker";
            else if(damage is {Hits:>0})state="Damage captured";
            var row=damage is null?new PlayerRow(id,name,0,0,0,0,state):damage with {Actor=name,Status=state};
            rows.Add(row with {ItemLevel=stats?.ItemLevel,CombatPower=stats?.CombatPower});
        }
        if(!string.IsNullOrWhiteSpace(selfName))Add(self,selfName,self.HasValue?"No hits captured":"Waiting for your identity",Statistics.ForSelf(Identity));
        if(Party.IsAuthoritative)foreach(var member in Party.Members)
        {
            if(member.Name==selfName)continue;
            var matches=Identity.Names.Where(p=>p.Value==member.Name).Take(2).ToArray();
            int? id=matches.Length==1?matches[0].Key:null;
            string state=matches.Length>1?"Ambiguous identity":id.HasValue?"No hits captured":"Combat identity not received";
            if(member.Name=="Unknown party member")state="Waiting for member name";
            Add(id,member.Name,state,Statistics.ForMember(member));
        }
        return rows.OrderByDescending(r=>r.Damage).ToArray();
    }
    public string Status=>LocalPlayerId is not int self?$"Waiting for {(string.IsNullOrWhiteSpace(ConfirmedSelfName)?"your character":ConfirmedSelfName)} identity · {AllowedActors().Count} verified teammate(s) mapped":$"Tracking {Name(self)} · "+(Party.IsAuthoritative?$"Party · {Party.Members.Count(m=>m.Name!=Name(self))} detected · {Math.Max(0,AllowedActors().Count-1)} mapped": "Only you · waiting for a verified party roster");
    public void Observe(ReadOnlySpan<byte> record,long timestamp)
    {
        Identity.Observe(record,timestamp);
        Statistics.Observe(record,Identity);
        if(Identity.LocalPlayerId is int local&&Identity.Names.TryGetValue(local,out var ownName))ConfirmedSelfName=ownName;
        Party.Observe(record,timestamp);stableChunkMembers?.IntersectWith(CurrentMembers());
        if(partySetSelfId is int inferred&&Identity.Names.TryGetValue(inferred,out var observed)&&observed!=ConfirmedSelfName){partySetSelfId=null;partyInferenceInvalidated=true;partyStatusActors.Clear();}
        var current=CurrentMembers();if(!current.SetEquals(previousMembers)){MembershipVersion++;previousMembers=current;}
        ObservePartyActor(record,timestamp);TryCompletePartyIdentity(timestamp);
    }
    private void ObservePartyActor(ReadOnlySpan<byte> record,long time)
    {
        int p=0;while(p<record.Length&&(record[p++]&128)!=0){}if(p+2>record.Length||record[p++]!=0x0e||record[p++]!=0x92)return;
        int actor=0,shift=0;
        while(p<record.Length&&shift<28){byte b=record[p++];actor|=(b&127)<<shift;if((b&128)==0)break;shift+=7;}
        if(actor is <100 or >999999||record.Length-p!=5||record[p]!=1)return;
        uint skill=System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(record[(p+1)..]);
        if(skill is <1000000 or >1000000000)return;
        if(partyStatusActors.Count>=16&&!partyStatusActors.ContainsKey(actor))partyStatusActors.Remove(partyStatusActors.MinBy(a=>a.Value).Key);
        partyStatusActors[actor]=time;
    }
    private void TryCompletePartyIdentity(long time)
    {
        if(partyInferenceInvalidated||LocalPlayerId.HasValue||!Party.RecoveredAtStartup||Party.Members.Count!=4||string.IsNullOrWhiteSpace(ConfirmedSelfName))return;
        var own=Party.KnownNames.Where(n=>n.Name==ConfirmedSelfName).Take(2).ToArray();
        if(own.Length!=1||Party.Members.Any(m=>m.CharacterId==own[0].CharacterId&&m.ServerId==own[0].ServerId))return;
        var peers=new HashSet<int>();
        foreach(var member in Party.Members)
        {
            var ids=Identity.Names.Where(n=>n.Value==member.Name).Take(2).ToArray();
            if(ids.Length!=1||!peers.Add(ids[0].Key))return;
        }
        var active=partyStatusActors.Where(a=>time-a.Value is >=0 and <=30000).Select(a=>a.Key).ToHashSet();
        if(active.Count!=5||!peers.IsSubsetOf(active))return;
        int candidate=active.Except(peers).Single();
        if(Identity.Names.TryGetValue(candidate,out var other)&&other!=ConfirmedSelfName)return;
        partySetSelfId=candidate;
    }
    public void Reset(){Identity.Reset();Statistics.Reset();Party.Reset();ExcludedHits=0;stableChunkMembers=null;partySetSelfId=null;partyInferenceInvalidated=false;partyStatusActors.Clear();previousMembers.Clear();MembershipVersion++;}
}

using LocalDps.Protocol;
namespace LocalDps;

/// <summary>Holds recent hits for confirmed party members whose combat IDs arrive later.</summary>
public sealed class PartyHitAttribution(CombatScope scope,Action<CombatHit> accept)
{
    private sealed record Awaiting(CombatHit Hit,long Membership,bool CanBeSelf);
    private readonly Queue<Awaiting> awaiting=new();
    public int PendingCount=>awaiting.Count;
    public void Reset()=>awaiting.Clear();
    public void Add(CombatHit hit)
    {
        if(scope.Include(hit)){accept(hit);return;}
        if(!scope.CanAwaitPartyIdentity)return;
        if(awaiting.Count>=10000)awaiting.Dequeue();
        awaiting.Enqueue(new(hit,scope.ChunkMembershipVersion,scope.CanAwaitSelfIdentity));
    }
    public void Flush(long now)
    {
        int count=awaiting.Count;
        if(count==0)return;
        int? self=scope.LocalPlayerId;
        var peers=scope.PartyActorMembershipVersions(self);
        while(count-->0)
        {
            var item=awaiting.Dequeue();
            if(item.Membership<scope.LastMembershipRemovalVersion||now-item.Hit.TimestampMs>120000)continue;
            bool verifiedPeer=peers.TryGetValue(item.Hit.ActorId,out long joined)&&joined<=item.Membership;
            bool verifiedSelf=item.CanBeSelf&&item.Membership==scope.MembershipVersion&&self==item.Hit.ActorId;
            if(verifiedPeer||verifiedSelf)
            {
                scope.RecordRecoveredHit();
                accept(item.Hit);
            }
            else awaiting.Enqueue(item);
        }
    }
}

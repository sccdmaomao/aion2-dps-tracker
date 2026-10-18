using LocalDps.Protocol;
namespace LocalDps;

/// <summary>Holds recent candidates within one full-party epoch; counts only subsequently verified actors.</summary>
public sealed class PartyHitAttribution(CombatScope scope,Action<CombatHit> accept)
{
    private sealed record Awaiting(CombatHit Hit,long Membership);
    private readonly Queue<Awaiting> awaiting=new();
    public int PendingCount=>awaiting.Count;
    public void Reset()=>awaiting.Clear();
    public void Add(CombatHit hit)
    {
        if(scope.Include(hit)){accept(hit);return;}
        if(!scope.CanAwaitPartyIdentity(hit))return;
        if(awaiting.Count>=10000)awaiting.Dequeue();
        awaiting.Enqueue(new(hit,scope.MembershipVersion));
    }
    public void Flush(long now)
    {
        int count=awaiting.Count;
        while(count-->0)
        {
            var item=awaiting.Dequeue();
            if(item.Membership!=scope.MembershipVersion||now-item.Hit.TimestampMs>120000)continue;
            if(scope.ReleaseAwaitingHit(item.Hit))accept(item.Hit);else awaiting.Enqueue(item);
        }
    }
}

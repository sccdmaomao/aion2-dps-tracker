using System.Text;
using LocalDps.Protocol;
namespace LocalDps;
public static class ScopeSelfTests
{
    public static void Run()
    {
        byte[] Var(int n){var b=new List<byte>();do{byte x=(byte)(n&127);n>>=7;b.Add((byte)(x|(n>0?128:0)));}while(n>0);return b.ToArray();}
        byte[] Named(byte op,int id,string name){var n=Encoding.UTF8.GetBytes(name);var body=new byte[]{op,0x36}.Concat(Var(id)).Concat(new byte[]{0,0,0,0,7,(byte)n.Length}).Concat(n).ToArray();return Var(body.Length+4).Concat(body).ToArray();}
        var scope=new CombatScope();var hit=new CombatHit(1,200,999,11010001,50,false);
        Require(!scope.Include(hit),"unknown self must block damage");
        scope.Observe(Named(0x44,300,"Stranger"),1);
        Require(scope.Name(300)=="Stranger"&&!scope.Include(hit with{ActorId=300}),"nearby name must not imply membership");
        scope.Observe(Named(0x33,200,"TestHéro"),2);
        Require(scope.LocalPlayerId==200&&scope.Include(hit),"proven local character included");
        Require(scope.ConfirmedSelfName=="TestHéro","local-character message automatically supplies fallback name");
        Require(!scope.Include(hit with{ActorId=300}),"nearby damage blocked while self idle");
        scope.Observe(new byte[]{9,2,0x97,0,0,0},3);
        Require(!scope.Party.IsAuthoritative&&!scope.Include(hit with{ActorId=300}),"room roster is not active party proof");
        var meter=new Meter{DisplayName=scope.Label};meter.Add(hit);meter.Add(hit with{ActorId=300,Damage=10000});
        var view=meter.View(allowedActors:scope.AllowedActors());
        Require(view.Damage==50&&view.Players.Single().Actor=="TestHéro (you)","display and totals exclude unrelated actors");
        scope.Reset();Require(!scope.Include(hit),"connection reset invalidates identity");
        scope.ConfirmedSelfName="Chosen";scope.Observe(Named(0x44,200,"Chosen"),4);
        Require(scope.LocalPlayerId==200,"explicit self name may match one observed entity");
        scope.Observe(Named(0x44,201,"Chosen"),5);
        Require(scope.LocalPlayerId is null,"duplicate display names must stay ambiguous");
        scope.Reset();scope.ConfirmedSelfName="勇者";
        scope.Observe(Named(0x44,200,"勇者"),6);scope.Observe(Named(0x44,300,"Stranger"),6);
        Require(scope.Include(hit)&&!scope.Include(hit with{ActorId=300}),"confirmed Chinese self name admits only self");
        Require(scope.AllowedActors().SetEquals(new[]{200}),"distant unseen party member cannot invent an entity ID");
        scope.Observe(Named(0x44,400,"隊友"),7);
        Require(!scope.Include(hit with{ActorId=400}),"observed familiar name cannot establish party membership");
        TrackingProfile.Create("勇者").Apply(scope);Require(!scope.Include(hit with{ActorId=400}),"profile cannot grant party membership");
        var joinBody=new byte[150];joinBody[0]=3;joinBody[1]=2;
        BitConverter.GetBytes(16296).CopyTo(joinBody,2);BitConverter.GetBytes((ushort)1101).CopyTo(joinBody,6);BitConverter.GetBytes((ushort)1101).CopyTo(joinBody,8);
        joinBody[10]=36;Encoding.ASCII.GetBytes("00000000-0000-0000-0000-000000000001").CopyTo(joinBody,11);
        joinBody[47]=1;BitConverter.GetBytes((ushort)1102).CopyTo(joinBody,53);joinBody[55]=6;Encoding.UTF8.GetBytes("隊友").CopyTo(joinBody,56);joinBody[62]=9;
        var join=Var(joinBody.Length+6).Concat(new byte[]{0x0d,0x92}).Concat(joinBody).ToArray();
        var independent=new CombatScope{ConfirmedSelfName="勇者"};independent.Observe(join,7);independent.Observe(Named(0x44,400,"隊友"),8);
        Require(independent.LocalPlayerId is null&&independent.Include(hit with{ActorId=400}),"verified teammate damage must not depend on local-player identity");
        Require(!independent.Include(hit with{ActorId=300}),"missing self identity must not admit unrelated actors");
        var distant=new CombatScope{ConfirmedSelfName="勇者"};
        distant.Observe(Named(0x33,200,"勇者"),7);distant.Observe(join,8);
        var idle=distant.DisplayRows(Array.Empty<PlayerRow>());
        Require(idle.Length==2&&idle.All(r=>r.Hits==0&&r.Damage==0&&r.Dps==0),"idle roster displays every detected member with zero totals");
        Require(idle.Single(r=>r.Actor=="隊友 (party)") is {ActorId:null,Status:"Combat identity not received"},"distant roster member appears without an invented entity ID");
        Require(idle.Single(r=>r.ActorId is null).DpsText=="—","unresolved combat identity is unavailable rather than a measured zero");
        distant.BeginChunk();distant.Observe(Named(0x44,400,"隊友"),9);
        Require(distant.Include(hit with{ActorId=400}),"first nearby hit is admitted when an already-confirmed member gets an entity mapping in the same chunk");distant.EndChunk();
        var partyMeter=new Meter();partyMeter.Add(hit with{ActorId=400});
        var visible=distant.DisplayRows(partyMeter.View(allowedActors:distant.AllowedActors()).Players);
        Require(visible.Length==2&&visible.Single(r=>r.ActorId==400).Damage==50&&visible.Single(r=>r.ActorId==200).Damage==0,"damage joins the existing roster row while idle self remains visible");
        partyMeter.Reset();Require(distant.DisplayRows(partyMeter.View().Players).Length==2,"new encounter preserves idle roster rows");
        distant.Observe(new byte[]{8,0x13,0x92,0,0},10);
        Require(distant.DisplayRows(visible).Length==1,"departed member disappears even with historical damage");
        scope.Observe(join,8);
        Require(scope.Include(hit with{ActorId=400})&&!scope.Include(hit with{ActorId=16296}),"decoded roster uniquely maps peer name; raw roster ID cannot admit an actor");
        scope.Observe(Named(0x44,402,"隊友"),9);
        Require(!scope.Include(hit with{ActorId=400})&&!scope.Include(hit with{ActorId=402}),"ambiguous party names cannot admit either entity");
        scope.Observe(new byte[]{8,0x13,0x92,0,0},10);
        Require(scope.Party.IsAuthoritative&&scope.Party.Members.Count==0&&scope.AllowedActors().SetEquals(new[]{200}),"self-leave automatically removes former party from scope");
        scope.Observe(Named(0x44,401,"勇者"),8);Require(scope.AllowedActors().Count==0,"duplicate self name blocks whole scope");
        var changing=new CombatScope{ConfirmedSelfName="勇者"};changing.Observe(Named(0x33,200,"勇者"),20);
        changing.BeginChunk();changing.Observe(join,21);changing.Observe(Named(0x44,400,"隊友"),21);
        Require(!changing.Include(hit with{ActorId=400}),"new membership must not admit ambiguous pre-join hits in a shared chunk");changing.EndChunk();
        Require(changing.Include(hit with{ActorId=400}),"subsequent chunks include joined member");
        var automatic=new CombatScope{ConfirmedSelfName="SavedOldCharacter"};
        automatic.Observe(Named(0x44,300,"NearbyPerson"),30);
        Require(automatic.ConfirmedSelfName=="SavedOldCharacter"&&automatic.LocalPlayerId is null,"nearby names never select the local character");
        automatic.Observe(Named(0x33,200,"NewCharacter"),31);
        Require(automatic.ConfirmedSelfName=="NewCharacter"&&automatic.LocalPlayerId==200,"authoritative local identity overrides stale saved name");
        automatic.Observe(Named(0x33,201,"OtherCharacter"),32);
        Require(automatic.ConfirmedSelfName=="OtherCharacter"&&automatic.LocalPlayerId==201,"automatic name follows character changes");
    }
    static void Require(bool condition,string reason){if(!condition)throw new Exception("Scope test failed: "+reason);}
}

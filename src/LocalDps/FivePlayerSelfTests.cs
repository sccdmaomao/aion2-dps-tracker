using System.Text;
using LocalDps.Protocol;
namespace LocalDps;

public static class FivePlayerSelfTests
{
    public static void Run()
    {
        byte[] V(int n){var b=new List<byte>();do{byte x=(byte)(n&127);n>>=7;b.Add((byte)(x|(n>0?128:0)));}while(n>0);return b.ToArray();}
        byte[] F(byte op,byte family,byte[] body)=>V(body.Length+6).Concat(new[]{op,family}).Concat(body).ToArray();
        byte[] Named(int actor,string name){var n=Encoding.UTF8.GetBytes(name);return F(0x44,0x36,V(actor).Concat(new byte[]{0,0,0,0,7,(byte)n.Length}).Concat(n).ToArray());}
        byte[] Periodic(int peer){var b=new byte[63];b[0]=(byte)peer;BitConverter.GetBytes((ushort)1102).CopyTo(b,6);b[8]=9;b[36]=45;b[^1]=1;return F(0x1c,0x92,b);}
        byte[] Status(int actor)=>F(0x0e,0x92,V(actor).Concat(new byte[]{1}).Concat(BitConverter.GetBytes(187300011)).ToArray());
        var known=Enumerable.Range(1,5).Select(i=>new KnownCharacterName((ulong)i,1102,9,"Peer"+(char)('A'+i-1))).ToArray();
        CombatScope Setup(bool ownMetadata=true)
        {
            var s=new CombatScope{ConfirmedSelfName="PeerE"};s.Party.RestoreKnownNames(ownMetadata?known:known[..4]);
            for(int i=1;i<=4;i++)s.Observe(Periodic(i),i);return s;
        }
        var scope=Setup();for(int i=1;i<=4;i++){scope.Observe(Named(100+i,known[i-1].Name),10);scope.Observe(Status(100+i),11);}
        Require(scope.LocalPlayerId is null,"incomplete party actor set cannot identify self");
        scope.Observe(Status(105),12);Require(scope.LocalPlayerId==105,"complete party actor set resolves the remaining local actor without class guessing");
        scope.Observe(Named(105,"OtherPlayer"),13);Require(scope.LocalPlayerId is null,"contradictory observed name invalidates derived self identity");
        scope.Reset();Require(scope.LocalPlayerId is null,"connection reset clears derived combat identity");
        var missing=Setup();for(int i=1;i<=5;i++)missing.Observe(Status(100+i),10);for(int i=1;i<=3;i++)missing.Observe(Named(100+i,known[i-1].Name),11);
        Require(missing.LocalPlayerId is null,"all four teammates need unique mappings");
        var extra=Setup();for(int i=1;i<=6;i++)extra.Observe(Status(100+i),10);for(int i=1;i<=4;i++)extra.Observe(Named(100+i,known[i-1].Name),11);
        Require(extra.LocalPlayerId is null,"extra party-status actors prevent ambiguous self binding");
        var noOwn=Setup(false);for(int i=1;i<=5;i++)noOwn.Observe(Status(100+i),10);for(int i=1;i<=4;i++)noOwn.Observe(Named(100+i,known[i-1].Name),11);
        Require(noOwn.LocalPlayerId is null,"own name needs a learned persistent identity");
        var delayed=Setup();var accepted=new List<CombatHit>();var attribution=new PartyHitAttribution(delayed,accepted.Add);
        delayed.BeginChunk();attribution.Add(new(20,105,200,11010001,500,false));attribution.Add(new(20,101,200,11010001,200,false));attribution.Add(new(20,999,200,1235410,99999,false));delayed.EndChunk();
        for(int i=1;i<=5;i++)delayed.Observe(Status(100+i),25);for(int i=1;i<=4;i++)delayed.Observe(Named(100+i,known[i-1].Name),26);
        attribution.Flush(30);Require(accepted.Count==2&&accepted.Sum(h=>h.Damage)==700,"delayed identity releases own and teammate hits while excluding NPC hits");
        delayed.Observe(new byte[]{8,0x13,0x92,0,0},31);attribution.Flush(32);Require(attribution.PendingCount==0,"party changes discard unresolved candidates");
        // Five recruiting-room names, independently decoded; no membership proof.
        var body=new List<byte>(new byte[24]);body[0]=1;body[5]=5;body[12]=5;body[18]=0x4e;body[19]=4;body[23]=5;
        for(int i=1;i<=5;i++)
        {
            byte[] name=Encoding.UTF8.GetBytes(known[i-1].Name);
            body.AddRange(new byte[]{12,(byte)i});body.AddRange(BitConverter.GetBytes(i));body.AddRange(new byte[]{0,0,0x4e,4,(byte)name.Length});body.AddRange(name);
            body.AddRange(BitConverter.GetBytes(9));body.AddRange(BitConverter.GetBytes(45));body.AddRange(BitConverter.GetBytes(100));body.AddRange(new byte[16]);
        }
        body.AddRange(new byte[2]);var room=new PartyRosterDecoder();room.Observe(F(2,0x97,body.ToArray()),1);
        Require(room.KnownNames.Length==5&&!room.IsAuthoritative&&room.Members.Count==0,"room names are metadata only");
        for(int i=1;i<=4;i++)room.Observe(Periodic(i),2);
        Require(room.Members.Select(m=>m.Name).Order().SequenceEqual(known[..4].Select(n=>n.Name).Order()),"independent party updates resolve all four room identities");
    }
    static void Require(bool value,string message){if(!value)throw new Exception("Five-player party: "+message);}
}

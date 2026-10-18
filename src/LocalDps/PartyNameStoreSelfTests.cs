using LocalDps.Protocol;
namespace LocalDps;

public static class PartyNameStoreSelfTests
{
    public static void Run()
    {
        string path=Path.Combine(AppContext.BaseDirectory,"localdps-names-"+Guid.NewGuid()+".json");
        try
        {
            var names=new[]{new KnownCharacterName(410001,1102,9,"隊友")};
            PartyNameStore.Save(names,path);var loaded=PartyNameStore.Load(path);
            if(loaded.Length!=1||loaded[0]!=names[0])throw new Exception("Persistent name-cache round trip failed");
            var party=new PartyRosterDecoder();party.RestoreKnownNames(loaded);
            if(party.IsAuthoritative||party.Members.Count!=0)throw new Exception("Stored names cannot restore membership");
            party.RestoreKnownNames(new[]{new KnownCharacterName(1,1102,9,"not a valid name"),new KnownCharacterName(2,1102,9,null!)});
            if(party.KnownNames.Length!=1)throw new Exception("Invalid stored names must be rejected");
            var history=new Capture.DiagnosticRecording();history.Add("[203.0.113.10]:13328>[192.168.1.10]:55020",1000,new byte[]{1,2,3});
            File.WriteAllLines(path,history.Lines());int chunks=0;
            Capture.RecordedStreamReplay.Read(path,(data,time,flow)=>{if(!data.SequenceEqual(new byte[]{1,2,3})||time.ToUnixTimeMilliseconds()!=1000)throw new Exception("Diagnostic replay payload mismatch");chunks++;});
            if(chunks!=1)throw new Exception("Diagnostic export cannot be replayed");
            history.Add("flow",122000,new byte[]{4});if(history.Count!=1)throw new Exception("Diagnostic buffer must expire old traffic");
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }
}

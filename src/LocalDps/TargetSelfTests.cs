using LocalDps.Protocol;
namespace LocalDps;
public static class TargetSelfTests
{
    public static void Run()
    {
        byte[] Frame(int template,bool prefix)
        {
            var body=new List<byte>{0x40,0x36,200,1,0,0,0};if(prefix)body.AddRange(new byte[2]);body.AddRange(BitConverter.GetBytes(template));body.AddRange(new byte[]{1,2});return new[]{(byte)(body.Count+4)}.Concat(body).ToArray();
        }
        var names=new TargetIdentityDecoder(new Dictionary<int,string>{{1_234_567,"Training Scarecrow"}});
        names.Observe(Frame(1_234_567,false),1);
        if(names.Resolve(200)!="Training Scarecrow"||names.Templates[200]!=1_234_567)throw new Exception("NPC entity/template mapping failed");
        names.Reset();names.Observe(Frame(1_234_567,true),2);
        if(names.Resolve(200)!="Training Scarecrow")throw new Exception("Prefixed NPC mapping failed");
        names.Observe(Frame(1_234_568,true),3);
        if(names.Resolve(200)!=null)throw new Exception("Reused entity must not retain old target name");
        names.Reset();names.Observe(Frame(1_234_567,false)[..7],4);
        if(names.Templates.Count!=0)throw new Exception("Truncated spawn must not invent target name");
        if(names.Resolve(1_234_567)!=null)throw new Exception("Template IDs must not be treated as entity IDs");
    }
}

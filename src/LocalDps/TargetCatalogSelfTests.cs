using LocalDps.Protocol;
namespace LocalDps;
public static class TargetCatalogSelfTests
{
    public static void Run()
    {
        string path=Path.Combine(AppContext.BaseDirectory,"test-target-catalog-"+Guid.NewGuid()+".json");
        try
        {
            var absent=new TargetIdentityDecoder(catalogPath:path);
            if(absent.CatalogWarning is not null)throw new Exception("Missing optional catalog must be normal.");
            File.WriteAllText(path,"{\"1234567\":\"Example Target\"}");
            var custom=new TargetIdentityDecoder(catalogPath:path);
            byte[] body=new byte[]{0x40,0x36,200,1,0,0,0}.Concat(BitConverter.GetBytes(1234567)).Concat(new byte[]{1,2}).ToArray();
            custom.Observe(new[]{(byte)(body.Length+4)}.Concat(body).ToArray(),1);
            if(custom.Resolve(200)!="Example Target"||custom.CatalogWarning is not null)throw new Exception("Optional local target catalog did not bind the observed template.");
            foreach(string bad in new[]{"not json","null","{\"1234567\":null}","{\"42\":\"Bad template ID\"}","{\"1234567\":\"Bad\\nname\"}"})
            {
                File.WriteAllText(path,bad);var rejected=new TargetIdentityDecoder(catalogPath:path);
                if(rejected.CatalogWarning is null)throw new Exception("Invalid local target catalog was not reported.");
            }
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }
}

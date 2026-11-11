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
            byte[] Spawn(int template)=>new byte[]{0x40,0x36,200,1,0,0,0}.Concat(BitConverter.GetBytes(template)).Concat(new byte[]{1,2}).ToArray();
            void Observe(TargetIdentityDecoder decoder,int template){var data=Spawn(template);decoder.Observe(new[]{(byte)(data.Length+4)}.Concat(data).ToArray(),1);}
            Observe(absent,2300812);
            if(absent.Resolve(200)!="Vakron")throw new Exception("Fresh install must resolve bundled enemy names without a local catalog.");
            var hit=new CombatHit(1000,500,200,11010001,250,false);
            var tracker=new EncounterTracker();
            void Track(TargetIdentityDecoder targets,CombatHit attack)
            {
                if(targets.ClassifyBossHit(attack) is {} bossHit)tracker.Add(bossHit);
            }
            Track(absent,hit with {TargetId=2300812}); // template IDs cannot qualify as entity IDs
            Observe(absent,2000000); // Punching Bag is a known non-boss template
            Track(absent,hit);
            if(tracker.Latest is not null)throw new Exception("Unmapped targets and training dummies must not start fights.");
            Observe(absent,2300812);
            Track(absent,hit);
            Track(absent,hit with {TargetId=201,Damage=10000});
            Observe(absent,2000000);
            Track(absent,hit with {TimestampMs=1500,Damage=10000});
            var fight=tracker.Latest!;
            if(fight.Meter.TotalDamage!=250||fight.Meter.TotalHits!=1||fight.Meter.Targets().Single().Target!="Vakron")
                throw new Exception("Boss damage and name must survive later entity reuse without counting non-boss hits.");
            absent.Reset();
            if(absent.ClassifyBossHit(hit) is not null)throw new Exception("Reset must clear boss combat IDs.");
            File.WriteAllText(path,"{\"2300812\":\"Custom boss name\"}");
            var overridden=new TargetIdentityDecoder(catalogPath:path);Observe(overridden,2300812);
            if(overridden.Resolve(200)!="Custom boss name"||overridden.ClassifyBossHit(hit)?.CapturedTargetName!="Custom boss name")
                throw new Exception("Local display name overrides must keep bundled boss classification.");
            File.WriteAllText(path,"{\"1234567\":\"Example Target\"}");
            var custom=new TargetIdentityDecoder(catalogPath:path);
            byte[] body=new byte[]{0x40,0x36,200,1,0,0,0}.Concat(BitConverter.GetBytes(1234567)).Concat(new byte[]{1,2}).ToArray();
            custom.Observe(new[]{(byte)(body.Length+4)}.Concat(body).ToArray(),1);
            if(custom.Resolve(200)!="Example Target"||custom.CatalogWarning is not null)throw new Exception("Optional local target catalog did not bind the observed template.");
            if(custom.ClassifyBossHit(hit) is not null)throw new Exception("A custom display name must not classify a target as a boss.");
            foreach(string bad in new[]{"not json","null","{\"1234567\":null}","{\"42\":\"Bad template ID\"}","{\"1234567\":\"Bad\\nname\"}"})
            {
                File.WriteAllText(path,bad);var rejected=new TargetIdentityDecoder(catalogPath:path);
                if(rejected.CatalogWarning is null)throw new Exception("Invalid local target catalog was not reported.");
                Observe(rejected,2300812);
                if(rejected.Resolve(200)!="Vakron")throw new Exception("Invalid local catalog must preserve bundled names.");
            }
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }
}

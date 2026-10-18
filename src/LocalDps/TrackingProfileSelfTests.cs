namespace LocalDps;

public static class TrackingProfileSelfTests
{
    public static void Run()
    {
        string root=Path.Combine(AppContext.BaseDirectory,"LocalDps-profile-"+Guid.NewGuid().ToString("N"));
        string saved=Path.Combine(root,"settings","track-people.json"),legacy=Path.Combine(root,"track-people.json");
        Directory.CreateDirectory(root);
        void Require(bool value,string reason){if(!value)throw new Exception("Profile test failed: "+reason);}
        try
        {
            Require(TrackingProfile.Load(saved,legacy).SelfName=="","fresh install remains unset");
            TrackingProfile.Create("LegacyHero").Save(legacy);
            Require(TrackingProfile.Load(saved,legacy).SelfName=="LegacyHero","existing portable profile remains readable");
            TrackingProfile.Create("勇者").Save(saved);
            TrackingProfile.Create("").Save(legacy);
            Require(TrackingProfile.Load(saved,legacy).SelfName=="勇者","new release blank profile cannot erase remembered name");
            TrackingProfile.Create("").Save(saved);
            TrackingProfile.Create("LegacyHero").Save(legacy);
            Require(TrackingProfile.Load(saved,legacy).SelfName=="","explicit clear must not resurrect a legacy name");
            File.WriteAllText(saved,new string('x',8193));
            bool rejected=false;try{TrackingProfile.Load(saved,legacy);}catch(InvalidDataException){rejected=true;}
            Require(rejected,"oversized profile is rejected");
        }
        finally
        {
            if(File.Exists(saved))File.Delete(saved);
            if(Directory.Exists(Path.GetDirectoryName(saved)))Directory.Delete(Path.GetDirectoryName(saved)!);
            if(File.Exists(legacy))File.Delete(legacy);
            Directory.Delete(root);
        }
    }
}

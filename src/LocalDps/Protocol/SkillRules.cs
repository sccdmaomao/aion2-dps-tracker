using System.Text.Json;
namespace LocalDps.Protocol;

/// <summary>Local, event-specific exclusions. Unknown skills are retained.
/// Exact direct-effect IDs must never be expanded into a periodic base-ID exclusion.</summary>
public sealed class SkillRules
{
    readonly HashSet<int> directExact, directHealBases, periodicBases;
    public SkillRules(IEnumerable<int> directExact, IEnumerable<int> directHealBases, IEnumerable<int> periodicBases)
    {
        this.directExact=new(directExact);this.directHealBases=new(directHealBases);this.periodicBases=new(periodicBases);
    }
    public bool Include(CombatHit hit)
    {
        int baseId=hit.SkillId/10000*10000;
        return hit.IsDot ? !periodicBases.Contains(baseId) : !directExact.Contains(hit.SkillId)&&!directHealBases.Contains(baseId);
    }
    public static SkillRules LoadBundled()
    {
        using var input=typeof(SkillRules).Assembly.GetManifestResourceStream("LocalDps.Data.skill-rules.json") ?? throw new InvalidDataException("Bundled skill rules missing.");
        using var doc=JsonDocument.Parse(input);
        if(doc.RootElement.GetProperty("schemaVersion").GetInt32()!=1)throw new InvalidDataException("Unsupported skill rules schema.");
        int[] Read(string name)=>doc.RootElement.GetProperty(name).EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        return new(Read("directExactExcluded"),Read("directHealBaseExcluded"),Read("periodicBaseExcluded"));
    }
}

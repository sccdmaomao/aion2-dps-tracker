using System.Windows.Media.Imaging;

namespace LocalDps;

public static class PlayerClasses
{
    public sealed record Icon(string Name,BitmapSource? Image);
    static readonly Icon Unknown=new("Class unavailable",null);
    // NC's official class emblems are embedded for offline display. Source,
    // original/converted digests and artwork rights are recorded with the assets.
    static readonly Icon[] Classes=
    [
        Load("Gladiator","gladiator"),Load("Templar","templar"),
        Load("Ranger","ranger"),Load("Assassin","assassin"),
        Load("Spiritmaster","spiritmaster"),Load("Sorcerer","sorcerer"),
        Load("Cleric","cleric"),Load("Chanter","chanter")
    ];
    static Icon Load(string name,string asset)
    {
        using var stream=typeof(PlayerClasses).Assembly.GetManifestResourceStream($"LocalDps.ClassIcons.{asset}.png")
            ??throw new InvalidDataException($"Bundled class emblem missing: {asset}.");
        var image=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
        image.Freeze();return new(name,image);
    }
    // Roster/directory class IDs occupy four-value blocks, unlike skill prefixes.
    // Unsupported/future IDs are deliberately left unavailable.
    public static Icon ForRosterId(uint? id)=>id is >=5 and <=36?Classes[(int)((id.Value-5)/4)]:Unknown;
}

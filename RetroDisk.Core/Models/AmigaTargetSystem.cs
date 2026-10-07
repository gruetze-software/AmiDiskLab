namespace AmiDiskLab.Core.Models;

public enum KickstartVersion { V13, V20, V31 }

public sealed record AmigaTargetSystem(KickstartVersion Kickstart, int RamMiB)
{
    public static AmigaTargetSystem Default { get; } = new(KickstartVersion.V13, 1);

    public string KickstartLabel => Kickstart switch
    {
        KickstartVersion.V13 => "1.3",
        KickstartVersion.V20 => "2.0",
        KickstartVersion.V31 => "3.1",
        _ => throw new ArgumentOutOfRangeException(nameof(Kickstart))
    };

    public void Validate()
    {
        if (!Enum.IsDefined(Kickstart) || RamMiB is not (1 or 2 or 4 or 8))
            throw new ArgumentOutOfRangeException(nameof(AmigaTargetSystem), "Unsupported A500 target configuration.");
    }
}

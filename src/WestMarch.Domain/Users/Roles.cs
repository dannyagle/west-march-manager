namespace WestMarch.Domain.Users;

/// <summary>
/// The additive role set. Every user is implicitly a Player (no role row);
/// DM and Campaign Admin are granted on top of that, in any combination.
/// </summary>
public static class Roles
{
    public const string DungeonMaster = "DM";
    public const string CampaignAdmin = "CampaignAdmin";

    public static readonly string[] All = [DungeonMaster, CampaignAdmin];
}

using WestMarch.Domain.Bestiary;

namespace WestMarch.Domain.Adventures;

/// <summary>
/// One encounter within an adventure: a room, a scene, a lead in a mystery.
/// SortOrder is display order only — encounters carry no implied sequence, because
/// many adventures are non-linear (the party chooses which thread to pull).
/// Every section is optional; an encounter can be nothing but read-aloud text.
/// DM/CA eyes only, like the notes it replaces.
/// </summary>
public class Encounter
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AdventureId { get; set; }

    /// <summary>Label for tabs and headers, e.g. "Room 2: The Kennels" or "The Docks".</summary>
    public string Title { get; set; } = default!;

    public int SortOrder { get; set; }

    /// <summary>DM-facing situation notes: tactics, terrain, secrets, triggers.</summary>
    public string? Description { get; set; }

    /// <summary>Text intended to be read verbatim to the players.</summary>
    public string? ReadAloud { get; set; }

    public List<EncounterNpc> Npcs { get; set; } = [];
    public List<EncounterMonster> Monsters { get; set; } = [];
}

/// <summary>A named NPC in an encounter, with free-text critical stats ("AC 15, HP 40, +6 Deception").</summary>
public class EncounterNpc
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EncounterId { get; set; }

    public string Name { get; set; } = default!;

    /// <summary>Critical stats, free text.</summary>
    public string? Stats { get; set; }

    public string? Description { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>A bestiary monster in an encounter, with a head-count ("8 × Orc").</summary>
public class EncounterMonster
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EncounterId { get; set; }

    public Guid MonsterId { get; set; }
    public Monster Monster { get; set; } = default!;

    public int Count { get; set; } = 1;

    public int SortOrder { get; set; }
}

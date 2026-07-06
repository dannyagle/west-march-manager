namespace WestMarch.Domain.Adventures;

/// <summary>Filterable adventure tag, e.g. "horror" or "part of an epic event".</summary>
public class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = default!;

    public List<Adventure> Adventures { get; set; } = [];
}

namespace Fbs.WebApi.Repository;

public abstract class Entity
{
    public int Row { get; set; }
    public abstract string? GetId();
}

public abstract class Entity<TKey> : Entity
{
    public virtual TKey Id { get; set; } = default!;

    public override string? GetId()
    {
        return Id?.ToString();
    }
}
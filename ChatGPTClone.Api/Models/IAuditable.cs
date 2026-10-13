namespace ChatGPTClone.Api.Models;

/// <summary>Entities with CreatedAt; AppDbContext fills it (UTC) on insert.</summary>
public interface IHasCreatedAt
{
    DateTime CreatedAt { get; set; }
}

/// <summary>Entities with CreatedAt and UpdatedAt; AppDbContext maintains both (UTC).</summary>
public interface IAuditable : IHasCreatedAt
{
    DateTime UpdatedAt { get; set; }
}

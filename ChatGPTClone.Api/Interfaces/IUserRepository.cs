using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken);
    Task<User?> GetByGoogleIdAsync(string googleId, CancellationToken cancellationToken);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    void Add(User user);
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Forgets inserts that failed to save (e.g. a duplicate created concurrently), so the context can be reused.</summary>
    void DiscardPendingChanges();
}

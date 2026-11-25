using FiscalizeXero.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FiscalizeXero.Api.Security
{
    /// <summary>The current <see cref="AppUser.TokenVersion"/> per platform user, and signing a user out everywhere.</summary>
    public interface ISessionRevocation
    {
        Task<int> CurrentVersionAsync(long userId);
        Task RevokeAllAsync(long userId);
    }

    public sealed class SessionRevocation : ISessionRevocation
    {
        /// <summary>The claim carrying the version a token was issued with.</summary>
        public const string Claim = "tv";
        /// <summary>Checked on every request, so cached briefly; revoking clears this server's copy at once.</summary>
        private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

        private readonly IServiceScopeFactory _scopes;
        private readonly IMemoryCache _cache;

        public SessionRevocation(IServiceScopeFactory scopes, IMemoryCache cache)
        {
            _scopes = scopes;
            _cache = cache;
        }

        public async Task<int> CurrentVersionAsync(long userId)
        {
            if (_cache.TryGetValue(Key(userId), out int version)) return version;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            version = await db.AppUsers.AsNoTracking().Where(u => u.UserId == userId).Select(u => u.TokenVersion).FirstOrDefaultAsync();
            _cache.Set(Key(userId), version, CacheFor);
            return version;
        }

        public async Task RevokeAllAsync(long userId)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            await db.AppUsers.Where(u => u.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1));
            _cache.Remove(Key(userId));
        }

        private static string Key(long userId) => $"token-version:{userId}";
    }
}

using FiscalizeXero.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Repositories
{
    public class ClientConfigRepository : IClientConfigRepository
    {
        private readonly DataContext _context;

        public ClientConfigRepository(DataContext context)
        {
            _context = context;
        }

        private IQueryable<ClientConfig> WithOwner() =>
            _context.Configs.Include(c => c.AppUserCompany).ThenInclude(auc => auc.AppUser);

        public async Task<IEnumerable<ClientConfig>> GetAllAsync() => await WithOwner().ToListAsync();

        public Task<ClientConfig?> GetByIdAsync(long id) => WithOwner().FirstOrDefaultAsync(c => c.Id == id);

        public Task<ClientConfig?> GetByAppUserCompanyIdAsync(int appUserCompanyId) =>
            WithOwner().FirstOrDefaultAsync(c => c.AppUserCompanyId == appUserCompanyId);

        public Task<ClientConfig?> GetByTenantIdAsync(Guid tenantId) =>
            WithOwner().FirstOrDefaultAsync(c => c.TenantId == tenantId);

        public Task<ClientConfig?> GetByStateAsync(Guid state) => WithOwner().FirstOrDefaultAsync(c => c.State == state);

        public Task<ClientConfig?> GetForCompanyAsync(long companyId)
        {
            return WithOwner()
                .Where(c => c.AppUserCompany.CompanyId == companyId)
                // One connection per Fiscalize company, whichever colleague set it up: a live connection wins, then the
                // row holding the company's devices, then a row whose tokens were cleared but organisation is known;
                // ties go to the oldest.
                .OrderByDescending(c => c.RefreshToken != null)
                .ThenByDescending(c => c.AppUserCompany.DeviceConfigs.Any())
                .ThenByDescending(c => c.TenantId != null)
                .ThenBy(c => c.Id)
                .FirstOrDefaultAsync();
        }

        public Task<bool> IsTenantConnectedElsewhereAsync(Guid tenantId, long companyId) =>
            TenantGuards.ConnectedElsewhereAsync(_context, tenantId, companyId);

        public Task<List<Guid?>> GetConnectionIdsHeldElsewhereAsync(Guid tenantId, long companyId) =>
            TenantGuards.ConnectionIdsHeldElsewhereAsync(_context, tenantId, companyId);

        public async Task<(string? AccessToken, DateTime? TokenExpiresAt)> GetFreshTokenStatusAsync(long id)
        {
            // AsNoTracking bypasses EF's identity cache so we always get the latest DB values,
            // which is essential for the double-checked locking pattern in token refresh.
            var result = await _context.Configs
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new { c.AccessToken, c.TokenExpiresAt })
                .FirstOrDefaultAsync();

            return result == null ? (null, null) : (result.AccessToken, result.TokenExpiresAt);
        }

        public async Task<(string? AccessToken, string? RefreshToken, DateTime? TokenExpiresAt)> GetFreshTokensAsync(long id)
        {
            var result = await _context.Configs
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new { c.AccessToken, c.RefreshToken, c.TokenExpiresAt })
                .FirstOrDefaultAsync();
            return result == null ? (null, null, null) : (result.AccessToken, result.RefreshToken, result.TokenExpiresAt);
        }

        public async Task SaveTokensAsync(ClientConfig clientConfig)
        {
            // Work on whichever instance this context tracks, so only the three token columns are marked modified.
            var tracked = _context.Configs.Local.FirstOrDefault(c => c.Id == clientConfig.Id);
            if (tracked == null)
            {
                _context.Configs.Attach(clientConfig);
                tracked = clientConfig;
            }
            else if (!ReferenceEquals(tracked, clientConfig))
            {
                tracked.AccessToken = clientConfig.AccessToken;
                tracked.RefreshToken = clientConfig.RefreshToken;
                tracked.TokenExpiresAt = clientConfig.TokenExpiresAt;
            }
            var entry = _context.Entry(tracked);
            entry.Property(c => c.AccessToken).IsModified = true;
            entry.Property(c => c.RefreshToken).IsModified = true;
            entry.Property(c => c.TokenExpiresAt).IsModified = true;
            await _context.SaveChangesAsync();
        }

        public async Task SaveSyncStateAsync(long id, DateTime? cursor, DateTime? lastRunAt, DateTime? retryAt, string? error)
        {
            if (error is { Length: > 500 }) error = error[..500];
            // Plain columns (no value converter), so a set-based update is safe and touches nothing else.
            await _context.Configs.Where(c => c.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.LastSyncAt, c => cursor ?? c.LastSyncAt)
                .SetProperty(c => c.LastSyncRunAt, c => lastRunAt ?? c.LastSyncRunAt)
                .SetProperty(c => c.SyncRetryAt, retryAt)
                .SetProperty(c => c.LastSyncError, error));
        }

        public async Task<ClientConfig> CreateAsync(ClientConfig clientConfig)
        {
            _context.Configs.Add(clientConfig);
            await _context.SaveChangesAsync();
            return clientConfig;
        }

        public async Task<ClientConfig> UpdateAsync(ClientConfig clientConfig)
        {
            _context.Configs.Update(clientConfig);
            await _context.SaveChangesAsync();
            return clientConfig;
        }

        public async Task<bool> DeleteAsync(long id)
        {
            var config = await _context.Configs.FindAsync(id);
            if (config == null)
                return false;

            _context.Configs.Remove(config);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}

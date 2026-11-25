using FiscalizeXero.Api.Data;

namespace FiscalizeXero.Api.Services.ClientConfigs
{
    public interface IClientConfigService
    {
        Task<IEnumerable<ClientConfig>> GetAllAsync();
        Task<ClientConfig?> GetByIdAsync(long id);
        Task<ClientConfig?> GetByAppUserCompanyIdAsync(int appUserCompanyId);
        Task<ClientConfig?> GetByTenantIdAsync(Guid tenantId);
        Task<ClientConfig?> GetByStateAsync(Guid state);
        Task<ClientConfig?> GetForCompanyAsync(long companyId);
        /// <summary>Whether a different Fiscalize company already has a live connection to this Xero organisation.</summary>
        Task<bool> IsTenantConnectedElsewhereAsync(Guid tenantId, long companyId);
        /// <summary>The Xero connection ids other Fiscalize companies hold for this organisation (never ours to delete).</summary>
        Task<List<Guid?>> GetConnectionIdsHeldElsewhereAsync(Guid tenantId, long companyId);
        Task<(string? AccessToken, DateTime? TokenExpiresAt)> GetFreshTokenStatusAsync(long id);
        /// <summary>All three token fields as stored now, bypassing any tracked copy.</summary>
        Task<(string? AccessToken, string? RefreshToken, DateTime? TokenExpiresAt)> GetFreshTokensAsync(long id);
        /// <summary>
        /// Saves only AccessToken, RefreshToken and TokenExpiresAt. Never a whole-row update: a copy of the row loaded
        /// earlier would otherwise write back a refresh token Xero has already rotated, or settings changed meanwhile.
        /// </summary>
        Task SaveTokensAsync(ClientConfig clientConfig);
        /// <summary>Records the outcome of a backstop sync run without touching any other column.</summary>
        Task SaveSyncStateAsync(long id, DateTime? cursor, DateTime? lastRunAt, DateTime? retryAt, string? error);
        Task<ClientConfig> CreateAsync(ClientConfig clientConfig);
        Task<ClientConfig> UpdateAsync(ClientConfig clientConfig);
        Task<bool> DeleteAsync(long id);
    }
}

using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;

namespace FiscalizeXero.Api.Services.ClientConfigs
{
    public class ClientConfigService : IClientConfigService
    {
        private readonly IClientConfigRepository _repository;

        public ClientConfigService(IClientConfigRepository clientConfigRepository)
        {
            _repository = clientConfigRepository;
        }

        public Task<IEnumerable<ClientConfig>> GetAllAsync() => _repository.GetAllAsync();
        public Task<ClientConfig?> GetByIdAsync(long id) => _repository.GetByIdAsync(id);
        public Task<ClientConfig?> GetByAppUserCompanyIdAsync(int appUserCompanyId) => _repository.GetByAppUserCompanyIdAsync(appUserCompanyId);
        public Task<ClientConfig?> GetByTenantIdAsync(Guid tenantId) => _repository.GetByTenantIdAsync(tenantId);
        public Task<ClientConfig?> GetByStateAsync(Guid state) => _repository.GetByStateAsync(state);
        public Task<ClientConfig?> GetForCompanyAsync(long companyId) => _repository.GetForCompanyAsync(companyId);
        public Task<bool> IsTenantConnectedElsewhereAsync(Guid tenantId, long companyId) => _repository.IsTenantConnectedElsewhereAsync(tenantId, companyId);
        public Task<List<Guid?>> GetConnectionIdsHeldElsewhereAsync(Guid tenantId, long companyId) => _repository.GetConnectionIdsHeldElsewhereAsync(tenantId, companyId);
        public Task<(string? AccessToken, DateTime? TokenExpiresAt)> GetFreshTokenStatusAsync(long id) => _repository.GetFreshTokenStatusAsync(id);
        public Task<(string? AccessToken, string? RefreshToken, DateTime? TokenExpiresAt)> GetFreshTokensAsync(long id) => _repository.GetFreshTokensAsync(id);
        public Task SaveTokensAsync(ClientConfig clientConfig) => _repository.SaveTokensAsync(clientConfig);
        public Task SaveSyncStateAsync(long id, DateTime? cursor, DateTime? lastRunAt, DateTime? retryAt, string? error) =>
            _repository.SaveSyncStateAsync(id, cursor, lastRunAt, retryAt, error);
        public Task<ClientConfig> CreateAsync(ClientConfig clientConfig) => _repository.CreateAsync(clientConfig);
        public Task<ClientConfig> UpdateAsync(ClientConfig clientConfig) => _repository.UpdateAsync(clientConfig);
        public Task<bool> DeleteAsync(long id) => _repository.DeleteAsync(id);
    }
}

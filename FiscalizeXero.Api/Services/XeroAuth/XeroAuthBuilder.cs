using FiscalizeXero.Api.Data;
using XeroAuthModel = FiscalizeXero.Xero.XeroAuth;

namespace FiscalizeXero.Api.Services.XeroAuth
{
    public interface IXeroAuthBuilder
    {
        /// <summary>
        /// What a Xero API call needs for the given connection: a valid (refreshed if needed) access token and the
        /// organisation's tenant id. Null when the connection can't be used (no organisation, no token, or the
        /// refresh failed), in which case the user has to reconnect.
        /// </summary>
        Task<XeroAuthModel?> BuildAsync(ClientConfig config);
    }

    public class XeroAuthBuilder : IXeroAuthBuilder
    {
        private readonly IXeroAuthService _authService;

        public XeroAuthBuilder(IXeroAuthService authService)
        {
            _authService = authService;
        }

        public async Task<XeroAuthModel?> BuildAsync(ClientConfig config)
        {
            if (!config.TenantId.HasValue || config.TenantId.Value == Guid.Empty)
                return null;

            var token = await _authService.GetValidAccessTokenAsync(config);
            if (string.IsNullOrEmpty(token))
                return null;

            return new XeroAuthModel
            {
                Token = token,
                TenantId = config.TenantId.Value,
                // A 401 from Xero despite an unexpired token (revoked, or clocks differ) gets one forced
                // refresh and one retry inside RestClient before it is surfaced.
                RefreshAccessTokenAsync = () => _authService.ForceRefreshAsync(config)
            };
        }
    }
}

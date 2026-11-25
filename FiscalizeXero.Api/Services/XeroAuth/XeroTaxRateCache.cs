using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.Services;
using Microsoft.Extensions.Caching.Memory;
using XeroAuthModel = FiscalizeXero.Xero.XeroAuth;

namespace FiscalizeXero.Api.Services.XeroAuth
{
    /// <summary>
    /// An organisation's tax rates, read from Xero at most once every few minutes. Rates rarely change, every
    /// fiscalisation needs them, and Xero allows an organisation only 60 calls a minute and as few as 1,000 a day.
    /// </summary>
    public interface IXeroTaxRateCache
    {
        Task<List<TaxRate>> GetAsync(XeroAuthModel auth);
        /// <summary>The same rates keyed by tax type, as rate and ZIMRA category.</summary>
        Task<IReadOnlyDictionary<string, LineTax>> GetLineTaxesAsync(XeroAuthModel auth);
    }

    public class XeroTaxRateCache : IXeroTaxRateCache
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

        private readonly IMemoryCache _cache;
        public XeroTaxRateCache(IMemoryCache cache) => _cache = cache;

        public async Task<List<TaxRate>> GetAsync(XeroAuthModel auth)
        {
            var key = $"xero-taxrates:{auth.TenantId}";
            if (_cache.TryGetValue(key, out List<TaxRate>? cached) && cached != null) return cached;
            var rates = await TaxRateService.ListAsync(auth);
            _cache.Set(key, rates, Lifetime);
            return rates;
        }

        public async Task<IReadOnlyDictionary<string, LineTax>> GetLineTaxesAsync(XeroAuthModel auth) =>
            XeroTaxResolver.Resolve(await GetAsync(auth));
    }
}

using FiscalizeXero.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FiscalizeXero.Api.Services.CompanyConnections
{
    /// <summary>
    /// Watches the two connection invariants: a Fiscalize company has one live Xero connection, and a Xero
    /// organisation is connected to one Fiscalize company. Degraded, still HTTP 200, when either is broken. Only
    /// counts are reported, never which company.
    /// </summary>
    public sealed class CompanyConnectionsHealthCheck : IHealthCheck
    {
        /// <summary>Health check registration name.</summary>
        public const string Name = "company-connections";
        /// <summary>Key of the count of Fiscalize companies with more than one live Xero connection.</summary>
        public const string DataKey = "companiesWithDuplicateConnections";
        /// <summary>Key of the count of Xero organisations connected to more than one Fiscalize company.</summary>
        public const string ContestedKey = "xeroOrganisationsSharedBetweenCompanies";

        private readonly DataContext _db;
        /// <summary>Creates the check over the request's database context.</summary>
        public CompanyConnectionsHealthCheck(DataContext db) => _db = db;

        /// <summary>Number of Fiscalize companies with more than one live Xero connection.</summary>
        public static Task<int> CountAsync(DataContext db, CancellationToken ct = default) =>
            db.Configs.Where(c => c.RefreshToken != null)
                .GroupBy(c => c.AppUserCompany.CompanyId)
                .CountAsync(g => g.Count() > 1, ct);

        /// <inheritdoc />
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var count = await CountAsync(_db, cancellationToken);
            var contested = await TenantGuards.ContestedTenants(_db).CountAsync(cancellationToken);
            var data = new Dictionary<string, object> { [DataKey] = count, [ContestedKey] = contested };
            return count == 0 && contested == 0
                ? HealthCheckResult.Healthy("One Xero connection per company", data)
                : HealthCheckResult.Degraded("Duplicate or shared Xero connections need attention: background fiscalisation skips them until one side disconnects", data: data);
        }
    }
}

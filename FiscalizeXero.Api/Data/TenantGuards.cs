using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Data
{
    /// <summary>
    /// A Xero organisation (tenant) must belong to exactly one Fiscalize company: otherwise one company's invoices
    /// could be fiscalised with another's ZIMRA device, and its staff could read the other's books. The OAuth callback
    /// refuses to create a second one; these queries let every background path refuse to act on any that already exist.
    /// </summary>
    public static class TenantGuards
    {
        /// <summary>Organisations with live connections from more than one Fiscalize company.</summary>
        public static IQueryable<Guid?> ContestedTenants(DataContext db) =>
            db.Configs
                .Where(c => c.RefreshToken != null && c.TenantId != null)
                .GroupBy(c => c.TenantId)
                .Where(g => g.Select(c => c.AppUserCompany.CompanyId).Distinct().Count() > 1)
                .Select(g => g.Key);

        /// <summary>Whether another Fiscalize company already has a live connection to <paramref name="tenantId"/>.</summary>
        public static Task<bool> ConnectedElsewhereAsync(DataContext db, Guid tenantId, long companyId, CancellationToken ct = default) =>
            db.Configs.AnyAsync(c => c.TenantId == tenantId && c.RefreshToken != null && c.AppUserCompany.CompanyId != companyId, ct);

        /// <summary>
        /// The Xero connection ids other Fiscalize companies hold for <paramref name="tenantId"/>. A connection that
        /// is in this list must never be deleted on this company's behalf: it is someone else's.
        /// </summary>
        public static Task<List<Guid?>> ConnectionIdsHeldElsewhereAsync(DataContext db, Guid tenantId, long companyId, CancellationToken ct = default) =>
            db.Configs
                .Where(c => c.TenantId == tenantId && c.RefreshToken != null && c.AppUserCompany.CompanyId != companyId)
                .Select(c => c.XeroConnectionId)
                .ToListAsync(ct);
    }
}

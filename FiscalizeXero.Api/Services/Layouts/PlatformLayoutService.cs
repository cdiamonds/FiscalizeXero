using System.Collections.Concurrent;
using Fiscalize.LayoutRenderer;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.DeviceSession;
using FiscalizeXero.Fiscalize.Models.PluginLayouts;
using FiscalizeXero.Fiscalize.Services.PluginLayouts;
using FiscalizeXero.Xero.Services;
using Microsoft.EntityFrameworkCore;
using XeroAuthModel = FiscalizeXero.Xero.XeroAuth;

namespace FiscalizeXero.Api.Services.Layouts
{
    /// <summary>A layout ready to draw: the verified package and what it is.</summary>
    public sealed record LayoutPick(LayoutPackage Package, int Version, bool IsStandardDesign, string DocumentType, string PaperSize);

    /// <summary>
    /// The layouts designed on the Fiscalize platform for Xero (plugin type "Xero"), the way the desktop plugins keep them,
    /// but on this server and per company: the last good package is kept in the database, checked against the platform
    /// with its hash when it is a few minutes old, and never replaced by a damaged download. When the company has no live
    /// layout the platform serves Fiscalize's standard design with the company's letterhead, so there is always something
    /// to draw while the platform is reachable or a copy is held.
    ///
    /// Nothing here can stop a document: every failure is logged and the caller falls back to the stamped Xero PDF.
    /// </summary>
    public interface IPlatformLayoutService
    {
        /// <summary>The layout for a document type ("invoice"/"creditnote" or "Invoice"/"CreditNote") and paper, or null when there is none.</summary>
        Task<LayoutPick?> GetAsync(ClientConfig config, string documentType, string paperSize, XeroAuthModel? xero = null, CancellationToken ct = default);

        /// <summary>Checks both document types on the company's paper with the platform now (the Connected apps report does this).</summary>
        Task RefreshAsync(ClientConfig config, XeroAuthModel? xero = null, CancellationToken ct = default);

        /// <summary>What the company holds, for the settings page and Connected apps.</summary>
        Task<List<LayoutPackageCache>> HeldAsync(int appUserCompanyId, CancellationToken ct = default);
    }

    public class PlatformLayoutService : IPlatformLayoutService
    {
        public static readonly string[] PlatformDocumentTypes = { "Invoice", "CreditNote" };

        // Field lists already sent this run, by company, and when: sent again daily so new tracking categories appear.
        private static readonly ConcurrentDictionary<int, DateTime> FieldsSentAt = new();
        private static readonly ConcurrentDictionary<(int, string, string), SemaphoreSlim> Gates = new();

        private readonly DataContext _db;
        private readonly IDeviceCaller _device;
        private readonly ILogger<PlatformLayoutService> _logger;
        private readonly TimeSpan _refreshAfter;

        public PlatformLayoutService(DataContext db, IDeviceCaller device, IConfiguration configuration, ILogger<PlatformLayoutService> logger)
        {
            _db = db;
            _device = device;
            _logger = logger;
            _refreshAfter = TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("Fiscalize:Layouts:RefreshMinutes", 10), 1, 24 * 60));
        }

        /// <summary>"invoice" -> "Invoice", "creditnote" -> "CreditNote" (the platform's names).</summary>
        public static string PlatformDocumentType(string documentType) =>
            string.Equals(documentType, FiscalDocumentType.CreditNote, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(documentType, "CreditNote", StringComparison.OrdinalIgnoreCase) ? "CreditNote" : "Invoice";

        public async Task<LayoutPick?> GetAsync(ClientConfig config, string documentType, string paperSize, XeroAuthModel? xero = null, CancellationToken ct = default)
        {
            var type = PlatformDocumentType(documentType);
            var paper = LayoutPapers.Normalise(paperSize);
            var companyId = config.AppUserCompanyId;

            var row = await FindAsync(companyId, type, paper, ct);
            if (row == null || DateTime.UtcNow - row.CheckedAt > _refreshAfter)
                row = await RefreshOneAsync(config, type, paper, xero, ct);

            if (row == null) return null;
            var package = LayoutPackage.TryParse(row.PackageJson, out var error);
            if (package == null)
            {
                _logger.LogWarning("Saved layout {Type} {Paper} for company {CompanyId} is unusable ({Error}); dropping it", type, paper, companyId, error);
                _db.LayoutPackageCaches.Remove(row);
                await _db.SaveChangesAsync(ct);
                return null;
            }
            return new LayoutPick(package, row.Version, row.IsStandardDesign, type, paper);
        }

        public async Task RefreshAsync(ClientConfig config, XeroAuthModel? xero = null, CancellationToken ct = default)
        {
            var paper = LayoutPapers.Normalise(config.LayoutPaperSize);
            foreach (var type in PlatformDocumentTypes)
                await RefreshOneAsync(config, type, paper, xero, ct);
        }

        public Task<List<LayoutPackageCache>> HeldAsync(int appUserCompanyId, CancellationToken ct = default) =>
            _db.LayoutPackageCaches.AsNoTracking().Where(l => l.AppUserCompanyId == appUserCompanyId)
                .OrderBy(l => l.DocumentType).ThenBy(l => l.PaperSize).ToListAsync(ct);

        private Task<LayoutPackageCache?> FindAsync(int companyId, string type, string paper, CancellationToken ct) =>
            _db.LayoutPackageCaches.FirstOrDefaultAsync(l => l.AppUserCompanyId == companyId && l.DocumentType == type && l.PaperSize == paper, ct);

        /// <summary>Asks the platform for one layout and keeps the answer. Returns what is held afterwards (possibly the old copy).</summary>
        private async Task<LayoutPackageCache?> RefreshOneAsync(ClientConfig config, string type, string paper, XeroAuthModel? xero, CancellationToken ct)
        {
            var companyId = config.AppUserCompanyId;
            var gate = Gates.GetOrAdd((companyId, type, paper), _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                var row = await FindAsync(companyId, type, paper, ct);
                if (row != null && DateTime.UtcNow - row.CheckedAt <= TimeSpan.FromSeconds(30))
                    return row; // another request just checked it

                var device = await _device.FindDeviceAsync(companyId);
                if (device == null)
                {
                    _logger.LogInformation("Company {CompanyId} has no device key yet; layouts cannot be checked", companyId);
                    return row;
                }

                await UploadFieldsIfDueAsync(companyId, device, xero, ct);

                PluginLayoutFetch fetch;
                try
                {
                    fetch = await _device.CallAsync(device, auth => PlatformLayoutClient.GetCurrentAsync(auth, XeroLayoutMapper.PluginType, type, paper, row?.Hash), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not reach the platform for layout {Type} {Paper} of company {CompanyId}; keeping the saved copy", type, paper, companyId);
                    return row;
                }

                switch (fetch.Outcome)
                {
                    case PluginLayoutFetchOutcome.NotModified when row != null:
                        row.CheckedAt = DateTime.UtcNow;
                        await _db.SaveChangesAsync(ct);
                        return row;

                    case PluginLayoutFetchOutcome.None:
                        if (row != null)
                        {
                            _logger.LogInformation("No layout is live for {Type} {Paper} of company {CompanyId} any more; forgetting the saved copy", type, paper, companyId);
                            _db.LayoutPackageCaches.Remove(row);
                            await _db.SaveChangesAsync(ct);
                        }
                        return null;

                    case PluginLayoutFetchOutcome.Updated:
                        var current = fetch.Current!;
                        var package = LayoutPackage.TryParse(current.Package, out var error);
                        if (package == null)
                        {
                            // Never replace a good layout with a damaged one.
                            _logger.LogWarning("Layout {Type} {Paper} for company {CompanyId} was rejected ({Error}); keeping the saved copy", type, paper, companyId, error);
                            return row;
                        }
                        row ??= _db.LayoutPackageCaches.Add(new LayoutPackageCache { AppUserCompanyId = companyId, DocumentType = type, PaperSize = paper }).Entity;
                        row.PluginLayoutId = current.PluginLayoutId;
                        row.Version = current.IsStandardDesign ? 0 : current.Version;
                        row.Hash = string.IsNullOrWhiteSpace(current.Hash) ? package.Hash : current.Hash;
                        row.IsStandardDesign = current.IsStandardDesign;
                        row.PackageJson = current.Package!;
                        row.CheckedAt = row.UpdatedAt = DateTime.UtcNow;
                        await _db.SaveChangesAsync(ct);
                        _logger.LogInformation("Layout {Type} {Paper} for company {CompanyId} is now {What}", type, paper, companyId,
                            current.IsStandardDesign ? "the Fiscalize standard design" : $"version {current.Version}");
                        return row;

                    default:
                        _logger.LogWarning("Layout {Type} {Paper} for company {CompanyId}: {Problem}; keeping the saved copy", type, paper, companyId, fetch.Problem ?? "no answer");
                        return row;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>Sends the platform the fields this app fills, plus the organisation's tracking categories. Once a day per company. Never throws.</summary>
        private async Task UploadFieldsIfDueAsync(int companyId, AppUserDeviceConfig device, XeroAuthModel? xero, CancellationToken ct)
        {
            if (FieldsSentAt.TryGetValue(companyId, out var sent) && DateTime.UtcNow - sent < TimeSpan.FromDays(1)) return;
            try
            {
                var fields = XeroLayoutMapper.Fields
                    .Select(f => new PluginFieldUploadModel { Key = f.Key, Label = f.Label, Restricted = XeroLayoutMapper.Restricted.Contains(f.Key) })
                    .ToList();
                if (xero != null)
                {
                    try
                    {
                        foreach (var category in await TrackingCategoryService.ListActiveAsync(xero))
                            fields.Add(new PluginFieldUploadModel
                            {
                                Key = XeroLayoutMapper.TrackingKey(category.Name!), Label = category.Name!.Trim(), Group = "Tracking categories"
                            });
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogInformation(ex, "Could not read tracking categories for company {CompanyId}; sending the built-in fields only", companyId);
                    }
                }

                await _device.CallAsync(device, auth => PlatformLayoutClient.UploadFieldsAsync(auth,
                    new UploadPluginFieldsModel { PluginType = XeroLayoutMapper.PluginType, Fields = fields }), ct);
                // Without Xero (no tracking categories read) try again within the hour; otherwise once a day.
                FieldsSentAt[companyId] = xero != null ? DateTime.UtcNow : DateTime.UtcNow - TimeSpan.FromHours(23);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not send the layout field list for company {CompanyId}", companyId);
            }
        }
    }
}

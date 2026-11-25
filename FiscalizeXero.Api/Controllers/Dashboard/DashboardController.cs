using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Filters;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.Sync;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Shared.Models.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FiscalizeXero.Api.Controllers.Dashboard
{
    /// <summary>
    /// The dashboard's figures. Read entirely from this service's database so opening the dashboard never spends any
    /// of the organisation's Xero calls.
    /// </summary>
    [Route("api/dashboard")]
    [ApiController]
    [Authorize]
    [CompanyScoped]
    public class DashboardController : ControllerBase
    {
        /// <summary>Zimbabwe has no daylight saving: days are counted at a fixed UTC+2 (CAT).</summary>
        internal static readonly TimeSpan Cat = TimeSpan.FromHours(2);
        private const int Days = 14;

        private readonly DataContext _db;
        private readonly ICurrentUserService _currentUser;
        private readonly XeroOptions _xero;
        private readonly XeroSyncOptions _sync;

        public DashboardController(DataContext db, ICurrentUserService currentUser,
            IOptions<XeroOptions> xero, IOptions<XeroSyncOptions> sync)
        {
            _db = db;
            _currentUser = currentUser;
            _xero = xero.Value;
            _sync = sync.Value;
        }

        [HttpGet("summary")]
        [ProducesResponseType(typeof(DashboardSummaryModel), StatusCodes.Status200OK)]
        public async Task<ActionResult<DashboardSummaryModel>> SummaryAsync()
        {
            var model = new DashboardSummaryModel();
            var config = await _currentUser.GetCurrentClientConfigAsync();
            var anchor = await _currentUser.GetCompanyAnchorIdAsync();

            model.Connection.SyncEnabled = _sync.Enabled;
            model.Connection.SyncIntervalHours = _sync.IntervalHours;
            model.Connection.WebhooksConfigured = Services.Health.WebhookKeyHealthCheck.IsConfigured(_xero.WebhookKey);
            if (config != null && config.IsLive)
            {
                model.Connection.Connected = true;
                model.Connection.TenantName = config.TenantName;
                model.Connection.AutoFiscalizeInvoices = config.AutoFiscalizeInvoices;
                model.Connection.AutoFiscalizeCreditNotes = config.AutoFiscalizeCreditNotes;
                model.Connection.LastSyncAt = Utc(config.LastSyncRunAt);
                model.Connection.LastSyncError = config.LastSyncError;
                var tenant = config.TenantId!.Value;
                model.Connection.LastWebhookAt = Utc(await _db.WebhookEvents.AsNoTracking()
                    .Where(e => e.TenantId == tenant && e.Source == WebhookEventSource.Webhook)
                    .MaxAsync(e => (DateTime?)e.ReceivedAt));
            }

            if (anchor == null) return Ok(model);
            var company = anchor.Value;

            // Only the columns shown: the device row also holds the encrypted API secret.
            var device = await _db.DeviceConfigs.AsNoTracking()
                .Where(d => d.AppUserCompanyId == company)
                .OrderByDescending(d => d.IsDefault).ThenByDescending(d => d.DateUpdated ?? d.DateConfigured)
                .Select(d => new { d.DeviceNumber, d.DeviceId })
                .FirstOrDefaultAsync();

            var records = _db.FiscalRecords.AsNoTracking().Where(r => r.AppUserCompanyId == company);

            if (device != null)
            {
                var latestQr = await records.OrderByDescending(r => r.FiscalizedAt).Select(r => r.QrCodeUrl).FirstOrDefaultAsync();
                model.Device = new DashboardDeviceModel
                {
                    DeviceNumber = device.DeviceNumber,
                    DeviceId = device.DeviceId,
                    Environment = EnvironmentOf(latestQr),
                };
            }

            var nowCat = DateTime.UtcNow + Cat;
            var todayStartUtc = nowCat.Date - Cat;
            var monthStartUtc = new DateTime(nowCat.Year, nowCat.Month, 1) - Cat;
            var windowStartUtc = todayStartUtc.AddDays(-(Days - 1));
            var earliest = monthStartUtc < windowStartUtc ? monthStartUtc : windowStartUtc;

            // A company fiscalises at most a few thousand documents a month: reading the timestamps is cheaper and
            // simpler than a timezone-aware GROUP BY in SQL.
            var stamps = await records.Where(r => r.FiscalizedAt >= earliest)
                .Select(r => new { r.FiscalizedAt, r.DocumentType }).ToListAsync();

            model.Counts.Total = await records.CountAsync();
            model.Counts.Today = stamps.Count(s => s.FiscalizedAt >= todayStartUtc);
            model.Counts.Last7Days = stamps.Count(s => s.FiscalizedAt >= todayStartUtc.AddDays(-6));
            var month = stamps.Where(s => s.FiscalizedAt >= monthStartUtc).ToList();
            model.Counts.ThisMonth = month.Count;
            model.Counts.InvoicesThisMonth = month.Count(s => s.DocumentType == FiscalDocumentType.Invoice);
            model.Counts.CreditNotesThisMonth = month.Count(s => s.DocumentType == FiscalDocumentType.CreditNote);

            var byDay = stamps.Where(s => s.FiscalizedAt >= windowStartUtc)
                .GroupBy(s => (s.FiscalizedAt + Cat).Date)
                .ToDictionary(g => g.Key, g => g.ToList());
            for (var i = 0; i < Days; i++)
            {
                var day = nowCat.Date.AddDays(-(Days - 1) + i);
                var list = byDay.GetValueOrDefault(day);
                model.Daily.Add(new DashboardDayModel
                {
                    Date = day.ToString("yyyy-MM-dd"),
                    Invoices = list?.Count(s => s.DocumentType == FiscalDocumentType.Invoice) ?? 0,
                    CreditNotes = list?.Count(s => s.DocumentType == FiscalDocumentType.CreditNote) ?? 0,
                });
            }

            model.Recent = await records.OrderByDescending(r => r.FiscalizedAt).ThenByDescending(r => r.Id).Take(8)
                .Select(r => new DashboardReceiptModel
                {
                    DocumentId = r.DocumentId,
                    DocumentType = r.DocumentType,
                    DocumentNumber = r.DocumentNumber,
                    ReceiptNumber = r.ReceiptNumber,
                    VerificationCode = r.VerificationCode,
                    FiscalizedAt = r.FiscalizedAt,
                }).ToListAsync();
            foreach (var r in model.Recent) r.FiscalizedAt = Utc(r.FiscalizedAt)!.Value;

            // A failure row is cleared when the document fiscalises; skip any that slipped through with a record.
            var failures = _db.FiscalFailures.AsNoTracking()
                .Where(f => f.AppUserCompanyId == company && !records.Any(r => r.DocumentId == f.DocumentId));
            model.FailureCount = await failures.CountAsync();
            model.Failures = await failures.OrderByDescending(f => f.LastFailedAt).Take(5)
                .Select(f => new DashboardFailureModel
                {
                    DocumentId = f.DocumentId,
                    DocumentType = f.DocumentType,
                    Category = f.Category,
                    Message = f.Message,
                    Attempts = f.Attempts,
                    LastFailedAt = f.LastFailedAt,
                }).ToListAsync();
            foreach (var f in model.Failures) f.LastFailedAt = Utc(f.LastFailedAt)!.Value;

            return Ok(model);
        }

        internal static string? EnvironmentOf(string? qrUrl)
        {
            if (string.IsNullOrWhiteSpace(qrUrl) || !Uri.TryCreate(qrUrl, UriKind.Absolute, out var uri)) return null;
            return uri.Host.Contains("test", StringComparison.OrdinalIgnoreCase) ? "test" : "production";
        }

        private static DateTime? Utc(DateTime? value) =>
            value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
    }
}

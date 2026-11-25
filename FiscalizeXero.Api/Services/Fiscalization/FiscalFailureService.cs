using FiscalizeXero.Api.Data;
using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Xero.Exceptions;
using FiscalizeXero.Shared.Models.Fiscalize;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    public interface IFiscalFailureService
    {
        /// <summary>Stores (or updates) the latest failure for a document. Never throws.</summary>
        Task RecordAsync(int appUserCompanyId, string documentType, string documentId, Exception error, string source);

        /// <summary>Removes the failure for a document after it fiscalised. Never throws.</summary>
        Task ClearAsync(int appUserCompanyId, string documentType, string documentId);

        Task<FiscalFailureModel?> GetAsync(int appUserCompanyId, string documentType, string documentId);

        /// <summary>Failures for a page of documents; documents without one (or already fiscalised) are absent.</summary>
        Task<List<FiscalFailureModel>> GetManyAsync(int appUserCompanyId, string documentType, IReadOnlyCollection<string> documentIds);

        /// <summary>
        /// Brings the automatic retry of a company's failures in the given categories forward to now: called when
        /// the thing they were waiting for is saved (buyer details, an HS code, the original invoice fiscalised).
        /// Never throws.
        /// </summary>
        Task RetrySoonAsync(int appUserCompanyId, params string[] categories);

        /// <summary>Keeps the automatic triggers away from a document (after a fiscal cancel). Never throws.</summary>
        Task SuppressAsync(int appUserCompanyId, string documentType, string documentId, string reason);

        /// <summary>Lets the automatic triggers handle a document again (a person fiscalised it). Never throws.</summary>
        Task UnsuppressAsync(int appUserCompanyId, string documentId);
    }

    public sealed class FiscalFailureService : IFiscalFailureService
    {
        private const int MaxMessage = 2000;
        private const int MaxDetails = 4000;

        private readonly DataContext _context;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<FiscalFailureService> _logger;

        public FiscalFailureService(DataContext context, IServiceScopeFactory scopes, ILogger<FiscalFailureService> logger)
        {
            _context = context;
            _scopes = scopes;
            _logger = logger;
        }

        public async Task RecordAsync(int appUserCompanyId, string documentType, string documentId, Exception error, string source)
        {
            var (category, message, details) = Classify(error);
            try
            {
                // A fresh context: the failed fiscalisation may have left the request's context with unsaved or broken
                // changes, and those must neither block this write nor be saved by it.
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var row = await db.FiscalFailures.FirstOrDefaultAsync(f =>
                        f.AppUserCompanyId == appUserCompanyId && f.DocumentType == documentType && f.DocumentId == documentId);
                    var now = DateTime.UtcNow;
                    if (row == null)
                    {
                        row = new FiscalFailure
                        {
                            AppUserCompanyId = appUserCompanyId, DocumentType = documentType, DocumentId = documentId,
                            FirstFailedAt = now, Attempts = 0
                        };
                        db.FiscalFailures.Add(row);
                    }
                    row.Category = category;
                    row.Message = Truncate(message, MaxMessage);
                    row.Details = details.Count == 0 ? null : Truncate(string.Join('\n', details), MaxDetails);
                    row.Source = source;
                    row.Attempts += 1;
                    row.LastFailedAt = now;
                    row.NextRetryAt = FailureRetryPolicy.Next(error, category, row.Attempts, now);
                    try
                    {
                        await db.SaveChangesAsync();
                        return;
                    }
                    catch (DbUpdateException) when (attempt == 0)
                    {
                        // Another attempt for the same document inserted the row first: reload and update it instead.
                        db.ChangeTracker.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not record the fiscalisation failure for {Type} {DocumentId} (company {CompanyId})",
                    documentType, documentId, appUserCompanyId);
            }
        }

        public async Task ClearAsync(int appUserCompanyId, string documentType, string documentId)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                await db.FiscalFailures
                    .Where(f => f.AppUserCompanyId == appUserCompanyId && f.DocumentType == documentType && f.DocumentId == documentId)
                    .ExecuteDeleteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not clear the fiscalisation failure for {Type} {DocumentId} (company {CompanyId})",
                    documentType, documentId, appUserCompanyId);
            }
        }

        public async Task RetrySoonAsync(int appUserCompanyId, params string[] categories)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                var now = DateTime.UtcNow;
                var changed = await db.FiscalFailures
                    .Where(f => f.AppUserCompanyId == appUserCompanyId && categories.Contains(f.Category))
                    .ExecuteUpdateAsync(u => u.SetProperty(f => f.NextRetryAt, now));
                if (changed > 0)
                    _logger.LogInformation("Brought {Count} failed document(s) forward for retry (company {CompanyId}, {Categories})",
                        changed, appUserCompanyId, string.Join(",", categories));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not schedule failed documents for retry (company {CompanyId})", appUserCompanyId);
            }
        }

        public async Task SuppressAsync(int appUserCompanyId, string documentType, string documentId, string reason)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                await FiscalSuppressions.AddAsync(db, appUserCompanyId, documentType, documentId, reason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not stop automatic fiscalisation of {Type} {DocumentId} (company {CompanyId})", documentType, documentId, appUserCompanyId);
            }
        }

        public async Task UnsuppressAsync(int appUserCompanyId, string documentId)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                await FiscalSuppressions.RemoveAsync(db, appUserCompanyId, documentId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not re-enable automatic fiscalisation of {DocumentId} (company {CompanyId})", documentId, appUserCompanyId);
            }
        }

        public async Task<FiscalFailureModel?> GetAsync(int appUserCompanyId, string documentType, string documentId)
        {
            var row = await _context.FiscalFailures.AsNoTracking().FirstOrDefaultAsync(f =>
                f.AppUserCompanyId == appUserCompanyId && f.DocumentType == documentType && f.DocumentId == documentId);
            if (row == null) return null;
            var fiscalised = await _context.FiscalRecords.AsNoTracking().AnyAsync(r =>
                r.AppUserCompanyId == appUserCompanyId && r.DocumentId == documentId && r.DocumentType == documentType && r.ReceiptNumber != null);
            return fiscalised ? null : ToModel(row);
        }

        public async Task<List<FiscalFailureModel>> GetManyAsync(int appUserCompanyId, string documentType, IReadOnlyCollection<string> documentIds)
        {
            if (documentIds.Count == 0) return new List<FiscalFailureModel>();
            var ids = documentIds.ToList();
            var rows = await _context.FiscalFailures.AsNoTracking()
                .Where(f => f.AppUserCompanyId == appUserCompanyId && f.DocumentType == documentType && ids.Contains(f.DocumentId))
                .ToListAsync();
            if (rows.Count == 0) return new List<FiscalFailureModel>();

            // A document fiscalised since (for example by a colleague) is no longer failed.
            var failedIds = rows.Select(r => r.DocumentId).ToList();
            var fiscalised = (await _context.FiscalRecords.AsNoTracking()
                    .Where(r => r.AppUserCompanyId == appUserCompanyId && r.DocumentType == documentType && r.ReceiptNumber != null && failedIds.Contains(r.DocumentId))
                    .Select(r => r.DocumentId)
                    .ToListAsync())
                .ToHashSet();
            return rows.Where(r => !fiscalised.Contains(r.DocumentId)).Select(ToModel).ToList();
        }

        internal static FiscalFailureModel ToModel(FiscalFailure f) => new()
        {
            DocumentId = f.DocumentId,
            DocumentType = f.DocumentType,
            Category = f.Category,
            Message = f.Message,
            Details = string.IsNullOrEmpty(f.Details) ? new List<string>() : f.Details.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList(),
            Source = f.Source,
            Attempts = f.Attempts,
            FirstFailedAt = DateTime.SpecifyKind(f.FirstFailedAt, DateTimeKind.Utc),
            LastFailedAt = DateTime.SpecifyKind(f.LastFailedAt, DateTimeKind.Utc)
        };

        /// <summary>What kind of failure an exception is, its readable message, and its individual problems.</summary>
        internal static (string Category, string Message, List<string> Details) Classify(Exception error)
        {
            switch (error)
            {
                case BuyerDetailsException buyer:
                    return (FiscalFailureCategory.Buyer, buyer.Message, buyer.Details.ToList());

                case FiscalizeApiException platform:
                {
                    var details = platform.Errors.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct().ToList();
                    var message = details.Count > 0
                        ? $"Fiscalize platform: {platform.Message.TrimEnd('.')}. {string.Join(" ", details.Select(d => d.TrimEnd('.') + "."))}"
                        : $"Fiscalize platform: {platform.Message}";
                    var text = (platform.Message + " " + string.Join(" ", details)).ToLowerInvariant();
                    var category = MentionsAny(text, "buyer", "tin", "vat", "phone", "email", "province", "street", "house", "registered name", "trade name")
                        ? FiscalFailureCategory.Buyer
                        : MentionsAny(text, "hs code", "hscode") ? FiscalFailureCategory.HsCode
                        : FiscalFailureCategory.Platform;
                    return (category, message, details);
                }

                case XeroApiException xero:
                {
                    // The correlation id is what Xero support asks for to trace a failed call.
                    var message = $"Xero: {xero.Message}" + (string.IsNullOrEmpty(xero.CorrelationId) ? string.Empty : $" [xero_correlation_id {xero.CorrelationId}]");
                    return (FiscalFailureCategory.Xero, message, xero.ValidationErrors.ToList());
                }

                case HttpRequestException or TaskCanceledException or TimeoutException:
                    return (FiscalFailureCategory.Network,
                        "The Fiscalize platform or Xero didn't answer in time. This usually clears on its own; try again in a minute.",
                        new List<string> { error.Message });
            }

            // Only messages written for people are shown; anything else (framework, database) gets plain words and
            // its type as the detail, with the full exception in the log.
            if (error is not FiscalizeXero.Api.Exceptions.UserFacingException)
                return (FiscalFailureCategory.Other,
                    "Something unexpected went wrong while fiscalising. It is tried again automatically; if it keeps failing, contact support.",
                    new List<string> { error.GetType().Name });
            var msg = error.Message;
            var lower = msg.ToLowerInvariant();
            var cat = MentionsAny(lower, "hs code") ? FiscalFailureCategory.HsCode
                : MentionsAny(lower, "device", "api key") ? FiscalFailureCategory.Device
                : MentionsAny(lower, "original", "reverses", "allocated", "fiscalize that invoice first", "not been fiscalized here") ? FiscalFailureCategory.OriginalSale
                : MentionsAny(lower, "xero is not connected", "xero connection", "reconnect xero", "token") ? FiscalFailureCategory.Xero
                : FiscalFailureCategory.Other;
            return (cat, msg, new List<string>());
        }

        /// <summary>Whole words only, so "tin" doesn't match "existing".</summary>
        private static bool MentionsAny(string text, params string[] words)
            => words.Any(w => System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{System.Text.RegularExpressions.Regex.Escape(w)}\b"));

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    }
}

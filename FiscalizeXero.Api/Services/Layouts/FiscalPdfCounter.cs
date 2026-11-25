using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.FiscalDocuments;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Services.Layouts
{
    /// <summary>
    /// Counts the fiscal PDFs attached in Xero by kind, for the platform's Connected apps page. Counts are added in the
    /// database in one statement (no read-modify-write), and the Connected apps report subtracts exactly what it sent, so a
    /// PDF attached while a report is in flight is never lost.
    /// </summary>
    public interface IFiscalPdfCounter
    {
        Task CountAsync(long clientConfigId, string kind, string? fallbackReason);
        /// <summary>Takes back what a successful report delivered.</summary>
        Task SubtractAsync(long clientConfigId, int withLayout, int withStandard, int stamped, int fallbacks);
    }

    public class FiscalPdfCounter : IFiscalPdfCounter
    {
        private readonly DataContext _db;
        private readonly ILogger<FiscalPdfCounter> _logger;

        public FiscalPdfCounter(DataContext db, ILogger<FiscalPdfCounter> logger) { _db = db; _logger = logger; }

        public async Task CountAsync(long clientConfigId, string kind, string? fallbackReason)
        {
            try
            {
                var configs = _db.Configs.Where(c => c.Id == clientConfigId);
                switch (kind)
                {
                    case FiscalPdfKinds.Layout:
                        await configs.ExecuteUpdateAsync(s => s.SetProperty(c => c.PendingPdfsWithLayout, c => c.PendingPdfsWithLayout + 1));
                        break;
                    case FiscalPdfKinds.StandardDesign:
                    case FiscalPdfKinds.Platform:
                        await configs.ExecuteUpdateAsync(s => s.SetProperty(c => c.PendingPdfsWithStandard, c => c.PendingPdfsWithStandard + 1));
                        break;
                    default:
                        await configs.ExecuteUpdateAsync(s => s.SetProperty(c => c.PendingPdfsStamped, c => c.PendingPdfsStamped + 1));
                        break;
                }
                if (fallbackReason != null)
                {
                    var reason = fallbackReason.Length <= 500 ? fallbackReason : fallbackReason[..500];
                    var now = DateTime.UtcNow;
                    await configs.ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.PendingPdfFallbacks, c => c.PendingPdfFallbacks + 1)
                        .SetProperty(c => c.LastPdfFallbackAt, now)
                        .SetProperty(c => c.LastPdfFallbackReason, reason));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not count a fiscal PDF for connection {ConfigId}", clientConfigId);
            }
        }

        public Task SubtractAsync(long clientConfigId, int withLayout, int withStandard, int stamped, int fallbacks) =>
            _db.Configs.Where(c => c.Id == clientConfigId).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.PendingPdfsWithLayout, c => c.PendingPdfsWithLayout - withLayout)
                .SetProperty(c => c.PendingPdfsWithStandard, c => c.PendingPdfsWithStandard - withStandard)
                .SetProperty(c => c.PendingPdfsStamped, c => c.PendingPdfsStamped - stamped)
                .SetProperty(c => c.PendingPdfFallbacks, c => c.PendingPdfFallbacks - fallbacks));
    }
}

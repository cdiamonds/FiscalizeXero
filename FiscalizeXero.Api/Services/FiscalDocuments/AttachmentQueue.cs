using System.Threading.Channels;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.ClientConfigs;
using FiscalizeXero.Api.Services.XeroAuth;

namespace FiscalizeXero.Api.Services.FiscalDocuments
{
    /// <summary>A stamped-PDF attachment to perform after the fiscalisation response has gone back to the caller.</summary>
    public sealed record AttachmentJob(long ClientConfigId, int AppUserCompanyId, string DocumentId, string DocumentNumber);

    public interface IAttachmentQueue
    {
        void Enqueue(AttachmentJob job);
        IAsyncEnumerable<AttachmentJob> ReadAllAsync(CancellationToken ct);
    }

    public class AttachmentQueue : IAttachmentQueue
    {
        private readonly Channel<AttachmentJob> _channel = Channel.CreateUnbounded<AttachmentJob>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        public void Enqueue(AttachmentJob job) => _channel.Writer.TryWrite(job);
        public IAsyncEnumerable<AttachmentJob> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
    }

    /// <summary>
    /// Builds and attaches the stamped PDF in the background. Fetching Xero's PDF, stamping it and uploading it
    /// is the slow part of a fiscalisation and nothing on screen depends on it, so the user gets the receipt straight
    /// away and the page watches the attachment status. One retry after a short pause; after that the document page
    /// shows "Not attached" with "Attach now".
    /// </summary>
    public class AttachmentWorker : BackgroundService
    {
        private readonly IAttachmentQueue _queue;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<AttachmentWorker> _logger;

        public AttachmentWorker(IAttachmentQueue queue, IServiceScopeFactory scopes, ILogger<AttachmentWorker> logger)
        {
            _queue = queue; _scopes = scopes; _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in _queue.ReadAllAsync(stoppingToken))
            {
                for (var attempt = 1; attempt <= 2 && !stoppingToken.IsCancellationRequested; attempt++)
                {
                    var warning = await RunAsync(job, stoppingToken);
                    // A PDF was attached (the stamp in place of an unusable layout counts): nothing to retry.
                    if (warning == null || warning.StartsWith(FiscalDocumentService.FallbackPrefix, StringComparison.Ordinal)) break;
                    _logger.LogWarning("Attachment of {DocNo} (attempt {Attempt}) not done: {Warning}", job.DocumentNumber, attempt, warning);
                    if (attempt == 1)
                    {
                        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch (OperationCanceledException) { return; }
                    }
                }
            }
        }

        private async Task<string?> RunAsync(AttachmentJob job, CancellationToken ct)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var configs = scope.ServiceProvider.GetRequiredService<IClientConfigService>();
                var authBuilder = scope.ServiceProvider.GetRequiredService<IXeroAuthBuilder>();
                var records = scope.ServiceProvider.GetRequiredService<IFiscalRecordRepository>();
                var documents = scope.ServiceProvider.GetRequiredService<IFiscalDocumentService>();

                var config = await configs.GetByIdAsync(job.ClientConfigId);
                if (config == null) return "The Xero connection no longer exists.";
                var auth = await authBuilder.BuildAsync(config);
                if (auth == null) return "The Xero connection is not authorised.";
                var record = await records.GetAsync(job.AppUserCompanyId, job.DocumentId);
                if (record == null) return "Fiscal record no longer exists.";

                return await documents.AttachStampedPdfAsync(config, auth, record);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Background attachment of {DocNo} failed", job.DocumentNumber);
                return ex.Message;
            }
        }
    }
}

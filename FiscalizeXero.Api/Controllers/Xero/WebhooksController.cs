using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.FiscalDocuments;
using FiscalizeXero.Api.Services.Sync;
using FiscalizeXero.Api.Services.Webhooks;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Shared.Models.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FiscalizeXero.Api.Controllers.Xero
{
    /// <summary>
    /// Receives Xero webhook deliveries and reports webhook health to the Webhooks page.
    /// </summary>
    [Route("api/xero/webhooks")]
    [ApiController]
    [FiscalizeXero.Api.Filters.CompanyScoped]
    public class WebhooksController : ControllerBase
    {
        public const string CallbackPath = "/api/xero/webhooks";

        /// <summary>Xero's deliveries are a few KB; anything bigger is not Xero.</summary>
        private const int MaxBodyBytes = 1_048_576;

        private readonly DataContext _db;
        private readonly IWebhookQueue _queue;
        private readonly ICurrentUserService _currentUserService;
        private readonly XeroOptions _xero;
        private readonly XeroSyncOptions _sync;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WebhooksController> _logger;

        public WebhooksController(
            DataContext db,
            IWebhookQueue queue,
            ICurrentUserService currentUserService,
            IOptions<XeroOptions> xeroOptions,
            IOptions<XeroSyncOptions> syncOptions,
            IConfiguration configuration,
            ILogger<WebhooksController> logger)
        {
            _db = db;
            _queue = queue;
            _currentUserService = currentUserService;
            _xero = xeroOptions.Value;
            _sync = syncOptions.Value;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Xero calls this for every invoice and credit note change, and for its "intent to receive" check, which
        /// sends correctly and incorrectly signed test deliveries and expects 200 with an empty body for the good
        /// ones and 401 for the bad ones, each within 5 seconds. So: the signature is checked on the raw bytes of the
        /// body, anything that does not verify (a wrong or missing signature, or no webhook key configured here) is
        /// answered 401, and everything that verifies is stored, queued and answered 200 with an empty body at once.
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ReceiveAsync()
        {
            var body = await ReadBodyAsync();
            var signature = Request.Headers[XeroWebhookSignature.HeaderName].FirstOrDefault();
            if (body == null || !XeroWebhookSignature.Verify(body, signature, _xero.WebhookKey))
            {
                if (string.IsNullOrWhiteSpace(_xero.WebhookKey))
                    _logger.LogWarning("Webhook delivery refused: Xero:WebhookKey is not configured");
                else
                    _logger.LogWarning("Webhook delivery rejected: signature mismatch from {Ip}", HttpContext.Connection.RemoteIpAddress);
                // Status only, no body: [ApiController] would turn a 401 result object into a problem-details document.
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                return new EmptyResult();
            }

            IReadOnlyList<XeroDocumentChange> changes;
            try
            {
                changes = XeroWebhookPayload.Parse(System.Text.Encoding.UTF8.GetString(body));
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Webhook delivery had a malformed body");
                return new OkResult(); // signed by Xero, nothing we can do with it; do not trigger retries
            }

            if (changes.Count > 0)
            {
                var events = changes.Select(c => new WebhookEvent
                {
                    TenantId = c.TenantId,
                    EntityName = XeroDocumentTypes.EntityName(c.DocumentType),
                    EntityId = FiscalDocumentType.Key(c.DocumentId),
                    Operation = c.Operation,
                    LastUpdated = c.EventDateUtc is { Length: > 40 } when ? when[..40] : c.EventDateUtc,
                    Source = WebhookEventSource.Webhook
                }).ToList();

                _db.WebhookEvents.AddRange(events);
                await _db.SaveChangesAsync();
                foreach (var evt in events) _queue.Enqueue(evt.Id);
            }

            _logger.LogInformation("Webhook delivery accepted: {Count} change(s) queued", changes.Count);
            return new OkResult();
        }

        /// <summary>The exact bytes Xero sent (the signature covers them), or null when the body is too large or could not be read.</summary>
        private async Task<byte[]?> ReadBodyAsync()
        {
            try
            {
                if (Request.ContentLength > MaxBodyBytes) return null;
                using var buffer = new MemoryStream();
                var chunk = new byte[16 * 1024];
                int read;
                while ((read = await Request.Body.ReadAsync(chunk, HttpContext.RequestAborted)) > 0)
                {
                    if (buffer.Length + read > MaxBodyBytes) return null;
                    buffer.Write(chunk, 0, read);
                }
                return buffer.ToArray();
            }
            catch (Exception ex) when (ex is IOException or BadHttpRequestException or OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>Setup details and recent deliveries for the signed-in user's connected company.</summary>
        [HttpGet("status")]
        [Authorize]
        [ProducesResponseType(typeof(WebhookStatusModel), StatusCodes.Status200OK)]
        public async Task<ActionResult<WebhookStatusModel>> GetStatusAsync()
        {
            var config = await _currentUserService.GetCurrentClientConfigAsync();

            var model = new WebhookStatusModel
            {
                ListenerConfigured = Services.Health.WebhookKeyHealthCheck.IsConfigured(_xero.WebhookKey),
                CallbackUrl = BuildCallbackUrl(),
                SyncEnabled = _sync.Enabled,
                SyncIntervalHours = _sync.IntervalHours,
            };

            if (config == null || !config.IsLive)
                return Ok(model);

            model.Connected = true;
            model.TenantId = config.TenantId;
            model.TenantName = config.TenantName;
            model.AutoFiscalizeInvoices = config.AutoFiscalizeInvoices;
            model.AutoFiscalizeCreditNotes = config.AutoFiscalizeCreditNotes;
            // When the check last ran, not its cursor (which can sit a little earlier, or be set at connect time).
            model.LastSyncAt = AsUtc(config.LastSyncRunAt);
            model.LastSyncError = config.LastSyncError;
            model.SyncRetryAt = AsUtc(config.SyncRetryAt);

            var tenant = config.TenantId!.Value;
            var since = DateTime.UtcNow.AddHours(-24);

            var counts = await _db.WebhookEvents
                .Where(e => e.TenantId == tenant && e.ReceivedAt >= since)
                .GroupBy(e => e.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            foreach (var c in counts)
            {
                model.Last24Hours.Received += c.Count;
                switch (c.Status)
                {
                    case WebhookEventStatus.Processed: model.Last24Hours.Processed = c.Count; break;
                    case WebhookEventStatus.Skipped: model.Last24Hours.Skipped = c.Count; break;
                    case WebhookEventStatus.Failed: model.Last24Hours.Failed = c.Count; break;
                    case WebhookEventStatus.Pending: model.Last24Hours.Pending = c.Count; break;
                }
            }

            // Stored as UTC; stamp the kind so the JSON carries a Z and browsers render local time.
            var lastReceived = await _db.WebhookEvents
                .Where(e => e.TenantId == tenant)
                .MaxAsync(e => (DateTime?)e.ReceivedAt);
            model.LastReceivedAt = AsUtc(lastReceived);

            model.Recent = await _db.WebhookEvents
                .Where(e => e.TenantId == tenant)
                .OrderByDescending(e => e.ReceivedAt).ThenByDescending(e => e.Id)
                .Take(25)
                .Select(e => new WebhookEventModel
                {
                    Id = e.Id,
                    EntityName = e.EntityName,
                    EntityId = e.EntityId,
                    DocumentNumber = e.DocumentNumber,
                    Operation = e.Operation,
                    Source = e.Source,
                    Status = e.Status,
                    Detail = e.Detail,
                    ReceivedAt = e.ReceivedAt,
                    ProcessedAt = e.ProcessedAt
                })
                .ToListAsync();

            foreach (var e in model.Recent)
            {
                e.ReceivedAt = AsUtc(e.ReceivedAt)!.Value;
                e.ProcessedAt = AsUtc(e.ProcessedAt);
            }

            return Ok(model);
        }

        private static DateTime? AsUtc(DateTime? value) =>
            value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

        private string BuildCallbackUrl()
        {
            var baseUrl = string.IsNullOrWhiteSpace(_xero.WebhookCallbackBaseUrl)
                ? $"{Request.Scheme}://{Request.Host}"
                : _xero.WebhookCallbackBaseUrl.TrimEnd('/');
            var pathBase = (_configuration["PathBase"] ?? string.Empty).TrimEnd('/');
            return $"{baseUrl}{pathBase}{CallbackPath}";
        }
    }
}

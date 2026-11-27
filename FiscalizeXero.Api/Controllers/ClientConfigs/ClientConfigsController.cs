using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Security;
using FiscalizeXero.Api.Services.Auths;
using FiscalizeXero.Api.Services.ClientConfigs;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.HsCodes;
using FiscalizeXero.Api.Services.Sync;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Shared.Models.ClientConfigs.Manage;
using FiscalizeXero.Shared.Models.Fiscalize;
using FiscalizeXero.Xero.Exceptions;
using FiscalizeXero.Xero.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FiscalizeXero.Api.Controllers.ClientConfigs
{
    /// <summary>The company's Xero connection (connect, re-authorise, disconnect) and its fiscalisation settings.</summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [FiscalizeXero.Api.Filters.CompanyScoped]
    public class ClientConfigsController : ControllerBase
    {
        private readonly IClientConfigService _clientConfigService;
        private readonly ICurrentUserService _currentUserService;
        private readonly IXeroAuthService _xeroAuthService;
        private readonly IXeroConnections _xeroConnections;
        private readonly IAuthService _authService;
        private readonly IXeroSyncRequests _sync;
        private readonly XeroOptions _xero;
        private readonly ILogger<ClientConfigsController> _logger;

        public ClientConfigsController(
            IClientConfigService clientConfigService,
            ICurrentUserService currentUserService,
            IXeroAuthService xeroAuthService,
            IXeroConnections xeroConnections,
            IAuthService authService,
            IXeroSyncRequests sync,
            IOptions<XeroOptions> xeroOptions,
            ILogger<ClientConfigsController> logger)
        {
            _sync = sync;
            _clientConfigService = clientConfigService;
            _currentUserService = currentUserService;
            _xeroAuthService = xeroAuthService;
            _xeroConnections = xeroConnections;
            _authService = authService;
            _xero = xeroOptions.Value;
            _logger = logger;
        }

        /// <summary>
        /// Public facts about this server's Xero app: what an administrator registers at developer.xero.com, and
        /// whether the server is ready to connect. No secret is ever returned.
        /// </summary>
        [HttpGet("xero-app-config")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult GetXeroAppConfig()
        {
            return Ok(new
            {
                redirectUri = _xero.RedirectUri,
                scope = _xero.Scope,
                webhookCallbackBaseUrl = _xero.WebhookCallbackBaseUrl,
                // True when the server holds the app's client id and secret, so users can connect.
                configured = _xeroAuthService.IsConfigured
            });
        }

        /// <summary>
        /// Disconnect Xero for the active company. The company stays on the user's account; this organisation's
        /// connection is removed at Xero (other organisations the same Xero user connected are untouched) and the
        /// connection record deleted.
        /// </summary>
        [HttpDelete("current")]
        [ProducesResponseType(typeof(DisconnectResultModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult> ResetCurrentClientConfigAsync()
        {
            var user = await _currentUserService.GetCurrentUserAsync();
            if (user == null) return Unauthorized();

            var config = await _currentUserService.GetCurrentClientConfigAsync();
            if (config == null)
                return NotFound(new ErrorModel { Code = 404, Message = "Xero is not connected for the current company." });

            return await DisconnectAsync(user, config);
        }

        /// <summary>
        /// Disconnect Xero for any company the current user belongs to. Idempotent: a company that is
        /// already disconnected returns 204. The company itself remains on the user's account.
        /// </summary>
        [HttpDelete("by-company/{companyId}")]
        [ProducesResponseType(typeof(DisconnectResultModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult> DisconnectForCompanyAsync(long companyId)
        {
            var user = await _currentUserService.GetCurrentUserAsync();
            if (user == null) return Unauthorized();

            var company = user.Companies.FirstOrDefault(c => c.CompanyId == companyId);
            if (company == null)
                return NotFound(new ErrorModel { Code = 404, Message = "Company not found in your account" });

            // The membership filter only re-checks the active company. This acts on any company in the local list,
            // which is not pruned when the platform removes someone, so ask the platform about this one too.
            var verifier = HttpContext.RequestServices.GetService<FiscalizeXero.Api.Services.Membership.ICompanyMembershipVerifier>();
            if (verifier != null && await verifier.CheckAsync(company, HttpContext.RequestAborted) != FiscalizeXero.Api.Services.Membership.MembershipDecision.Allowed)
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorModel { Code = 403, Message = "You are no longer a member of this Fiscalize company, so its Xero connection can't be changed from your account." });

            // The company's connection, whichever colleague set it up: anyone in the company may disconnect it.
            var config = await _clientConfigService.GetForCompanyAsync(company.CompanyId);
            if (config == null) return NoContent();

            return await DisconnectAsync(user, config);
        }

        private static ErrorModel NotYourCompanyError() => new()
        {
            Code = 403,
            Message = "This Xero connection belongs to a Fiscalize company you are not a member of."
        };

        /// <summary>A connection is the whole Fiscalize company's: any user signed in to that company may manage it.</summary>
        private static bool IsMemberOf(AppUser user, ClientConfig config) =>
            config.AppUserCompany != null && user.Companies.Any(c => c.CompanyId == config.AppUserCompany.CompanyId);

        private async Task<ActionResult> DisconnectAsync(AppUser user, ClientConfig config)
        {
            if (!IsMemberOf(user, config))
                return StatusCode(StatusCodes.Status403Forbidden, NotYourCompanyError());
            if (!ConnectionManagers.CanManage(config, user.UserId))
                return StatusCode(StatusCodes.Status403Forbidden, ConnectionManagers.Refusal(config));

            var result = new DisconnectResultModel();
            // Remove this organisation's connection at Xero first, while we still hold a token. Never blocks the
            // disconnect, and never revokes the refresh token: that would remove every organisation the same Xero
            // user connected, which could be another Fiscalize company's.
            if (config.IsLive && !await _xeroAuthService.DisconnectAsync(config))
                result.Warnings.Add("We couldn't confirm the removal on Xero's side. You can also remove Fiscalize under Settings > Connected apps in Xero.");

            // Connected apps on the platform shows this organisation as disconnected (best effort, never holds the disconnect up long).
            try
            {
                using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await HttpContext.RequestServices.GetRequiredService<FiscalizeXero.Api.Services.ConnectedApps.IConnectedAppReportSender>()
                    .SendAsync(config, disconnected: true, reportTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Connected apps report for the disconnect of connection {ConfigId} timed out", config.Id);
            }

            await _clientConfigService.DeleteAsync(config.Id);
            return Ok(result);
        }

        /// <summary>Who manages the company's connection, and whether the signed-in person may change it.</summary>
        [HttpGet("management")]
        [ProducesResponseType(typeof(ConnectionManagementModel), StatusCodes.Status200OK)]
        public async Task<ActionResult<ConnectionManagementModel>> GetManagementAsync()
        {
            var config = await _currentUserService.GetCurrentClientConfigAsync();
            var userId = _currentUserService.GetCurrentUserId();
            return Ok(new ConnectionManagementModel
            {
                CanManage = ConnectionManagers.CanManage(config, userId),
                ConnectedByName = config?.ConnectedByName,
                IsYou = config?.ConnectedByUserId != null && config.ConnectedByUserId == userId,
            });
        }

        [HttpGet("current")]
        [ProducesResponseType(typeof(ClientConfig), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ClientConfig>> GetCurrentAsync()
        {
            var config = await _currentUserService.GetCurrentClientConfigAsync();
            if (config == null)
                return NotFound(new ErrorModel { Code = 404, Message = "Xero is not connected for the current company." });
            return Ok(config);
        }

        /// <summary>
        /// Starts connecting the current user's active company to Xero. Returns the Xero consent URL the user must
        /// open; Xero then sends them back to the redirect URI with <c>code</c> and <c>state</c>, which the web app
        /// passes to <c>oauth/callback</c>. The request needs no body.
        /// </summary>
        [HttpPost("register")]
        [ProducesResponseType(typeof(RegisterClientResponseModel), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<RegisterClientResponseModel>> RegisterClientAsync()
        {
            var user = await _currentUserService.GetCurrentUserAsync();
            if (user?.ActiveCompany == null)
                return NotFound(new ErrorModel { Code = 404, Message = "No active company configured for the current user" });
            if (!_xeroAuthService.IsConfigured)
                return BadRequest(NotConfiguredError());

            // One connection per Fiscalize company: (re)connecting reuses the company's row even when a colleague set
            // it up. Its live tokens are kept until the callback replaces them, so a colleague who opens the consent
            // screen and abandons it doesn't drop the company's connection.
            var config = await _currentUserService.GetCurrentClientConfigAsync();
            var state = OAuthState.New();
            if (config != null)
            {
                config.IsCompanyWide = true;
                config.State = state;
                config = await _clientConfigService.UpdateAsync(config);
            }
            else
            {
                config = await _clientConfigService.CreateAsync(new ClientConfig
                {
                    AppUserCompanyId = await _currentUserService.GetCompanyAnchorIdAsync() ?? user.ActiveCompany.Id,
                    IsCompanyWide = true,
                    State = state
                });
            }

            return Created(string.Empty, new RegisterClientResponseModel
            {
                ClientConfigId = config.Id,
                AuthUrl = _xeroAuthService.BuildConsentUrl(state)!
            });
        }

        /// <summary>
        /// Regenerates the consent URL for the company's existing connection (new scopes, or a lost connection). The
        /// organisation and its settings are kept; the callback replaces the tokens.
        /// </summary>
        [HttpPost("reauth")]
        [ProducesResponseType(typeof(RegisterClientResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<RegisterClientResponseModel>> ReauthorizeAsync()
        {
            var config = await _currentUserService.GetCurrentClientConfigAsync();
            if (config == null)
                return NotFound(new ErrorModel { Code = 404, Message = "Xero is not connected for the current company." });
            if (!_xeroAuthService.IsConfigured)
                return BadRequest(NotConfiguredError());

            var newState = OAuthState.New();
            config.State = newState;
            await _clientConfigService.UpdateAsync(config);

            return Ok(new RegisterClientResponseModel
            {
                ClientConfigId = config.Id,
                AuthUrl = _xeroAuthService.BuildConsentUrl(newState)!
            });
        }

        private static ErrorModel NotConfiguredError() => new()
        {
            Code = 400,
            Message = "This server has no Xero app credentials yet, so Xero can't be connected. Ask your administrator to set Xero:ClientId and Xero:ClientSecret."
        };

        /// <summary>
        /// Xero OAuth callback. Xero's redirect carries only <c>code</c> and <c>state</c>, not the organisation, so:
        /// the code is exchanged, the user's connections are listed, and the organisation this consent was for is
        /// picked. Nothing is stored until that organisation is known not to belong to another Fiscalize company.
        /// </summary>
        [HttpGet("oauth/callback")]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult> OAuthCallbackAsync([FromQuery] string? state, [FromQuery] string? code, [FromQuery] string? error = null)
        {
            if (!string.IsNullOrEmpty(error))
                return BadRequest(new ErrorModel
                {
                    Code = 400,
                    Message = string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
                        ? "Xero was not connected because access was not allowed on the Xero consent screen. Start the connection again and choose Allow access."
                        : $"Xero did not complete the connection ({error}). Start the connection again."
                });
            if (string.IsNullOrEmpty(state) || !Guid.TryParse(state, out var stateGuid))
                return BadRequest(new ErrorModel { Code = 400, Message = "Invalid state parameter" });
            if (string.IsNullOrEmpty(code))
                return BadRequest(new ErrorModel { Code = 400, Message = "Authorization code is required" });

            var config = await _clientConfigService.GetByStateAsync(stateGuid);
            if (config?.AppUserCompany == null)
                return NotFound(new ErrorModel { Code = 404, Message = "This Xero connection attempt was not found or has already been completed. Start the connection again." });
            var companyId = config.AppUserCompany.CompanyId;

            // The consent link is only good for the company that asked for it, finished by one of its own members, and
            // only for a short while. Otherwise anyone could start a connection and send the link to another business,
            // whose Xero organisation would then be connected to (and fiscalised on) the sender's company.
            var caller = await _currentUserService.GetCurrentUserAsync();
            if (caller == null || !IsMemberOf(caller, config))
            {
                _logger.LogWarning("OAuth callback refused: the signed-in user is not a member of the company that started connection {ConfigId}", config.Id);
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorModel
                {
                    Code = 403,
                    Message = "This Xero connection was started by a different Fiscalize company. Sign in to your own company and start the connection from there."
                });
            }
            if (!OAuthState.IsFresh(stateGuid, DateTime.UtcNow))
                return StatusCode(StatusCodes.Status410Gone, new ErrorModel
                {
                    Code = 410,
                    Message = "This Xero connection link has expired. Start the connection again; it takes a few seconds."
                });

            var tokens = await _xeroAuthService.ExchangeCodeAsync(code);
            if (!tokens.IsSuccess)
                return Unauthorized(new ErrorModel
                {
                    Code = 401,
                    Message = "Xero did not accept the authorisation code (it may have expired or been used already). Start the connection again."
                });

            // Which organisation did the user choose? Only Xero's connection list says.
            XeroConnection? connection;
            try
            {
                connection = XeroTenantSelection.Pick(await _xeroConnections.ListAsync(tokens.AccessToken), tokens.AccessToken, config.TenantId);
            }
            catch (XeroApiException ex)
            {
                _logger.LogWarning(ex, "OAuth callback: could not list Xero connections for config {ConfigId}", config.Id);
                return StatusCode(StatusCodes.Status502BadGateway, new ErrorModel
                {
                    Code = 502,
                    Message = $"Xero accepted the authorisation but did not say which organisation it was for ({ex.Message}). Start the connection again."
                });
            }
            if (connection == null)
                return BadRequest(new ErrorModel
                {
                    Code = 400,
                    Message = "Xero did not report an organisation for this authorisation. Start the connection again and choose an organisation on the Xero consent screen."
                });

            // A Xero organisation belongs to one Fiscalize company. Refuse before anything is stored, so a second
            // company can never read the first one's books or fiscalise its invoices on its own device.
            if (await _clientConfigService.IsTenantConnectedElsewhereAsync(connection.TenantId, companyId))
            {
                // Undo our access. Xero keeps one connection per app and organisation, so the connection this consent
                // returned may be the one the other company is using; it is deleted only when it is provably not.
                var heldElsewhere = await _clientConfigService.GetConnectionIdsHeldElsewhereAsync(connection.TenantId, companyId);
                if (XeroTenantSelection.SafeToDelete(connection, heldElsewhere))
                    await _xeroConnections.TryDisconnectAsync(tokens.AccessToken, connection.Id);
                // The tokens from this consent are dropped here and never stored.
                _logger.LogWarning("OAuth callback refused: Xero tenant {TenantId} is already connected to another Fiscalize company (config {ConfigId})",
                    connection.TenantId, config.Id);
                return Conflict(new
                {
                    Code = 409,
                    Message = "This Xero organisation is already connected to another Fiscalize company. Disconnect it there first, or choose a different Xero organisation.",
                    TenantId = connection.TenantId,
                    TenantName = connection.TenantName
                });
            }

            var previousTenant = config.TenantId;
            // The state has done its job: the same redirect cannot be replayed.
            config.State = Guid.NewGuid();
            // Whoever completes the consent manages the connection from now on (a colleague reconnecting takes it over).
            config.ConnectedByUserId = caller.UserId;
            config.ConnectedByName = ConnectionManagers.UserName(User) ?? caller.Email;
            await _xeroAuthService.StoreConnectionAsync(config, tokens, connection.TenantId, connection.TenantName, connection.Id);
            if (previousTenant.HasValue && previousTenant != connection.TenantId)
                _logger.LogWarning("Company {CompanyId} switched Xero organisation from {Old} to {New} (config {ConfigId})", companyId, previousTenant, connection.TenantId, config.Id);

            // Pair the organisation with the company on the Fiscalize platform (provider "Xero", external identifier
            // the tenant id). Platform's UNIQUE(Provider, ExternalIdentifier) makes repeated calls safe.
            var ensure = await _authService.EnsureExternalIdentityAsync(config.AppUserCompanyId, connection.TenantId.ToString("D"), connection.TenantName);
            if (ensure.Status == System.Net.HttpStatusCode.Conflict)
            {
                // The platform pairs this organisation with a different Fiscalize company: undo the connection we just
                // made so this company holds no access to the other company's books. No company here holds it (checked
                // above), so removing the connection at Xero cuts nobody else off.
                await _xeroConnections.TryDisconnectAsync(tokens.AccessToken, connection.Id);
                config.AccessToken = null;
                config.RefreshToken = null;
                config.TokenExpiresAt = null;
                config.TenantId = null;
                config.TenantName = null;
                config.XeroConnectionId = null;
                await _clientConfigService.UpdateAsync(config);
                _logger.LogWarning("OAuth callback: Xero tenant {TenantId} is paired with another Fiscalize company on the platform; connection {ConfigId} undone",
                    connection.TenantId, config.Id);

                return Conflict(new
                {
                    Code = 409,
                    Message = ensure.Message ?? "This Xero organisation is already paired with another Fiscalize company. Disconnect it there first, or choose a different Xero organisation.",
                    TenantId = connection.TenantId,
                    TenantName = connection.TenantName
                });
            }
            // The platform does not know a Xero provider yet, so any other answer is expected for now: it is logged,
            // never shown to the user, and never undoes the connection.
            if ((int)ensure.Status < 200 || (int)ensure.Status >= 300)
                _logger.LogWarning("OAuth callback: platform pairing not created for AppUserCompany {AppUserCompanyId} ({Status}: {Message})",
                    config.AppUserCompanyId, ensure.Status, ensure.Message);

            // Catch up on anything approved while the company was disconnected or being authorised.
            _sync.RequestSync(config.Id);

            return Ok(new
            {
                Success = true,
                Message = $"Xero organisation {connection.TenantName ?? connection.TenantId.ToString()} is connected.",
                TenantId = connection.TenantId,
                TenantName = connection.TenantName
            });
        }

        [HttpGet("fiscalization-settings")]
        [ProducesResponseType(typeof(FiscalizationSettingsModel), StatusCodes.Status200OK)]
        public async Task<ActionResult<FiscalizationSettingsModel>> GetFiscalizationSettingsAsync()
        {
            var config = await _currentUserService.GetCurrentClientConfigAsync();
            return Ok(new FiscalizationSettingsModel
            {
                AutoFiscalizeInvoices = config?.AutoFiscalizeInvoices ?? true,
                AutoFiscalizeCreditNotes = config?.AutoFiscalizeCreditNotes ?? true,
                WritebackHistoryNote = config?.WritebackHistoryNote ?? true,
                FiscalStampEnabled = config?.FiscalStampEnabled ?? true,
                AttachmentIncludeOnline = config?.AttachmentIncludeOnline ?? true,
                WritebackReference = config?.WritebackReference ?? false,
                FiscalPdfLayout = FiscalPdfLayouts.Normalise(config?.FiscalPdfLayout),
                LayoutPaperSize = LayoutPapers.Normalise(config?.LayoutPaperSize),
                LayoutDesignerUrl = LayoutDesignerUrl(),
                FiscalStampPosition = config?.FiscalStampPosition ?? "bottom-right",
                FiscalStampMarginTop = config?.FiscalStampMarginTop ?? 10,
                FiscalStampMarginBottom = config?.FiscalStampMarginBottom ?? 10,
                FiscalStampMarginLeft = config?.FiscalStampMarginLeft ?? 10,
                FiscalStampMarginRight = config?.FiscalStampMarginRight ?? 10,
                DefaultHsCode = config?.EffectiveDefaultHsCode ?? ClientConfig.SystemDefaultHsCode,
                FiscalizationValidationMode = config?.FiscalizationValidationMode ?? FiscalizationValidationMode.Strict,
            });
        }

        [HttpPatch("fiscalization-settings")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult> UpdateFiscalizationSettingsAsync([FromBody] UpdateFiscalizationSettingsModel model,
            [FromServices] FiscalizeXero.Api.Services.ConnectedApps.IConnectedAppReportQueue connectedApps)
        {
            if (!StampPositions.Contains(model.FiscalStampPosition ?? ""))
                return BadRequest(new ErrorModel { Code = 400, Message = $"The stamp position must be one of: {string.Join(", ", StampPositions)}." });
            if (model.FiscalPdfLayout != null && !FiscalPdfLayouts.All.Contains(model.FiscalPdfLayout.Trim(), StringComparer.OrdinalIgnoreCase))
                return BadRequest(new ErrorModel { Code = 400, Message = "The PDF attached in Xero must be XeroStamp, FiscalizeLayout or FiscalizeStandard." });
            if (model.LayoutPaperSize != null && !LayoutPapers.All.Contains(model.LayoutPaperSize.Trim(), StringComparer.OrdinalIgnoreCase))
                return BadRequest(new ErrorModel { Code = 400, Message = "The layout paper must be A4, 80mm or 58mm." });

            string? defaultHsCode = null;
            if (!string.IsNullOrWhiteSpace(model.DefaultHsCode))
            {
                defaultHsCode = ItemHsCodes.AsHsCode(model.DefaultHsCode);
                if (defaultHsCode == null)
                    return BadRequest(new ErrorModel { Code = 400, Message = "The default HS code must be a code from the HS code catalogue, such as 04021099." });
            }
            if (model.FiscalizationValidationMode.HasValue && !Enum.IsDefined(model.FiscalizationValidationMode.Value))
                return BadRequest(new ErrorModel { Code = 400, Message = "Buyer validation must be Strict or Lenient." });

            var config = await _currentUserService.GetCurrentClientConfigAsync();
            if (config != null && !ConnectionManagers.CanManage(config, _currentUserService.GetCurrentUserId()))
                return StatusCode(StatusCodes.Status403Forbidden, ConnectionManagers.Refusal(config));
            var isNew = config == null;
            if (config == null)
            {
                var user = await _currentUserService.GetCurrentUserAsync();
                if (user?.ActiveCompany == null)
                    return NotFound(new ErrorModel { Code = 404, Message = "No active company configured for the current user" });

                // Settings can be chosen before Xero is connected; they are kept under the company's anchor row.
                config = new ClientConfig
                {
                    AppUserCompanyId = await _currentUserService.GetCompanyAnchorIdAsync() ?? user.ActiveCompany.Id,
                    State = Guid.NewGuid()
                };
            }

            config.AutoFiscalizeInvoices = model.AutoFiscalizeInvoices;
            config.AutoFiscalizeCreditNotes = model.AutoFiscalizeCreditNotes;
            config.FiscalStampEnabled = model.FiscalStampEnabled;
            if (model.WritebackHistoryNote.HasValue) config.WritebackHistoryNote = model.WritebackHistoryNote.Value;
            if (model.AttachmentIncludeOnline.HasValue) config.AttachmentIncludeOnline = model.AttachmentIncludeOnline.Value;
            if (model.WritebackReference.HasValue) config.WritebackReference = model.WritebackReference.Value;
            config.FiscalStampPosition = model.FiscalStampPosition!.Trim();
            config.FiscalStampMarginTop = Math.Clamp(model.FiscalStampMarginTop, 0, 200);
            config.FiscalStampMarginBottom = Math.Clamp(model.FiscalStampMarginBottom, 0, 200);
            config.FiscalStampMarginLeft = Math.Clamp(model.FiscalStampMarginLeft, 0, 200);
            config.FiscalStampMarginRight = Math.Clamp(model.FiscalStampMarginRight, 0, 200);
            if (model.FiscalPdfLayout != null) config.FiscalPdfLayout = FiscalPdfLayouts.Normalise(model.FiscalPdfLayout);
            if (model.LayoutPaperSize != null) config.LayoutPaperSize = LayoutPapers.Normalise(model.LayoutPaperSize);
            config.DefaultHsCode = defaultHsCode;
            if (model.FiscalizationValidationMode.HasValue)
                config.FiscalizationValidationMode = model.FiscalizationValidationMode.Value;

            if (isNew) await _clientConfigService.CreateAsync(config);
            else await _clientConfigService.UpdateAsync(config);
            if (config.TenantId != null) connectedApps.Enqueue(config.Id); // Connected apps shows the new settings soon
            return NoContent();
        }

        /// <summary>The stamp positions the stamp can be drawn at.</summary>
        public static readonly string[] StampPositions = { "top-left", "top-center", "top-right", "bottom-left", "bottom-center", "bottom-right" };

        /// <summary>The platform page where the company designs its layouts (plugin type "Xero").</summary>
        private string LayoutDesignerUrl()
        {
            var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var web = (config["Fiscalize:WebUrl"] ?? "https://fiscalize.co.zw").TrimEnd('/');
            return $"{web}/dashboard/plugin-layouts";
        }

        /// <summary>
        /// The Fiscalize layout the company would use for invoices and credit notes on its paper (its own, or Fiscalize's
        /// standard design). With <c>refresh</c> the platform is asked now; otherwise what is held is shown.
        /// </summary>
        [HttpGet("fiscal-layouts")]
        [ProducesResponseType(typeof(List<FiscalLayoutStatusModel>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<FiscalLayoutStatusModel>>> GetFiscalLayoutsAsync([FromQuery] bool refresh,
            [FromServices] FiscalizeXero.Api.Services.Layouts.IPlatformLayoutService layouts,
            [FromServices] IXeroAuthBuilder xeroAuth)
        {
            var config = await _currentUserService.GetCurrentClientConfigAsync();
            var paper = LayoutPapers.Normalise(config?.LayoutPaperSize);
            if (config == null)
                return Ok(FiscalizeXero.Api.Services.Layouts.PlatformLayoutService.PlatformDocumentTypes
                    .Select(t => new FiscalLayoutStatusModel { DocumentType = t, PaperSize = paper }).ToList());

            if (refresh)
            {
                FiscalizeXero.Xero.XeroAuth? xero = null;
                try { if (config.IsLive) xero = await xeroAuth.BuildAsync(config); } catch (Exception ex) { _logger.LogInformation(ex, "Xero not reachable while checking layouts"); }
                await layouts.RefreshAsync(config, xero, HttpContext.RequestAborted);
            }
            var held = await layouts.HeldAsync(config.AppUserCompanyId, HttpContext.RequestAborted);
            return Ok(FiscalizeXero.Api.Services.Layouts.PlatformLayoutService.PlatformDocumentTypes.Select(t =>
            {
                var h = held.FirstOrDefault(l => l.DocumentType == t && string.Equals(l.PaperSize, paper, StringComparison.OrdinalIgnoreCase));
                return new FiscalLayoutStatusModel
                {
                    DocumentType = t, PaperSize = paper, Available = h != null,
                    IsStandardDesign = h?.IsStandardDesign ?? false, Version = h?.Version ?? 0, CheckedAt = h?.CheckedAt
                };
            }).ToList());
        }
    }
}

using System.Text;
using System.Text.Json;
using FiscalizeXero.Api.Controllers.ClientConfigs;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.Auths;
using FiscalizeXero.Api.Services.ClientConfigs;
using FiscalizeXero.Api.Services.Sync;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Fiscalize.Models.Auths;
using FiscalizeXero.Shared;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.OAuth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Claims;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    /// <summary>Xero's redirect does not say which organisation was chosen; it is worked out from the connection list.</summary>
    public class XeroTenantSelectionTests
    {
        private static readonly Guid Event = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
        private static readonly Guid OrgA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OrgB = Guid.Parse("22222222-2222-2222-2222-222222222222");

        internal static string Token(Guid? authEvent)
        {
            static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var payload = authEvent.HasValue ? $"{{\"authentication_event_id\":\"{authEvent}\"}}" : "{}";
            return $"{B64("{\"alg\":\"none\"}")}.{B64(payload)}.sig";
        }

        internal static XeroConnection Conn(Guid tenant, Guid? authEvent, string type = "ORGANISATION", int minutesAgo = 0, string? name = null) => new()
        {
            Id = Guid.NewGuid(), TenantId = tenant, AuthEventId = authEvent, TenantType = type, TenantName = name ?? "Org",
            UpdatedDateUtc = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo)
        };

        [Fact]
        public void The_organisation_stamped_with_this_consent_is_picked_not_one_connected_earlier()
        {
            var earlier = Conn(OrgA, Guid.NewGuid(), minutesAgo: 0);   // more recent, but from another consent
            var chosen = Conn(OrgB, Event, minutesAgo: 30);
            Assert.Same(chosen, XeroTenantSelection.Pick(new[] { earlier, chosen }, Token(Event)));
        }

        [Fact]
        public void Without_an_event_id_the_most_recently_updated_organisation_is_picked()
        {
            var old = Conn(OrgA, null, minutesAgo: 600);
            var recent = Conn(OrgB, null, minutesAgo: 1);
            Assert.Same(recent, XeroTenantSelection.Pick(new[] { old, recent }, Token(null)));
            Assert.Same(recent, XeroTenantSelection.Pick(new[] { old, recent }, "not-a-jwt"));
        }

        [Fact]
        public void Practices_and_other_non_organisations_are_never_picked()
        {
            Assert.Null(XeroTenantSelection.Pick(new[] { Conn(OrgA, Event, type: "PRACTICE") }, Token(Event)));
            Assert.Null(XeroTenantSelection.Pick(Array.Empty<XeroConnection>(), Token(Event)));
        }

        [Fact]
        public void When_one_consent_covers_several_organisations_the_one_already_connected_is_kept()
        {
            var a = Conn(OrgA, Event, minutesAgo: 5);
            var b = Conn(OrgB, Event, minutesAgo: 1);
            Assert.Same(a, XeroTenantSelection.Pick(new[] { a, b }, Token(Event), currentTenantId: OrgA));
            Assert.Same(b, XeroTenantSelection.Pick(new[] { a, b }, Token(Event)));                       // first connection: most recent
            Assert.Same(b, XeroTenantSelection.Pick(new[] { a, b }, Token(Event), currentTenantId: Guid.NewGuid()));
        }

        [Fact]
        public void A_connection_is_only_deleted_when_it_is_provably_not_the_other_companys()
        {
            var ours = Conn(OrgA, Event);
            Assert.True(XeroTenantSelection.SafeToDelete(ours, new Guid?[] { Guid.NewGuid() }));
            Assert.False(XeroTenantSelection.SafeToDelete(ours, new Guid?[] { ours.Id }));        // the very connection they use
            Assert.False(XeroTenantSelection.SafeToDelete(ours, new Guid?[] { null }));           // unknown: don't risk it
            Assert.False(XeroTenantSelection.SafeToDelete(ours, Array.Empty<Guid?>()));
        }
    }

    /// <summary>The OAuth callback end to end, with Xero and the platform stood in for.</summary>
    public class XeroOAuthCallbackTests
    {
        private static readonly Guid OrgA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OrgB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid Event = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

        private sealed class FakeXeroAuth : IXeroAuthService
        {
            private readonly IClientConfigService _configs;
            public FakeXeroAuth(IClientConfigService configs) => _configs = configs;
            public XeroTokenResponse Tokens = new() { AccessToken = XeroTenantSelectionTests.Token(Event), RefreshToken = "refresh-new", ExpiresIn = 1800 };
            public bool IsConfigured => true;
            public string? BuildConsentUrl(Guid state) => "https://login.xero.com/x?state=" + state;
            public Task<XeroTokenResponse> ExchangeCodeAsync(string code) => Task.FromResult(Tokens);
            public async Task StoreConnectionAsync(ClientConfig config, XeroTokenResponse tokens, Guid tenantId, string? tenantName, Guid connectionId)
            {
                (config.AccessToken, config.RefreshToken, config.TenantId, config.TenantName, config.XeroConnectionId, config.LastSyncAt) =
                    (tokens.AccessToken, tokens.RefreshToken, tenantId, tenantName, connectionId, null);
                await _configs.UpdateAsync(config);
            }
            public Task<XeroTokenResponse?> RefreshAccessTokenAsync(ClientConfig config) => throw new NotSupportedException();
            public Task<string?> GetValidAccessTokenAsync(ClientConfig config) => throw new NotSupportedException();
            public Task<string?> ForceRefreshAsync(ClientConfig config) => throw new NotSupportedException();
            public Task<bool> DisconnectAsync(ClientConfig config) => throw new NotSupportedException();
        }

        private sealed class FakeConnections : IXeroConnections
        {
            public List<XeroConnection> List = new();
            public List<Guid> Deleted = new();
            public Task<List<XeroConnection>> ListAsync(string accessToken) => Task.FromResult(List);
            public Task<bool> TryDisconnectAsync(string accessToken, Guid connectionId) { Deleted.Add(connectionId); return Task.FromResult(true); }
        }

        private sealed class FakeAuth : IAuthService
        {
            public HttpStatusCode Pairing = HttpStatusCode.OK;
            public (int Company, string Tenant, string? Name)? Paired;
            public Task<ServiceResponse<bool>> EnsureExternalIdentityAsync(int appUserCompanyId, string tenantId, string? displayName)
            {
                Paired = (appUserCompanyId, tenantId, displayName);
                return Task.FromResult(new ServiceResponse<bool> { Status = Pairing, Message = Pairing == HttpStatusCode.OK ? string.Empty : "platform says no" });
            }
            public Task<ServiceResponse<LoginResponseModel>> LoginAsync(ClaimsPrincipal claim, LoginModel login) => throw new NotSupportedException();
            public Task<ServiceResponse<LoginResponseModel>> SwitchCompanyAsync(ClaimsPrincipal claim, long companyId) => throw new NotSupportedException();
            public Task<ServiceResponse<LoginResponseModel>> ExchangeLaunchTokenAsync(string launchToken) => throw new NotSupportedException();
            public Task<ServiceResponse<string>> OpenFiscalizeAsync(ClaimsPrincipal claim) => throw new NotSupportedException();
        }

        /// <summary>The person finishing the consent: a member of every company the test created, unless told otherwise.</summary>
        private sealed class FakeCurrentUser : FiscalizeXero.Api.Services.CurrentUser.ICurrentUserService
        {
            public AppUser User = new() { UserId = 999 };
            public Task<AppUser?> GetCurrentUserAsync() => Task.FromResult<AppUser?>(User);
            public ClientConfig? Config;
            public Task<ClientConfig?> GetCurrentClientConfigAsync() => Task.FromResult(Config);
            public Task<int?> GetCompanyAnchorIdAsync() => Task.FromResult<int?>(null);
            public long? GetCurrentUserId() => User.UserId;
        }

        private sealed class Harness
        {
            public FakeCurrentUser CurrentUser = new();
            public DataContext Db = new(new DbContextOptionsBuilder<DataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            public FakeConnections Connections = new();
            public FakeAuth Platform = new();
            public XeroSyncRequests Sync = new();
            public FakeXeroAuth Xero;
            public ClientConfigsController Controller;

            public Harness()
            {
                var configs = new ClientConfigService(new ClientConfigRepository(Db));
                Xero = new FakeXeroAuth(configs);
                Controller = new ClientConfigsController(configs, CurrentUser, Xero, Connections, Platform, Sync,
                    Options.Create(new XeroOptions()), NullLogger<ClientConfigsController>.Instance);
            }

            public ClientConfig Company(long companyId, Guid? tenant = null, string? refresh = null, Guid? connectionId = null)
            {
                var user = new AppUser { UserId = companyId * 10 };
                var auc = new AppUserCompany { AppUser = user, CompanyId = companyId };
                Db.AddRange(user, auc);
                Db.SaveChanges();
                CurrentUser.User.Companies.Add(new AppUserCompany { CompanyId = companyId });
                var config = new ClientConfig { AppUserCompanyId = auc.Id, State = FiscalizeXero.Api.Security.OAuthState.New(), TenantId = tenant, RefreshToken = refresh, XeroConnectionId = connectionId, LastSyncAt = DateTime.UtcNow };
                Db.Configs.Add(config);
                Db.SaveChanges();
                return config;
            }

            public ClientConfig Reload(ClientConfig c) => Db.Configs.AsNoTracking().Single(x => x.Id == c.Id);
        }

        [Fact]
        public async Task Someone_outside_the_company_cannot_finish_its_consent_link()
        {
            var h = new Harness();
            var config = h.Company(1);
            h.CurrentUser.User = new AppUser { UserId = 5 }; // signed in to another company only
            var result = Assert.IsType<ObjectResult>(await h.Controller.OAuthCallbackAsync(config.State.ToString(), "code"));
            Assert.Equal(403, result.StatusCode);
            Assert.Null(h.Reload(config).TenantId);
        }

        [Fact]
        public async Task Whoever_completes_the_consent_manages_the_connection()
        {
            var h = new Harness();
            var config = h.Company(1);
            h.CurrentUser.User.UserId = 42;
            h.Connections.List = new() { XeroTenantSelectionTests.Conn(OrgB, Event, name: "Chosen Org") };
            Assert.IsType<OkObjectResult>(await h.Controller.OAuthCallbackAsync(config.State.ToString(), "code"));
            Assert.Equal(42, h.Reload(config).ConnectedByUserId);
        }

        [Fact]
        public async Task A_colleague_cannot_disconnect_but_is_told_how_to_take_over()
        {
            var h = new Harness();
            var config = h.Company(1, OrgA, "refresh");
            config.ConnectedByUserId = 42; config.ConnectedByName = "Tendai";
            h.Db.SaveChanges();
            h.CurrentUser.User.UserId = 7;
            h.CurrentUser.Config = h.Db.Configs.Include(c => c.AppUserCompany).Single(c => c.Id == config.Id);
            var result = Assert.IsType<ObjectResult>(await h.Controller.ResetCurrentClientConfigAsync());
            Assert.Equal(403, result.StatusCode);
            Assert.Contains("Tendai", Body(result));
            Assert.Contains("reconnect Xero with your own Xero login", Body(result));
            Assert.NotNull(h.Reload(config).TenantId); // nothing was removed
        }

        [Fact]
        public void The_connector_and_anyone_on_an_older_connection_may_manage()
        {
            Assert.True(FiscalizeXero.Api.Security.ConnectionManagers.CanManage(new ClientConfig { ConnectedByUserId = 42 }, 42));
            Assert.False(FiscalizeXero.Api.Security.ConnectionManagers.CanManage(new ClientConfig { ConnectedByUserId = 42 }, 7));
            Assert.True(FiscalizeXero.Api.Security.ConnectionManagers.CanManage(new ClientConfig { ConnectedByUserId = null }, 7));
            Assert.True(FiscalizeXero.Api.Security.ConnectionManagers.CanManage(null, 7)); // nothing connected yet
        }

        [Fact]
        public async Task An_expired_consent_link_is_refused()
        {
            var h = new Harness();
            var config = h.Company(1);
            config.State = Guid.NewGuid(); // no timestamp, as if made long ago
            h.Db.SaveChanges();
            var result = Assert.IsType<ObjectResult>(await h.Controller.OAuthCallbackAsync(config.State.ToString(), "code"));
            Assert.Equal(410, result.StatusCode);
        }

        private static string Body(ActionResult result) => JsonSerializer.Serialize(((ObjectResult)result).Value);

        [Fact]
        public async Task Connects_the_organisation_the_consent_was_for_and_asks_for_a_catch_up_sync()
        {
            var h = new Harness();
            var config = h.Company(1);
            var state = config.State;
            var earlier = XeroTenantSelectionTests.Conn(OrgA, Guid.NewGuid(), name: "Earlier Org");
            var chosen = XeroTenantSelectionTests.Conn(OrgB, Event, minutesAgo: 30, name: "Chosen Org");
            h.Connections.List = new() { earlier, chosen };

            var result = await h.Controller.OAuthCallbackAsync(state.ToString(), "code");

            Assert.IsType<OkObjectResult>(result);
            Assert.Contains("Chosen Org", Body(result));
            Assert.DoesNotContain("Warning", Body(result));
            var stored = h.Reload(config);
            Assert.Equal((OrgB, "Chosen Org", chosen.Id, "refresh-new"), (stored.TenantId!.Value, stored.TenantName, stored.XeroConnectionId!.Value, stored.RefreshToken));
            Assert.Null(stored.LastSyncAt);                          // a (re)connection starts from the short look-back
            Assert.NotEqual(state, stored.State);                    // the redirect cannot be replayed
            Assert.Equal(("Xero org", OrgB.ToString()), ("Xero org", h.Platform.Paired!.Value.Tenant)); // paired by tenant id
            Assert.True(h.Sync.TryRead(out var requested) && requested == config.Id);
            Assert.Empty(h.Connections.Deleted);

            Assert.IsType<NotFoundObjectResult>(await h.Controller.OAuthCallbackAsync(state.ToString(), "code"));
        }

        [Fact]
        public async Task An_organisation_live_under_another_company_is_refused_with_409_and_nothing_is_stored()
        {
            var h = new Harness();
            var theirConnection = Guid.NewGuid();
            var theirs = h.Company(1, OrgA, "their-refresh", theirConnection);
            var mine = h.Company(2);
            var ours = XeroTenantSelectionTests.Conn(OrgA, Event, name: "Shared Org");
            h.Connections.List = new() { ours };

            var result = await h.Controller.OAuthCallbackAsync(mine.State.ToString(), "code");

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.Contains("This Xero organisation is already connected to another Fiscalize company", Body(conflict));
            var stored = h.Reload(mine);
            Assert.Null(stored.TenantId);
            Assert.Null(stored.RefreshToken);
            Assert.Null(stored.AccessToken);
            Assert.Equal(new[] { ours.Id }, h.Connections.Deleted);  // our new connection is undone at Xero
            Assert.Null(h.Platform.Paired);
            Assert.False(h.Sync.TryRead(out _));
            Assert.Equal("their-refresh", h.Reload(theirs).RefreshToken); // the other company is untouched
        }

        [Fact]
        public async Task The_other_companys_own_connection_is_never_deleted_on_our_behalf()
        {
            var h = new Harness();
            var shared = XeroTenantSelectionTests.Conn(OrgA, Event);
            h.Company(1, OrgA, "their-refresh", shared.Id);          // Xero returned the connection they already use
            var mine = h.Company(2);
            h.Connections.List = new() { shared };

            Assert.IsType<ConflictObjectResult>(await h.Controller.OAuthCallbackAsync(mine.State.ToString(), "code"));
            Assert.Empty(h.Connections.Deleted);
            Assert.Null(h.Reload(mine).RefreshToken);
        }

        [Fact]
        public async Task A_colleague_reconnecting_the_same_company_is_not_a_conflict()
        {
            var h = new Harness();
            var config = h.Company(1, OrgA, "old-refresh", Guid.NewGuid());
            h.Connections.List = new() { XeroTenantSelectionTests.Conn(OrgA, Event) };
            Assert.IsType<OkObjectResult>(await h.Controller.OAuthCallbackAsync(config.State.ToString(), "code"));
            Assert.Equal("refresh-new", h.Reload(config).RefreshToken);
        }

        [Fact]
        public async Task A_platform_pairing_conflict_undoes_the_connection()
        {
            var h = new Harness();
            var config = h.Company(1);
            var conn = XeroTenantSelectionTests.Conn(OrgA, Event);
            h.Connections.List = new() { conn };
            h.Platform.Pairing = HttpStatusCode.Conflict;

            var result = Assert.IsType<ConflictObjectResult>(await h.Controller.OAuthCallbackAsync(config.State.ToString(), "code"));
            Assert.Contains("platform says no", Body(result));
            var stored = h.Reload(config);
            Assert.Null(stored.TenantId);
            Assert.Null(stored.RefreshToken);
            Assert.Equal(new[] { conn.Id }, h.Connections.Deleted);
            Assert.False(h.Sync.TryRead(out _));
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task Any_other_platform_pairing_failure_is_logged_only_and_never_shown(HttpStatusCode status)
        {
            var h = new Harness();
            var config = h.Company(1);
            h.Connections.List = new() { XeroTenantSelectionTests.Conn(OrgA, Event) };
            h.Platform.Pairing = status;

            var result = Assert.IsType<OkObjectResult>(await h.Controller.OAuthCallbackAsync(config.State.ToString(), "code"));
            Assert.DoesNotContain("Warning", Body(result));
            Assert.DoesNotContain("platform says no", Body(result));
            Assert.Equal(OrgA, h.Reload(config).TenantId);
            Assert.True(h.Sync.TryRead(out _));
        }

        [Fact]
        public async Task A_refused_code_or_no_organisation_stores_nothing()
        {
            var h = new Harness();
            var config = h.Company(1);
            h.Xero.Tokens = new XeroTokenResponse { Error = "invalid_grant" };
            Assert.IsType<UnauthorizedObjectResult>(await h.Controller.OAuthCallbackAsync(config.State.ToString(), "code"));

            h.Xero.Tokens = new XeroTokenResponse { AccessToken = XeroTenantSelectionTests.Token(Event), RefreshToken = "r" };
            h.Connections.List = new() { XeroTenantSelectionTests.Conn(OrgA, Event, type: "PRACTICE") };
            Assert.IsType<BadRequestObjectResult>(await h.Controller.OAuthCallbackAsync(config.State.ToString(), "code"));
            Assert.Null(h.Reload(config).RefreshToken);

            Assert.IsType<BadRequestObjectResult>(await h.Controller.OAuthCallbackAsync("not-a-guid", "code"));
            Assert.IsType<BadRequestObjectResult>(await h.Controller.OAuthCallbackAsync(config.State.ToString(), null));
            Assert.Contains("Allow access", Body(await h.Controller.OAuthCallbackAsync(config.State.ToString(), null, "access_denied")));
        }
    }
}

using System.Net;
using System.Text;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.FiscalizeSession;
using FiscalizeXero.Api.Services.Membership;
using FiscalizeXero.Shared.Models.Auths;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FiscalizeXero.Api.Tests;

/// <summary>A user of one Fiscalize company must never reach another company's Xero organisation.</summary>
public class TenantIsolationTests
{
    private static readonly AppUserCompany CompanyA = new() { Id = 7, CompanyId = 1, CompanyName = "A", FiscalizeToken = "t" };

    // ---------- membership decisions ----------

    private sealed class FakeDirectory : IPlatformCompanyDirectory
    {
        public PlatformMembership Next = PlatformMembership.Member;
        public int Calls;
        public Task<PlatformMembership> LookupAsync(AppUserCompany company, CancellationToken ct = default) { Calls++; return Task.FromResult(Next); }
    }

    private static (CompanyMembershipVerifier Verifier, FakeDirectory Directory, Func<DateTime, DateTime> SetNow) Verifier()
    {
        var now = new DateTime(2026, 11, 18, 8, 0, 0, DateTimeKind.Utc);
        var dir = new FakeDirectory();
        var v = new CompanyMembershipVerifier(dir, new MemoryCache(new MemoryCacheOptions()), NullLogger<CompanyMembershipVerifier>.Instance, () => now);
        return (v, dir, t => now = t);
    }

    [Fact]
    public async Task Member_is_allowed_and_the_answer_is_cached()
    {
        var (v, dir, _) = Verifier();
        Assert.Equal(MembershipDecision.Allowed, await v.CheckAsync(CompanyA));
        Assert.Equal(MembershipDecision.Allowed, await v.CheckAsync(CompanyA));
        Assert.Equal(1, dir.Calls);
    }

    [Fact]
    public async Task Removed_on_the_platform_is_refused_at_the_next_check_despite_earlier_access()
    {
        var (v, dir, setNow) = Verifier();
        var start = new DateTime(2026, 11, 18, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(MembershipDecision.Allowed, await v.CheckAsync(CompanyA));

        dir.Next = PlatformMembership.NotMember;
        setNow(start + CompanyMembershipVerifier.RecheckAfter);
        Assert.Equal(MembershipDecision.Revoked, await v.CheckAsync(CompanyA));

        // ...and a later platform outage doesn't bring access back through the grace period.
        dir.Next = PlatformMembership.Unknown;
        setNow(start + CompanyMembershipVerifier.RecheckAfter * 2);
        Assert.Equal(MembershipDecision.NeedsSignIn, await v.CheckAsync(CompanyA));
    }

    [Fact]
    public async Task Access_granted_at_sign_in_counts_as_confirmed()
    {
        // A session the platform just granted (sign-in, launch, choosing the company) confirms access, so a platform
        // that can't be asked right after doesn't send the user straight back to sign in (direct sign-in loop, 20 Nov 2026).
        var (v, dir, setNow) = Verifier();
        var start = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        setNow(start);
        dir.Next = PlatformMembership.Unknown;
        v.RecordGranted(CompanyA);
        Assert.Equal(MembershipDecision.Allowed, await v.CheckAsync(CompanyA));
        setNow(start + CompanyMembershipVerifier.RecheckAfter + TimeSpan.FromMinutes(1));
        Assert.Equal(MembershipDecision.Allowed, await v.CheckAsync(CompanyA)); // still within the grace period
    }

    [Fact]
    public async Task Access_granted_at_sign_in_is_still_revoked_by_a_clear_refusal()
    {
        var (v, dir, setNow) = Verifier();
        var start = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        setNow(start);
        v.RecordGranted(CompanyA);
        dir.Next = PlatformMembership.NotMember;
        setNow(start + CompanyMembershipVerifier.RecheckAfter);
        Assert.Equal(MembershipDecision.Revoked, await v.CheckAsync(CompanyA));
    }

    [Fact]
    public async Task Never_confirmed_and_platform_unreachable_needs_sign_in()
    {
        var (v, dir, _) = Verifier();
        dir.Next = PlatformMembership.Unknown;
        Assert.Equal(MembershipDecision.NeedsSignIn, await v.CheckAsync(CompanyA));
    }

    [Fact]
    public async Task Platform_outage_is_tolerated_only_within_the_grace_period()
    {
        var (v, dir, setNow) = Verifier();
        var start = new DateTime(2026, 11, 18, 8, 0, 0, DateTimeKind.Utc);
        await v.CheckAsync(CompanyA);

        dir.Next = PlatformMembership.Unknown;
        setNow(start + TimeSpan.FromHours(2));
        Assert.Equal(MembershipDecision.Allowed, await v.CheckAsync(CompanyA));

        setNow(start + CompanyMembershipVerifier.Grace + TimeSpan.FromMinutes(1));
        Assert.Equal(MembershipDecision.NeedsSignIn, await v.CheckAsync(CompanyA));
    }

    // ---------- reading the platform's answer ----------

    private sealed class FakeSession : IFiscalizeSessionService
    {
        public Task<FiscalizeAuthModel> GetAuthAsync() => throw new NotSupportedException();
        public Task<FiscalizeAuthModel?> TryGetAuthForCompanyAsync(AppUserCompany company) =>
            Task.FromResult<FiscalizeAuthModel?>(company.FiscalizeToken == null ? null : new FiscalizeAuthModel { ApiUrl = "http://platform", Token = company.FiscalizeToken });
        public Task<FiscalizeAuthModel?> TryRefreshAsync(AppUserCompany company) => Task.FromResult<FiscalizeAuthModel?>(null);
        public Task<FiscalizeAuthModel?> TryGetUserAuthAsync(AppUser user) =>
            Task.FromResult<FiscalizeAuthModel?>(user.FiscalizeToken == null ? null : new FiscalizeAuthModel { ApiUrl = "http://platform", Token = user.FiscalizeToken });
        public Task ClearSessionAsync() => Task.CompletedTask;
        public Task<T> RunAsync<T>(Func<FiscalizeAuthModel, Task<T>> call) => throw new NotSupportedException();
        public Task<PlatformSessionState> KeepAliveAsync() => throw new NotSupportedException();
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(_respond(request));
    }

    private sealed class Factory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        public Factory(HttpMessageHandler h) => _h = h;
        public HttpClient CreateClient(string name) => new(_h, false) { BaseAddress = new Uri("http://platform") };
    }

    private static Task<PlatformMembership> Lookup(Func<HttpRequestMessage, HttpResponseMessage> respond, AppUserCompany? company = null) =>
        new PlatformCompanyDirectory(new FakeSession(), new Factory(new Handler(respond))).LookupAsync(company ?? CompanyA);

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // ---------- the user's own sign-in as a fallback (direct sign-in, 20 Nov 2026) ----------

    private static AppUserCompany WithUserSession(string? userToken) => new()
    {
        Id = 10, AppUserId = 5, CompanyId = 1, FiscalizeToken = "t",
        AppUser = new AppUser { Id = 5, UserId = 77, FiscalizeToken = userToken }
    };

    [Fact]
    public async Task Company_session_refused_but_the_users_own_sign_in_lists_the_company_is_a_member()
    {
        var seen = new List<string>();
        var r = await Lookup(req =>
        {
            var token = req.Headers.Authorization?.Parameter;
            seen.Add($"{token} {req.RequestUri!.AbsolutePath}");
            if (token == "u" && req.RequestUri!.AbsolutePath.StartsWith("/api/CompanyUsers/MyCompanies")) return Json("{\"value\":[{\"id\":1}]}");
            return Json("{\"message\":\"Something went wrong\"}", HttpStatusCode.InternalServerError);
        }, WithUserSession("u"));
        Assert.Equal(PlatformMembership.Member, r);
        Assert.Contains("u /api/CompanyUsers/MyCompanies", seen);
    }

    [Fact]
    public async Task The_users_own_sign_in_never_turns_unknown_into_a_refusal()
    {
        // Listed elsewhere only: the fallback can confirm access, never take it away.
        var r = await Lookup(req => req.Headers.Authorization?.Parameter == "u"
            ? Json("{\"value\":[{\"id\":2}]}")
            : Json("{}", HttpStatusCode.InternalServerError), WithUserSession("u"));
        Assert.Equal(PlatformMembership.Unknown, r);
    }

    [Fact]
    public async Task Without_a_user_sign_in_the_answer_is_unchanged()
        => Assert.Equal(PlatformMembership.Unknown, await Lookup(_ => Json("{}", HttpStatusCode.InternalServerError), WithUserSession(null)));

    [Fact]
    public async Task Listed_and_active_is_a_member()
    {
        string? auth = null;
        var r = await Lookup(req => { auth = req.Headers.Authorization?.ToString(); return Json("{\"value\":[{\"id\":2},{\"id\":1,\"isActive\":true}]}"); });
        Assert.Equal(PlatformMembership.Member, r);
        Assert.Equal("Bearer t", auth); // the user's own platform session, never a colleague's
    }

    /// <summary>Answers the company list with <paramref name="list"/> and the direct company check with <paramref name="direct"/>.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> Platform(string list, Func<HttpResponseMessage> direct) =>
        req => req.RequestUri!.AbsolutePath.StartsWith("/api/Auths/Companies/") ? direct() : Json(list);

    [Fact]
    public async Task Listed_but_flagged_inactive_is_still_a_member() =>
        Assert.Equal(PlatformMembership.Member, await Lookup(Platform("{\"value\":[{\"id\":1,\"isActive\":false}]}", () => Json("{}", HttpStatusCode.Forbidden))));

    [Fact]
    public async Task Not_listed_but_platform_grants_a_session_is_a_member()
    {
        // e.g. the platform administrator, who can open companies they are not listed in
        string? path = null;
        var r = await Lookup(req =>
        {
            if (req.RequestUri!.AbsolutePath.StartsWith("/api/Auths/Companies/")) { path = req.RequestUri.AbsolutePath; return Json("{\"token\":\"x\",\"activeCompany\":{\"id\":1,\"name\":\"A\"}}"); }
            return Json("{\"value\":[{\"id\":2}]}");
        });
        Assert.Equal(PlatformMembership.Member, r);
        Assert.Equal("/api/Auths/Companies/1", path);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Not_listed_and_refused_by_the_platform_is_not_a_member(HttpStatusCode code) =>
        Assert.Equal(PlatformMembership.NotMember, await Lookup(Platform("{\"value\":[{\"id\":2,\"isActive\":true}]}", () => Json("{}", code))));

    [Fact]
    public async Task Empty_list_and_refused_is_not_a_member() =>
        Assert.Equal(PlatformMembership.NotMember, await Lookup(Platform("{\"value\":[]}", () => Json("{}", HttpStatusCode.Forbidden))));

    [Fact]
    public async Task Platform_granting_a_different_company_is_not_a_member() =>
        Assert.Equal(PlatformMembership.NotMember, await Lookup(Platform("{\"value\":[{\"id\":2}]}", () => Json("{\"activeCompany\":{\"id\":2}}"))));

    [Fact]
    public async Task Not_listed_and_platform_down_is_unknown_never_a_refusal() =>
        Assert.Equal(PlatformMembership.Unknown, await Lookup(Platform("{\"value\":[{\"id\":2}]}", () => Json("{}", HttpStatusCode.InternalServerError))));

    [Fact]
    public async Task Unreadable_list_and_refusal_is_unknown_never_a_refusal() =>
        Assert.Equal(PlatformMembership.Unknown, await Lookup(Platform("{\"unexpected\":true}", () => Json("{}", HttpStatusCode.Forbidden))));

    [Fact]
    public async Task Platform_returning_fewer_rows_per_page_than_asked_is_read_to_the_end()
    {
        // Asked for 200 per page, the platform caps at 50: the company is on page 3.
        string PageOf(int page) => "{\"value\":[" + string.Join(",", Enumerable.Range(1000 + (page - 1) * 50, page < 3 ? 50 : 10).Select(i => $"{{\"id\":{i}}}").Append(page == 3 ? "{\"id\":1}" : null).Where(x => x != null)) + "]}";
        var directCalled = false;
        var r = await Lookup(req =>
        {
            if (req.RequestUri!.AbsolutePath.StartsWith("/api/Auths/Companies/")) { directCalled = true; return Json("{}", HttpStatusCode.Forbidden); }
            var q = req.RequestUri.Query;
            return Json(PageOf(q.Contains("Page=1&") ? 1 : q.Contains("Page=2&") ? 2 : 3));
        });
        Assert.Equal(PlatformMembership.Member, r);
        Assert.False(directCalled);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.Unauthorized, "{}")]
    [InlineData(HttpStatusCode.OK, "{\"unexpected\":true}")]
    [InlineData(HttpStatusCode.OK, "\"text\"")]
    public async Task Errors_and_unexpected_answers_are_unknown_never_a_member(HttpStatusCode code, string body) =>
        Assert.Equal(PlatformMembership.Unknown, await Lookup(_ => Json(body, code)));

    [Fact]
    public async Task No_platform_session_is_unknown() =>
        Assert.Equal(PlatformMembership.Unknown, await Lookup(_ => Json("{\"value\":[{\"id\":1}]}"), new AppUserCompany { Id = 8, CompanyId = 1 }));

    [Fact]
    public async Task Later_pages_are_read_when_a_page_is_full()
    {
        var page1 = "{\"value\":[" + string.Join(",", Enumerable.Range(100, 200).Select(i => $"{{\"id\":{i}}}")) + "]}";
        var r = await Lookup(req => Json(req.RequestUri!.Query.Contains("Page=1&") ? page1 : "{\"value\":[{\"id\":1}]}"));
        Assert.Equal(PlatformMembership.Member, r);
    }

    // ---------- one Xero organisation, one Fiscalize company ----------

    private static DataContext NewDb() =>
        new(new DbContextOptionsBuilder<DataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static void Connect(DataContext db, long userId, long companyId, Guid tenant, bool live = true, Guid? connectionId = null)
    {
        var u = new AppUser { UserId = userId };
        var auc = new AppUserCompany { AppUser = u, CompanyId = companyId };
        db.AddRange(u, auc);
        db.SaveChanges();
        db.Configs.Add(new ClientConfig { AppUserCompanyId = auc.Id, State = Guid.NewGuid(), TenantId = tenant, XeroConnectionId = connectionId, RefreshToken = live ? "rt" : null });
        db.SaveChanges();
    }

    private static readonly Guid OrgA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrgB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task An_organisation_live_under_two_fiscalize_companies_is_contested()
    {
        using var db = NewDb();
        Connect(db, 1, companyId: 1, tenant: OrgA);
        Connect(db, 2, companyId: 1, tenant: OrgB);  // colleagues in one company: fine
        Connect(db, 3, companyId: 1, tenant: OrgB);
        Connect(db, 4, companyId: 2, tenant: OrgA);  // company B on company A's Xero organisation
        Connect(db, 5, companyId: 3, tenant: OrgB, live: false); // disconnected rows don't count

        Assert.Equal(new Guid?[] { OrgA }, await TenantGuards.ContestedTenants(db).ToArrayAsync());
    }

    [Fact]
    public async Task Connecting_company_b_to_company_a_organisation_is_detected()
    {
        using var db = NewDb();
        Connect(db, 1, companyId: 1, tenant: OrgA);

        Assert.True(await TenantGuards.ConnectedElsewhereAsync(db, OrgA, companyId: 2));
        Assert.False(await TenantGuards.ConnectedElsewhereAsync(db, OrgA, companyId: 1)); // a colleague reconnecting
        Assert.False(await TenantGuards.ConnectedElsewhereAsync(db, Guid.NewGuid(), companyId: 2));
    }

    [Fact]
    public async Task Background_sync_skips_a_contested_organisation()
    {
        using var db = NewDb();
        Connect(db, 1, companyId: 1, tenant: OrgA);
        Connect(db, 2, companyId: 2, tenant: OrgA);
        Connect(db, 3, companyId: 3, tenant: OrgB);

        var eligible = await FiscalizeXero.Api.Services.Sync.XeroSyncService.EligibleConfigs(db).Select(c => c.TenantId).ToListAsync();
        Assert.Equal(new Guid?[] { OrgB }, eligible);
    }

    [Fact]
    public async Task Health_check_reports_shared_organisations_as_degraded_with_counts_only()
    {
        using var db = NewDb();
        Connect(db, 1, companyId: 1, tenant: OrgA);
        var healthy = await new FiscalizeXero.Api.Services.CompanyConnections.CompanyConnectionsHealthCheck(db)
            .CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, healthy.Status);

        Connect(db, 2, companyId: 2, tenant: OrgA);
        var degraded = await new FiscalizeXero.Api.Services.CompanyConnections.CompanyConnectionsHealthCheck(db)
            .CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, degraded.Status);
        Assert.Equal(1, degraded.Data[FiscalizeXero.Api.Services.CompanyConnections.CompanyConnectionsHealthCheck.ContestedKey]);
        Assert.DoesNotContain(OrgA.ToString(), degraded.Description);
    }
}

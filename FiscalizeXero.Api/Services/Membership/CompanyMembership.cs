using System.Net;
using System.Text.Json;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.FiscalizeSession;
using Microsoft.Extensions.Caching.Memory;

namespace FiscalizeXero.Api.Services.Membership
{
    /// <summary>What the Fiscalize platform says about a user's access to one of their companies.</summary>
    public enum PlatformMembership
    {
        /// <summary>The platform lists the company for this user, or grants them a session on it.</summary>
        Member,
        /// <summary>The platform answered and the company is not (or no longer) one of this user's.</summary>
        NotMember,
        /// <summary>No answer: the platform is unreachable or the user's platform session has lapsed.</summary>
        Unknown
    }

    /// <summary>Whether a request may use the active company's data.</summary>
    public enum MembershipDecision
    {
        /// <summary>Confirmed member (now, or recently enough while the platform can't be asked).</summary>
        Allowed,
        /// <summary>The platform says the user no longer belongs to the company.</summary>
        Revoked,
        /// <summary>Not confirmed for too long: the user must sign in to Fiscalize again.</summary>
        NeedsSignIn
    }

    /// <summary>Asks the Fiscalize platform whether a user belongs to a company.</summary>
    public interface IPlatformCompanyDirectory
    {
        /// <summary>Looks the company up in the user's platform company list, using that user's own platform session.</summary>
        Task<PlatformMembership> LookupAsync(AppUserCompany company, CancellationToken ct = default);
    }

    /// <summary>
    /// Every member of a Fiscalize company shares its Xero connection, so membership is the access boundary.
    /// Local membership rows are only created after the platform confirms access, but the platform can revoke it later;
    /// this re-confirms it at most every <see cref="RecheckAfter"/>. A "not a member" answer denies at once. While the
    /// platform can't be asked, access continues only within <see cref="Grace"/> of the last confirmation.
    /// </summary>
    public interface ICompanyMembershipVerifier
    {
        /// <summary>Decides whether the user behind <paramref name="company"/> may use that company's data.</summary>
        Task<MembershipDecision> CheckAsync(AppUserCompany company, CancellationToken ct = default);

        /// <summary>
        /// The platform has just granted a session on <paramref name="company"/> (sign-in, launch, or choosing the
        /// company): that is a confirmation of access, so the next check doesn't ask again and a platform that can't be
        /// asked afterwards doesn't send the user straight back to sign in.
        /// </summary>
        void RecordGranted(AppUserCompany company);
    }

    /// <inheritdoc />
    public sealed class CompanyMembershipVerifier : ICompanyMembershipVerifier
    {
        /// <summary>How long a platform answer is trusted before asking again.</summary>
        public static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(10);
        /// <summary>How long after the last confirmation access survives a platform that can't be asked.</summary>
        public static readonly TimeSpan Grace = TimeSpan.FromHours(12);
        private static readonly TimeSpan UnknownBackoff = TimeSpan.FromMinutes(1);

        private readonly IPlatformCompanyDirectory _directory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CompanyMembershipVerifier> _logger;
        private readonly Func<DateTime> _now;

        /// <summary>Creates the verifier.</summary>
        public CompanyMembershipVerifier(IPlatformCompanyDirectory directory, IMemoryCache cache, ILogger<CompanyMembershipVerifier> logger)
            : this(directory, cache, logger, () => DateTime.UtcNow) { }

        /// <summary>Creates the verifier with a clock (tests).</summary>
        public CompanyMembershipVerifier(IPlatformCompanyDirectory directory, IMemoryCache cache, ILogger<CompanyMembershipVerifier> logger, Func<DateTime> now)
        {
            _directory = directory;
            _cache = cache;
            _logger = logger;
            _now = now;
        }

        private sealed record Answer(PlatformMembership Status, DateTime At);

        /// <inheritdoc />
        public async Task<MembershipDecision> CheckAsync(AppUserCompany company, CancellationToken ct = default)
        {
            var key = $"membership:{company.Id}";
            var confirmedKey = $"membership-confirmed:{company.Id}";
            var now = _now();

            if (!_cache.TryGetValue(key, out Answer? answer) || answer == null || IsStale(answer, now))
            {
                PlatformMembership status;
                try { status = await _directory.LookupAsync(company, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not confirm membership of AppUserCompany {Id} with the Fiscalize platform", company.Id);
                    status = PlatformMembership.Unknown;
                }
                answer = new Answer(status, now);
                _cache.Set(key, answer, RecheckAfter);
                if (status == PlatformMembership.Member)
                    _cache.Set(confirmedKey, now, Grace);
                else if (status == PlatformMembership.NotMember)
                {
                    _cache.Remove(confirmedKey);
                    _logger.LogWarning("Fiscalize platform no longer lists company {CompanyId} for AppUserCompany {Id}: access to its data refused",
                        company.CompanyId, company.Id);
                }
            }

            var decision = answer.Status switch
            {
                PlatformMembership.Member => MembershipDecision.Allowed,
                PlatformMembership.NotMember => MembershipDecision.Revoked,
                _ => _cache.TryGetValue(confirmedKey, out DateTime confirmedAt) && now - confirmedAt <= Grace
                    ? MembershipDecision.Allowed
                    : MembershipDecision.NeedsSignIn
            };
            if (decision == MembershipDecision.NeedsSignIn)
                _logger.LogWarning("Access of AppUserCompany {Id} (company {CompanyId}) couldn't be confirmed with the Fiscalize platform: asked to sign in again",
                    company.Id, company.CompanyId);
            return decision;
        }

        /// <inheritdoc />
        public void RecordGranted(AppUserCompany company)
        {
            var now = _now();
            _cache.Set($"membership:{company.Id}", new Answer(PlatformMembership.Member, now), RecheckAfter);
            _cache.Set($"membership-confirmed:{company.Id}", now, Grace);
        }

        private static bool IsStale(Answer a, DateTime now) =>
            now - a.At >= (a.Status == PlatformMembership.Unknown ? UnknownBackoff : RecheckAfter);
    }

    /// <summary>
    /// Asks the Fiscalize platform, with the user's own session, whether they may use a company. Access is whatever the
    /// platform grants, not only membership rows:
    /// <list type="number">
    /// <item>The company appears in the user's company list (<c>api/CompanyUsers/MyCompanies</c>): member. Its
    /// "isActive" flag is not treated as a refusal; the platform uses it for other things, and reading it as
    /// "access removed" locked real members out.</item>
    /// <item>Otherwise the platform is asked directly for a session on that company (<c>api/Auths/Companies/{id}</c>,
    /// the call "Switch to" makes). Granted for that company: member, which covers platform administrators and access
    /// the list doesn't show. A clear refusal (400/403/404) after a complete list without it: not a member.</item>
    /// <item>Anything else (platform down, session lapsed, an answer that can't be read) is <see cref="PlatformMembership.Unknown"/>,
    /// never a refusal, so the verifier's grace period applies instead of locking the user out.</item>
    /// </list>
    /// </summary>
    public sealed class PlatformCompanyDirectory : IPlatformCompanyDirectory
    {
        private const int PageSize = 200;
        private const int MaxPages = 25;

        private readonly IFiscalizeSessionService _session;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly FiscalizeXero.Api.Services.AppUsers.IAppUserService? _appUsers;

        /// <summary>Creates the directory.</summary>
        public PlatformCompanyDirectory(IFiscalizeSessionService session, IHttpClientFactory httpClientFactory,
            FiscalizeXero.Api.Services.AppUsers.IAppUserService? appUsers = null)
        {
            _session = session;
            _httpClientFactory = httpClientFactory;
            _appUsers = appUsers;
        }

        private enum ListResult { Found, NotFound, Unknown }

        /// <inheritdoc />
        public async Task<PlatformMembership> LookupAsync(AppUserCompany company, CancellationToken ct = default)
        {
            var answer = await LookupWithCompanySessionAsync(company, ct);
            if (answer != PlatformMembership.Unknown) return answer;

            // The company's session couldn't answer (lapsed, or refused for this kind of session). The user's own sign-in
            // can still list their companies: someone who just signed in directly is then never sent round to sign in.
            var user = company.AppUser ?? (_appUsers == null ? null : await _appUsers.GetByIdAsync(company.AppUserId));
            if (user == null) return answer;
            var userAuth = await _session.TryGetUserAuthAsync(user);
            if (userAuth == null) return answer;
            var listed = await FindInListAsync(new Caller(this, company, userAuth.Token, ct, canRefresh: false), company.CompanyId);
            return listed == ListResult.Found ? PlatformMembership.Member : answer;
        }

        private async Task<PlatformMembership> LookupWithCompanySessionAsync(AppUserCompany company, CancellationToken ct)
        {
            var auth = await _session.TryGetAuthForCompanyAsync(company);
            if (auth == null) return PlatformMembership.Unknown;

            var call = new Caller(this, company, auth.Token, ct);
            var listed = await FindInListAsync(call, company.CompanyId);
            if (listed == ListResult.Found) return PlatformMembership.Member;

            // Not in the list (or the list couldn't be read): ask the platform for a session on the company itself.
            using var response = await call.SendAsync($"/api/Auths/Companies/{company.CompanyId}");
            if (response == null) return PlatformMembership.Unknown;
            if (response.IsSuccessStatusCode)
            {
                var granted = await GrantedCompanyIdAsync(response, ct);
                if (granted == company.CompanyId) return PlatformMembership.Member;
                return granted != null && listed == ListResult.NotFound ? PlatformMembership.NotMember : PlatformMembership.Unknown;
            }
            var refused = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.BadRequest;
            return refused && listed == ListResult.NotFound ? PlatformMembership.NotMember : PlatformMembership.Unknown;
        }

        /// <summary>
        /// Reads every page of the user's company list. The list ends at an empty page, a page shorter than the first
        /// (so a platform that returns fewer rows than asked is still read to the end), or a page that repeats the last.
        /// </summary>
        private static async Task<ListResult> FindInListAsync(Caller call, long companyId)
        {
            int? pageLength = null;
            string? previousPage = null;
            for (var page = 1; page <= MaxPages; page++)
            {
                using var response = await call.SendAsync($"/api/CompanyUsers/MyCompanies?Search=&Page={page}&PageSize={PageSize}");
                if (response == null || !response.IsSuccessStatusCode) return ListResult.Unknown;

                JsonElement? rows;
                using (var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(call.Ct)))
                {
                    rows = Rows(doc.RootElement)?.Clone();
                }
                if (rows == null) return ListResult.Unknown; // unexpected shape: never read it as "not a member"

                var count = rows.Value.GetArrayLength();
                var ids = new List<long>(count);
                foreach (var row in rows.Value.EnumerateArray())
                {
                    if (!TryGetLong(row, "id", out var id)) continue;
                    if (id == companyId) return ListResult.Found;
                    ids.Add(id);
                }

                if (count == 0) return ListResult.NotFound;
                // A platform that ignores the page number answers every page the same: the list has been read.
                var signature = string.Join(',', ids);
                if (signature == previousPage) return ListResult.NotFound;
                previousPage = signature;
                pageLength ??= count;
                if (count < pageLength) return ListResult.NotFound;
            }
            return ListResult.Unknown;
        }

        /// <summary>The company a granted platform session is for (<c>activeCompany.id</c>), or null if unreadable.</summary>
        private static async Task<long?> GrantedCompanyIdAsync(HttpResponseMessage response, CancellationToken ct)
        {
            try
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Name.Equals("activeCompany", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object
                        && TryGetLong(p.Value, "id", out var id))
                        return id;
                return null;
            }
            catch (JsonException) { return null; }
        }

        /// <summary>Sends with the user's platform token, refreshing it once on a 401. Null when the session can't be renewed.</summary>
        private sealed class Caller
        {
            private readonly PlatformCompanyDirectory _owner;
            private readonly AppUserCompany _company;
            private string _token;
            private bool _refreshed;
            public CancellationToken Ct { get; }

            public Caller(PlatformCompanyDirectory owner, AppUserCompany company, string token, CancellationToken ct, bool canRefresh = true)
            {
                _owner = owner; _company = company; _token = token; Ct = ct; _refreshed = !canRefresh;
            }

            public async Task<HttpResponseMessage?> SendAsync(string path)
            {
                var response = await Send(path);
                if (response.StatusCode != HttpStatusCode.Unauthorized || _refreshed) return response;
                response.Dispose();
                _refreshed = true;
                var renewed = await _owner._session.TryRefreshAsync(_company);
                if (renewed == null) return null;
                _token = renewed.Token;
                return await Send(path);
            }

            private Task<HttpResponseMessage> Send(string path)
            {
                var client = _owner._httpClientFactory.CreateClient("Fiscalize");
                var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
                return client.SendAsync(request, Ct);
            }
        }

        /// <summary>The company array: <c>{ "value": [...] }</c>, or a bare array.</summary>
        internal static JsonElement? Rows(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array) return root;
            if (root.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in root.EnumerateObject())
                if (p.Name.Equals("value", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array)
                    return p.Value;
            return null;
        }

        private static bool TryGetLong(JsonElement row, string name, out long value)
        {
            value = 0;
            foreach (var p in row.EnumerateObject())
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return p.Value.ValueKind == JsonValueKind.Number ? p.Value.TryGetInt64(out value)
                         : p.Value.ValueKind == JsonValueKind.String && long.TryParse(p.Value.GetString(), out value);
            return false;
        }
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.AppUserCompanies;
using FiscalizeXero.Api.Services.AppUsers;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.FiscalizeSession;
using FiscalizeXero.Fiscalize.Models.Auths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    /// <summary>
    /// The sign-in loop: a company session from an earlier visit that can't be renewed must be replaced by a new one
    /// opened from the user's own sign-in, so company pages work instead of sending the user back to sign in.
    /// Runs against a fake Fiscalize platform over real HTTP.
    /// </summary>
    public class SessionRecoveryTests : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly string _url;
        private readonly List<string> _calls = new();
        /// <summary>Company the platform answers for /api/Auths/Companies/{id} (null: the one asked for).</summary>
        private long? _answerCompany;

        public SessionRecoveryTests()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            _url = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(_url + "/");
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); } catch { return; }
                var path = ctx.Request.Url!.AbsolutePath;
                var bearer = ctx.Request.Headers["Authorization"]?.Replace("Bearer ", "") ?? "";
                var body = await new StreamReader(ctx.Request.InputStream).ReadToEndAsync();
                lock (_calls) _calls.Add($"{ctx.Request.HttpMethod} {path} {bearer}{(body.Length > 0 ? " " + body : "")}");

                object? reply = null; var status = 200;
                var future = DateTime.UtcNow.AddHours(1);
                if (path == "/api/Auths/RefreshToken")
                {
                    if (body.Contains("user-refresh")) reply = new { token = "user-renewed", tokeExpiry = future, refreshToken = "user-refresh-2" };
                    else status = 401; // the company's old refresh token is dead
                }
                else if (path.StartsWith("/api/Auths/Companies/") && (bearer == "user-live" || bearer == "user-renewed"))
                {
                    var id = long.Parse(path.Split('/').Last());
                    reply = new { token = "company-new", tokeExpiry = future, refreshToken = "company-refresh-new", activeCompany = new { id = _answerCompany ?? id, name = "TUKU" } };
                }
                else status = 401;

                ctx.Response.StatusCode = status;
                var bytes = Encoding.UTF8.GetBytes(reply == null ? "{\"message\":\"no\"}" : JsonSerializer.Serialize(reply));
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        }

        public void Dispose() { _listener.Stop(); _listener.Close(); }

        private (FiscalizeSessionService Service, AppUser User, AppUserCompany Company) Arrange(string? userToken, DateTime? userExpiry, string? userRefresh)
        {
            var user = new AppUser { Id = 1, UserId = 7, FiscalizeToken = userToken, FiscalizeTokenExpiresAt = userExpiry, FiscalizeRefreshToken = userRefresh };
            // A company session from an earlier visit: expired, and its refresh token is no longer accepted.
            var company = new AppUserCompany { Id = 10, AppUserId = 1, CompanyId = 555, FiscalizeToken = "company-old", FiscalizeTokenExpiresAt = DateTime.UtcNow.AddHours(-3), FiscalizeRefreshToken = "company-refresh-dead" };
            user.Companies.Add(company); user.ActiveCompany = company; user.ActiveCompanyId = company.Id;
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Fiscalize:ApiUrl"] = _url }).Build();
            var service = new FiscalizeSessionService(new FakeCurrentUser(user), new FakeUsers(user), new FakeCompanies(company), config, NullLogger<FiscalizeSessionService>.Instance);
            return (service, user, company);
        }

        [Fact]
        public async Task A_dead_company_session_is_reopened_from_the_users_sign_in()
        {
            var (service, _, company) = Arrange("user-live", DateTime.UtcNow.AddHours(1), "user-refresh");

            var auth = await service.TryGetAuthForCompanyAsync(company);

            Assert.Equal("company-new", auth?.Token);
            Assert.Equal("company-new", company.FiscalizeToken);
            Assert.Equal("company-refresh-new", company.FiscalizeRefreshToken);
            Assert.True(company.FiscalizeTokenExpiresAt > DateTime.UtcNow);
            Assert.Contains(_calls, c => c.StartsWith("GET /api/Auths/Companies/555 user-live"));
        }

        [Fact]
        public async Task A_lapsed_user_sign_in_is_renewed_first()
        {
            var (service, user, company) = Arrange("user-old", DateTime.UtcNow.AddMinutes(-1), "user-refresh");

            var auth = await service.TryGetAuthForCompanyAsync(company);

            Assert.Equal("company-new", auth?.Token);
            Assert.Equal("user-renewed", user.FiscalizeToken);
            Assert.Equal("user-refresh-2", user.FiscalizeRefreshToken);
            Assert.Contains(_calls, c => c.StartsWith("GET /api/Auths/Companies/555 user-renewed"));
        }

        [Fact]
        public async Task Without_a_user_sign_in_the_user_must_sign_in_again()
        {
            var (service, _, company) = Arrange(null, null, null);
            Assert.Null(await service.TryGetAuthForCompanyAsync(company));
            Assert.Equal("company-old", company.FiscalizeToken);
        }

        [Fact]
        public async Task A_session_for_a_different_company_is_never_used()
        {
            _answerCompany = 999;
            var (service, _, company) = Arrange("user-live", DateTime.UtcNow.AddHours(1), "user-refresh");
            Assert.Null(await service.TryGetAuthForCompanyAsync(company));
            Assert.Equal("company-old", company.FiscalizeToken);
        }

        [Fact]
        public async Task A_live_company_session_is_used_as_is()
        {
            var (service, _, company) = Arrange("user-live", DateTime.UtcNow.AddHours(1), "user-refresh");
            company.FiscalizeToken = "company-live"; company.FiscalizeTokenExpiresAt = DateTime.UtcNow.AddHours(1);
            Assert.Equal("company-live", (await service.TryGetAuthForCompanyAsync(company))?.Token);
            Assert.Empty(_calls);
        }

        private sealed class FakeCurrentUser(AppUser user) : ICurrentUserService
        {
            public Task<AppUser?> GetCurrentUserAsync() => Task.FromResult<AppUser?>(user);
            public Task<ClientConfig?> GetCurrentClientConfigAsync() => Task.FromResult<ClientConfig?>(null);
            public Task<int?> GetCompanyAnchorIdAsync() => Task.FromResult<int?>(user.ActiveCompanyId);
            public long? GetCurrentUserId() => user.UserId;
        }

        private sealed class FakeUsers(AppUser user) : IAppUserService
        {
            public Task<AppUser?> GetByIdAsync(int id) => Task.FromResult<AppUser?>(id == user.Id ? user : null);
            public Task<AppUser> UpdateAsync(AppUser appUser) => Task.FromResult(appUser);
            public Task<IEnumerable<AppUser>> GetAllAsync() => throw new NotSupportedException();
            public Task<AppUser?> GetByUserIdAsync(long userId) => throw new NotSupportedException();
            public Task<AppUser?> GetByEmailAsync(string email) => throw new NotSupportedException();
            public Task<AppUser> GetOrCreateAppUserAsync(LoginResponseModel loginResponse, string? email = null) => throw new NotSupportedException();
            public Task<AppUser> CreateAsync(AppUser appUser) => throw new NotSupportedException();
            public Task<bool> DeleteAsync(int id) => throw new NotSupportedException();
        }

        private sealed class FakeCompanies(AppUserCompany company) : IAppUserCompanyService
        {
            public Task<AppUserCompany?> GetByIdAsync(int id) => Task.FromResult<AppUserCompany?>(id == company.Id ? company : null);
            public Task<AppUserCompany> UpdateAsync(AppUserCompany c) => Task.FromResult(c);
            public Task<IEnumerable<AppUserCompany>> GetAllAsync() => throw new NotSupportedException();
            public Task<IEnumerable<AppUserCompany>> GetByAppUserIdAsync(int appUserId) => throw new NotSupportedException();
            public Task<AppUserCompany?> GetByAppUserIdAndCompanyIdAsync(int appUserId, long companyId) => throw new NotSupportedException();
            public Task<int> CountMembersAsync(long companyId) => throw new NotSupportedException();
            public Task<int?> GetDataHolderIdAsync(long companyId) => throw new NotSupportedException();
            public Task<AppUserCompany> GetOrCreateAsync(int appUserId, long companyId, string? companyName = null) => throw new NotSupportedException();
            public Task<AppUserCompany> CreateAsync(AppUserCompany c) => throw new NotSupportedException();
            public Task<bool> DeleteAsync(int id) => throw new NotSupportedException();
        }
    }
}

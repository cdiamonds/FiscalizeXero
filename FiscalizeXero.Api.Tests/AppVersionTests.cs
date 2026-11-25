using System.Text.Json;
using FiscalizeXero.Api.Services.Health;
using FiscalizeXero.Api.Services.Version;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    /// <summary>The running version is visible from outside: GET api/version and the /health body.</summary>
    public class AppVersionTests
    {
        [Fact]
        public void The_version_is_the_csproj_version_without_the_commit_suffix()
        {
            var info = AppVersionInfo.Current("Production");
            var csproj = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../FiscalizeXero.Api/FiscalizeXero.Api.csproj"));
            var declared = System.Text.RegularExpressions.Regex.Match(csproj, "<Version>([^<]+)</Version>").Groups[1].Value;
            Assert.Equal(declared, info.Version);
            Assert.DoesNotContain("+", info.Version);
            Assert.True(info.Commit == null || info.Commit.Length <= 12);
            Assert.True(info.StartedAt <= DateTime.UtcNow);
            Assert.Equal("Production", info.Environment);
        }

        private sealed class Env : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Production";
            public string ApplicationName { get; set; } = "FiscalizeXero.Api";
            public string ContentRootPath { get; set; } = "/";
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        [Fact]
        public async Task The_health_body_says_which_version_answers()
        {
            var ctx = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddSingleton<IHostEnvironment>(new Env()).BuildServiceProvider()
            };
            ctx.Response.Body = new MemoryStream();
            var report = new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero);

            await WebhookKeyHealthCheck.ResponseOptions().ResponseWriter(ctx, report);

            ctx.Response.Body.Position = 0;
            using var json = await JsonDocument.ParseAsync(ctx.Response.Body);
            Assert.Equal(AppVersionInfo.Current("x").Version, json.RootElement.GetProperty("version").GetString());
            Assert.True(json.RootElement.TryGetProperty("startedAt", out _));
            Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        }
    }
}

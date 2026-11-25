using FiscalizeXero.Api.Services.Health;
using FiscalizeXero.Api.Services.XeroAuth;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class WebhookKeyHealthCheckTests
    {
        private static WebhookKeyHealthCheck Check(string? key) => new(Options.Create(new XeroOptions { WebhookKey = key }));

        [Fact]
        public async Task Missing_key_is_degraded_and_flagged_false_without_leaking_anything()
        {
            var result = await Check(null).CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Equal(false, result.Data[WebhookKeyHealthCheck.DataKey]);
            Assert.Contains("401", result.Description);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("<paste the webhook key here>")]
        public async Task Blank_or_placeholder_counts_as_missing(string key)
        {
            var result = await Check(key).CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Degraded, result.Status);
        }

        [Fact]
        public async Task Configured_key_is_healthy_and_flagged_true_but_never_in_the_output()
        {
            const string key = "webhook-key-value-that-must-stay-secret";
            var result = await Check(key).CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Equal(true, result.Data[WebhookKeyHealthCheck.DataKey]);
            Assert.DoesNotContain(key, result.Description);
            Assert.All(result.Data.Values, v => Assert.DoesNotContain(key, v.ToString()));
        }

        [Fact]
        public void Startup_warning_only_when_missing_and_names_the_setting_not_the_value()
        {
            Assert.Null(WebhookKeyHealthCheck.StartupWarning("set"));
            var warning = WebhookKeyHealthCheck.StartupWarning(null);
            Assert.NotNull(warning);
            Assert.Contains("Xero webhooks will be rejected with 401", warning);
            Assert.Contains("Xero__WebhookKey", warning);
        }
    }
}

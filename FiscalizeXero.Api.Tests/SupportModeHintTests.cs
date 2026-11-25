using FiscalizeXero.Api.Controllers.Fiscalization;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class SupportModeHintTests
    {
        [Fact]
        public void SupportModeRefusalTellsTheUserToReopenTheApp()
        {
            var m = FiscalizeProxyControllerBase.WithHint("You're viewing TUKU TECHNICAL SERVICES in support mode, which is read-only. Choose \"Act on their behalf\" in the support bar if you need to make this change.");
            Assert.Contains("open Fiscalize for Xero again from the platform", m);
        }

        [Fact]
        public void DeviceAndPlatformRefusalsDontLookLikeASignOut()
        {
            Assert.Equal(424, FiscalizationController.UpstreamStatus(System.Net.HttpStatusCode.Unauthorized));
            Assert.Equal(424, FiscalizationController.UpstreamStatus(System.Net.HttpStatusCode.Forbidden));
            Assert.Equal(400, FiscalizationController.UpstreamStatus(System.Net.HttpStatusCode.BadRequest));
        }

        [Fact]
        public void OtherErrorsAreUnchanged() => Assert.Equal("Device not found", FiscalizeProxyControllerBase.WithHint("Device not found"));
    }
}

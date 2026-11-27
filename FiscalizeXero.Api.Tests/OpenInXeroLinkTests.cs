using FiscalizeXero.Api.Services.XeroAuth;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    /// <summary>"Open Xero" goes to the connected organisation when its short code is known, else to Xero's front door.</summary>
    public class OpenInXeroLinkTests
    {
        [Fact]
        public void With_a_short_code_the_link_opens_that_organisation()
        {
            Assert.Equal("https://go.xero.com/organisationlogin/default.aspx?shortcode=%21Q2vw8",
                XeroOptions.XeroWebUrlFor("!Q2vw8"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Without_a_short_code_the_link_is_xero_itself(string? shortCode)
        {
            Assert.Equal("https://go.xero.com/", XeroOptions.XeroWebUrlFor(shortCode));
        }
    }
}

using FiscalizeXero.Api.Services.FiscalizeSession;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class PlatformSessionTests
    {
        private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void A_future_expiry_is_kept() =>
            Assert.Equal(Now.AddHours(2), PlatformSessions.Expiry(Now.AddHours(2), Now));

        [Fact]
        public void A_missing_expiry_means_the_usual_lifetime_not_already_expired() =>
            Assert.Equal(Now.Add(PlatformSessions.AssumedLifetime), PlatformSessions.Expiry(default, Now));

        [Fact]
        public void A_past_expiry_on_a_token_just_issued_means_the_usual_lifetime() =>
            Assert.Equal(Now.Add(PlatformSessions.AssumedLifetime), PlatformSessions.Expiry(Now.AddMinutes(-5), Now));
    }
}

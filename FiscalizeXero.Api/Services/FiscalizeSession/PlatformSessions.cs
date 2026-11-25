namespace FiscalizeXero.Api.Services.FiscalizeSession
{
    /// <summary>Small rules for platform sessions shared by sign-in, company switch and refresh.</summary>
    public static class PlatformSessions
    {
        /// <summary>How long a platform token is assumed to last when the platform doesn't say.</summary>
        public static readonly TimeSpan AssumedLifetime = TimeSpan.FromMinutes(30);

        /// <summary>
        /// The expiry to store for a token the platform just issued. A missing (default) or already-past expiry would
        /// make every request treat the brand-new token as lapsed and try to renew it straight away, so it is read as
        /// "lasts the usual time"; a token that really is dead is renewed on the platform's first 401.
        /// </summary>
        public static DateTime Expiry(DateTime reported, DateTime? now = null)
        {
            var t = now ?? DateTime.UtcNow;
            return reported == default || reported <= t ? t.Add(AssumedLifetime) : reported;
        }
    }
}

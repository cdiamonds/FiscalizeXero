using FiscalizeXero.Api.Data;
using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Xero.Exceptions;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>
    /// When a document that failed to fiscalise is tried again automatically. Pure, so it can be unit tested.
    ///
    /// Passing problems (Xero or the platform busy or down, a timeout) are retried quickly with a growing wait.
    /// Problems a person has to fix (buyer details, HS code, an unallocated credit note, data the platform refused)
    /// are retried rarely as a safety net, and straight away when the fix is saved here (see
    /// <see cref="IFiscalFailureService.RetrySoonAsync"/>). Xero's daily call limit waits for Xero's midnight-UTC reset.
    /// </summary>
    public static class FailureRetryPolicy
    {
        private static readonly TimeSpan[] Transient =
        {
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2),
            TimeSpan.FromHours(6), TimeSpan.FromHours(12), TimeSpan.FromHours(24), TimeSpan.FromHours(24),
        };

        private static readonly TimeSpan[] NeedsPerson = { TimeSpan.FromHours(6), TimeSpan.FromHours(24), TimeSpan.FromHours(24) };

        /// <summary>The next automatic attempt after the <paramref name="attempts"/>-th failure, or null for none.</summary>
        public static DateTime? Next(Exception error, string category, int attempts, DateTime nowUtc)
        {
            if (error is XeroApiException { IsDailyLimit: true })
                return nowUtc.Date.AddDays(1).AddMinutes(10);

            var schedule = IsTransient(error, category) ? Transient : NeedsPerson;
            var index = Math.Max(0, attempts - 1);
            return index < schedule.Length ? nowUtc + schedule[index] : null;
        }

        internal static bool IsTransient(Exception error, string category) => error switch
        {
            HttpRequestException or TaskCanceledException or TimeoutException => true,
            XeroApiException x => (int)x.StatusCode is 429 or >= 500 || x.StatusCode == 0,
            // The platform refusing the data (4xx with validation errors) needs a person; it being down does not.
            FiscalizeApiException p => (int)p.StatusCode is 0 or 408 or 429 or >= 500,
            _ => category is FiscalFailureCategory.Network,
        };
    }
}

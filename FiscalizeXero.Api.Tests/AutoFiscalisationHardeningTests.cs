using System.Net;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Api.Services.Sync;
using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Xero.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    /// <summary>The rules that keep automatic fiscalisation from losing, repeating or stalling on documents.</summary>
    public class AutoFiscalisationHardeningTests
    {
        private static readonly XeroSyncOptions Options = new();
        private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        private const string A = "11111111-1111-1111-1111-111111111111";

        // ── Sync scheduling ─────────────────────────────────────────────────

        [Fact]
        public void A_connection_that_never_completed_a_run_is_due()
            => Assert.True(SyncPlanner.IsDue(null, null, Now, 1, Options));

        [Fact]
        public void A_failed_run_waits_for_its_backoff_then_runs_at_once()
        {
            Assert.False(SyncPlanner.IsDue(Now.AddMinutes(-1), Now.AddMinutes(5), Now, 1, Options));
            Assert.True(SyncPlanner.IsDue(Now.AddMinutes(-1), Now.AddMinutes(-1), Now, 1, Options));
        }

        [Fact]
        public void A_recent_successful_run_is_not_due()
            => Assert.False(SyncPlanner.IsDue(Now.AddHours(-1), null, Now, 1, Options));

        [Fact]
        public void Backoff_doubles_from_five_minutes_and_stops_at_the_interval()
        {
            Assert.Equal(TimeSpan.FromMinutes(5), SyncPlanner.Backoff(1, Options));
            Assert.Equal(TimeSpan.FromMinutes(10), SyncPlanner.Backoff(2, Options));
            Assert.Equal(TimeSpan.FromMinutes(40), SyncPlanner.Backoff(4, Options));
            Assert.Equal(TimeSpan.FromHours(Options.IntervalHours), SyncPlanner.Backoff(50, Options));
        }

        [Fact]
        public void Modified_since_is_rounded_down_never_up()
        {
            var t = new DateTime(2026, 11, 24, 10, 15, 30, 999, DateTimeKind.Utc);
            Assert.Equal(new DateTime(2026, 11, 24, 10, 15, 30, DateTimeKind.Utc), SyncPlanner.FloorToSecond(t));
            Assert.Equal(DateTimeKind.Utc, SyncPlanner.FloorToSecond(t).Kind);
        }

        [Fact]
        public void A_record_still_waiting_for_its_receipt_is_looked_at_again()
        {
            var changes = new[] { new SyncChange(FiscalDocumentType.Invoice, A, "INV-1", null) };
            var waiting = new[] { new FiscalRecord { DocumentType = FiscalDocumentType.Invoice, DocumentId = A, ReceiptNumber = null } };
            var selected = SyncPlanner.SelectForFiscalisation(changes, new ClientConfig(), waiting, Array.Empty<string>(), out var done, out _, out _);
            Assert.Single(selected);
            Assert.Equal(0, done);
        }

        [Fact]
        public void A_fiscally_cancelled_document_is_not_queued_by_the_sync()
        {
            // The sync passes suppressed documents in with the pending ones.
            var changes = new[] { new SyncChange(FiscalDocumentType.Invoice, A, "INV-1", null) };
            var selected = SyncPlanner.SelectForFiscalisation(changes, new ClientConfig(), Array.Empty<FiscalRecord>(), new[] { A }, out _, out _, out _);
            Assert.Empty(selected);
        }

        // ── Failure retries ─────────────────────────────────────────────────

        [Fact]
        public void A_timeout_is_retried_soon_with_a_growing_wait_and_then_given_up()
        {
            var timeout = new TaskCanceledException("timed out");
            Assert.Equal(Now.AddMinutes(10), FailureRetryPolicy.Next(timeout, FiscalFailureCategory.Network, 1, Now));
            Assert.Equal(Now.AddMinutes(30), FailureRetryPolicy.Next(timeout, FiscalFailureCategory.Network, 2, Now));
            Assert.Equal(Now.AddHours(2), FailureRetryPolicy.Next(timeout, FiscalFailureCategory.Network, 3, Now));
            Assert.Null(FailureRetryPolicy.Next(timeout, FiscalFailureCategory.Network, 8, Now));
        }

        [Fact]
        public void The_platform_being_down_is_transient_but_refused_data_needs_a_person()
        {
            var down = new FiscalizeApiException(HttpStatusCode.BadGateway, "bad gateway");
            var refused = new FiscalizeApiException(HttpStatusCode.BadRequest, "invalid buyer");
            Assert.Equal(Now.AddMinutes(10), FailureRetryPolicy.Next(down, FiscalFailureCategory.Platform, 1, Now));
            Assert.Equal(Now.AddHours(6), FailureRetryPolicy.Next(refused, FiscalFailureCategory.Buyer, 1, Now));
            Assert.Null(FailureRetryPolicy.Next(refused, FiscalFailureCategory.Buyer, 4, Now));
        }

        [Fact]
        public void Xero_rate_limit_is_transient_and_the_daily_limit_waits_for_midnight_utc()
        {
            var minute = new XeroApiException(HttpStatusCode.TooManyRequests, "slow down");
            Assert.Equal(Now.AddMinutes(10), FailureRetryPolicy.Next(minute, FiscalFailureCategory.Xero, 1, Now));
            var day = new XeroApiException(HttpStatusCode.TooManyRequests, "limit", rateLimitProblem: "day");
            Assert.Equal(new DateTime(2026, 11, 25, 0, 10, 0, DateTimeKind.Utc), FailureRetryPolicy.Next(day, FiscalFailureCategory.Xero, 1, Now));
        }

        [Fact]
        public void A_missing_original_invoice_is_retried_rarely_as_a_safety_net()
        {
            var error = new FiscalizeXero.Api.Exceptions.UserFacingException("Invoice INV-9 has not been fiscalized here.");
            Assert.Equal(Now.AddHours(6), FailureRetryPolicy.Next(error, FiscalFailureCategory.OriginalSale, 1, Now));
            Assert.Equal(Now.AddHours(24), FailureRetryPolicy.Next(error, FiscalFailureCategory.OriginalSale, 2, Now));
        }

        // ── Token persistence ───────────────────────────────────────────────

        [Fact]
        public async Task Saving_refreshed_tokens_from_a_stale_copy_keeps_settings_changed_meanwhile()
        {
            var name = Guid.NewGuid().ToString();
            DataContext Db() => new(new DbContextOptionsBuilder<DataContext>().UseInMemoryDatabase(name).Options);

            await using (var seed = Db())
            {
                seed.Configs.Add(new ClientConfig { Id = 7, TenantId = Guid.NewGuid(), RefreshToken = "r1", AccessToken = "a1", AutoFiscalizeInvoices = true });
                await seed.SaveChangesAsync();
            }

            // A worker loads its copy...
            ClientConfig stale;
            await using (var worker = Db()) stale = await worker.Configs.AsNoTracking().SingleAsync(c => c.Id == 7);

            // ...a user switches auto-fiscalise off meanwhile...
            await using (var user = Db())
            {
                var row = await user.Configs.SingleAsync(c => c.Id == 7);
                row.AutoFiscalizeInvoices = false;
                await user.SaveChangesAsync();
            }

            // ...then the worker saves rotated tokens from its stale copy.
            stale.AccessToken = "a2";
            stale.RefreshToken = "r2";
            await using (var save = Db()) await new ClientConfigRepository(save).SaveTokensAsync(stale);

            await using var check = Db();
            var final = await check.Configs.AsNoTracking().SingleAsync(c => c.Id == 7);
            Assert.Equal("r2", final.RefreshToken);
            Assert.Equal("a2", final.AccessToken);
            Assert.False(final.AutoFiscalizeInvoices); // the user's change survived
        }
    }
}

namespace FiscalizeXero.Api.Tests
{
    using FiscalizeXero.Api.Security;

    public class OAuthAndSecretsHardeningTests
    {
        [Fact]
        public void A_new_consent_state_is_fresh_and_expires_after_thirty_minutes()
        {
            var state = OAuthState.New();
            var now = DateTime.UtcNow;
            Assert.True(OAuthState.IsFresh(state, now));
            Assert.True(OAuthState.IsFresh(state, now.AddMinutes(29)));
            Assert.False(OAuthState.IsFresh(state, now.AddMinutes(31)));
        }

        [Fact]
        public void A_state_from_before_the_change_is_not_accepted()
            => Assert.False(OAuthState.IsFresh(Guid.NewGuid(), DateTime.UtcNow)); // version 4: no timestamp

        [Fact]
        public void A_secret_encrypted_with_another_key_reads_as_missing_instead_of_throwing()
        {
            var oldKey = new AesGcmSecretProtector("an old signing key that is at least 32 bytes long!!");
            var newKey = new AesGcmSecretProtector("the new signing key, also at least 32 bytes long!!!");
            var stored = oldKey.Protect("refresh-token");
            Exception? seen = null;
            ProtectedStringConverter.OnUnreadable = ex => seen = ex;
            try
            {
                Assert.Null(ProtectedStringConverter.ReadOrNull(newKey, stored));
                Assert.NotNull(seen);
                Assert.Equal("refresh-token", ProtectedStringConverter.ReadOrNull(oldKey, stored));
            }
            finally { ProtectedStringConverter.OnUnreadable = null; }
        }
    }
}

namespace FiscalizeXero.Api.Tests
{
    public class SafeErrorMessageTests
    {
        [Fact]
        public void An_unexpected_error_shows_plain_words_not_its_internal_text()
        {
            var (category, message, details) = FiscalizeXero.Api.Services.Fiscalization.FiscalFailureService.Classify(
                new InvalidOperationException("The instance of entity type 'FiscalRecord' cannot be tracked because another instance with the key value '{Id: 5}' is already being tracked."));
            Assert.Equal("Other", category);
            Assert.DoesNotContain("tracked", message);
            Assert.Equal(new[] { "InvalidOperationException" }, details);
        }

        [Fact]
        public void A_platform_error_page_is_not_shown_raw()
        {
            var ex = new FiscalizeXero.Fiscalize.Exceptions.FiscalizeApiException(System.Net.HttpStatusCode.BadGateway, "<!DOCTYPE html><html><body>502 Bad Gateway nginx</body></html>");
            Assert.DoesNotContain("<", ex.Message);
            Assert.Contains("error page", ex.Message);
            Assert.Contains("nginx", ex.ResponseBody); // still there for the log
        }

        [Fact]
        public void The_apps_own_plain_messages_still_read_as_written()
            => Assert.Equal("Device #5 has no API key.", new FiscalizeXero.Fiscalize.Exceptions.FiscalizeApiException(System.Net.HttpStatusCode.BadRequest, "Device #5 has no API key.").Message);
    }
}

namespace FiscalizeXero.Api.Tests
{
    using FiscalizeXero.Api.Data;
    using Microsoft.EntityFrameworkCore;

    public class StoredTimesTests
    {
        [Fact]
        public async Task Times_read_back_from_the_database_are_marked_utc_so_browsers_show_local_time_correctly()
        {
            var name = Guid.NewGuid().ToString();
            DataContext Db() => new(new DbContextOptionsBuilder<DataContext>().UseInMemoryDatabase(name).Options);
            await using (var db = Db())
            {
                db.FiscalRecords.Add(new FiscalRecord { DocumentId = "a", DocumentType = "invoice", DocumentNumber = "INV-1",
                    FiscalizedAt = new DateTime(2026, 11, 24, 19, 52, 0, DateTimeKind.Unspecified) });
                db.Configs.Add(new ClientConfig { LastSyncRunAt = new DateTime(2026, 11, 24, 20, 0, 0, DateTimeKind.Unspecified) });
                await db.SaveChangesAsync();
            }
            await using var read = Db();
            Assert.Equal(DateTimeKind.Utc, (await read.FiscalRecords.SingleAsync()).FiscalizedAt.Kind);
            Assert.Equal(DateTimeKind.Utc, (await read.Configs.SingleAsync()).LastSyncRunAt!.Value.Kind);
        }
    }
}

using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class SyncPlannerTests
    {
        private static readonly XeroSyncOptions Options = new();
        private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        private const string A = "11111111-1111-1111-1111-111111111111", B = "22222222-2222-2222-2222-222222222222", C = "33333333-3333-3333-3333-333333333333";

        [Fact]
        public void First_run_looks_back_sixty_minutes_only()
        {
            Assert.Equal(60, Options.InitialLookbackMinutes);
            Assert.Equal(Now.AddMinutes(-60), SyncPlanner.ModifiedSince(null, Now, Options));
        }

        [Fact]
        public void Later_runs_overlap_the_previous_one()
        {
            var last = Now.AddHours(-6);
            Assert.Equal(last.AddMinutes(-Options.OverlapMinutes), SyncPlanner.ModifiedSince(last, Now, Options));
        }

        [Fact]
        public void A_long_gap_is_covered_in_full_because_Xero_has_no_change_window()
        {
            var last = Now.AddDays(-45);
            Assert.Equal(last.AddMinutes(-Options.OverlapMinutes), SyncPlanner.ModifiedSince(last, Now, Options));
        }

        [Fact]
        public void Due_when_never_run_or_interval_plus_jitter_elapsed()
        {
            Assert.True(SyncPlanner.IsDue(null, Now, 1, Options));
            Assert.False(SyncPlanner.IsDue(Now.AddHours(-1), Now, 1, Options));
            // Past the interval plus the maximum jitter: always due.
            Assert.True(SyncPlanner.IsDue(Now.AddHours(-Options.IntervalHours).AddMinutes(-Options.JitterMinutes), Now, 1, Options));
        }

        [Fact]
        public void Jitter_is_fixed_per_company_and_within_range()
        {
            var seen = new HashSet<double>();
            for (long id = 1; id <= 200; id++)
            {
                var j = SyncPlanner.Jitter(id, 45);
                Assert.Equal(j, SyncPlanner.Jitter(id, 45));
                Assert.InRange(j.TotalMinutes, 0, 44);
                seen.Add(j.TotalMinutes);
            }
            Assert.True(seen.Count > 20, "companies should spread across many offsets");
            Assert.Equal(TimeSpan.Zero, SyncPlanner.Jitter(7, 0));
        }

        [Fact]
        public void Dedups_against_fiscal_records_pending_events_and_switches()
        {
            var changes = new[]
            {
                new SyncChange(FiscalDocumentType.Invoice, A, "INV-1", null),
                new SyncChange(FiscalDocumentType.Invoice, A, "INV-1", null),      // on two pages of one run
                new SyncChange(FiscalDocumentType.Invoice, B, "INV-2", null),
                new SyncChange(FiscalDocumentType.CreditNote, C, "CN-1", null),
                new SyncChange("bill", Guid.NewGuid().ToString(), "B-1", null),    // not fiscalisable
            };
            var config = new ClientConfig { AutoFiscalizeInvoices = true, AutoFiscalizeCreditNotes = false };
            var existing = new[] { new FiscalRecord { DocumentType = FiscalDocumentType.Invoice, DocumentId = A, ReceiptNumber = 1 } };

            var selected = SyncPlanner.SelectForFiscalisation(changes, config, existing, new[] { B.ToUpperInvariant() },
                out var done, out var pending, out var off);

            Assert.Empty(selected);           // A done, B pending, C switched off, the bill ignored
            Assert.Equal((1, 1, 1), (done, pending, off));
        }

        [Fact]
        public void A_new_document_is_selected_once()
        {
            var changes = new[] { new SyncChange(FiscalDocumentType.CreditNote, C, "CN-1", null), new SyncChange(FiscalDocumentType.CreditNote, C, "CN-1", null) };
            var selected = SyncPlanner.SelectForFiscalisation(changes, new ClientConfig(), Array.Empty<FiscalRecord>(), Array.Empty<string>(), out _, out _, out _);
            Assert.Single(selected);
            Assert.Equal(C, selected[0].DocumentId);
        }

        [Fact]
        public void One_sync_worker_is_registered_and_reads_the_Xero_Sync_section()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Xero:Sync:IntervalHours"] = "2" }).Build();
            var services = new ServiceCollection().AddLogging();
            services.AddXeroBackgroundSync(config);
            Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
            Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(XeroSyncService));
            Assert.Equal(2, services.BuildServiceProvider().GetRequiredService<IOptions<XeroSyncOptions>>().Value.IntervalHours);
        }

        [Fact]
        public void Sync_defaults()
        {
            Assert.True(new XeroSyncOptions().Enabled);
            Assert.Equal(6, new XeroSyncOptions().IntervalHours);
        }
    }
}

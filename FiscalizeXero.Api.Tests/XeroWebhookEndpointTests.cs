using System.Text;
using FiscalizeXero.Api.Controllers.Xero;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Api.Services.Sync;
using FiscalizeXero.Api.Services.Webhooks;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Shared.Models.Fiscalize;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    /// <summary>
    /// POST api/xero/webhooks. Xero's "intent to receive" check needs 200 with an empty body for a correctly signed
    /// delivery and 401 for anything else.
    /// </summary>
    public class XeroWebhookEndpointTests
    {
        private const string Key = "test-webhook-key";
        private static readonly Guid Tenant = Guid.Parse("c2cc9b6e-9458-4c7d-93cc-f02b81b0594f");
        private static readonly Guid Doc = Guid.Parse("717f2bfc-c6d4-41fd-b238-3f2f0c0cf777");

        private static string Delivery(string category = "INVOICE", string type = "UPDATE") =>
            $"{{\"events\":[{{\"resourceUrl\":\"https://api.xero.com/api.xro/2.0/Invoices/{Doc}\",\"resourceId\":\"{Doc}\",\"tenantId\":\"{Tenant}\",\"tenantType\":\"ORGANISATION\",\"eventCategory\":\"{category}\",\"eventType\":\"{type}\",\"eventDateUtc\":\"2026-11-23T01:15:39.902\"}}],\"firstEventSequence\":1,\"lastEventSequence\":1,\"entropy\":\"S0m3r4Nd0m\"}}";

        private const string IntentToReceive = "{\"events\":[],\"firstEventSequence\":0,\"lastEventSequence\":0,\"entropy\":\"ABCDEFGH\"}";

        private sealed class Harness
        {
            public DataContext Db = new(new DbContextOptionsBuilder<DataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            public WebhookQueue Queue = new();
            public WebhooksController Controller;
            public MemoryStream ResponseBody = new();

            public Harness(string? key = Key)
            {
                Controller = new WebhooksController(Db, Queue, null!, Options.Create(new XeroOptions { WebhookKey = key }),
                    Options.Create(new XeroSyncOptions()), new ConfigurationBuilder().Build(), NullLogger<WebhooksController>.Instance);
            }

            public async Task<int> PostAsync(byte[] body, string? signature)
            {
                var http = new DefaultHttpContext();
                http.Request.Method = "POST";
                http.Request.Body = new MemoryStream(body);
                http.Request.ContentLength = body.Length;
                if (signature != null) http.Request.Headers[XeroWebhookSignature.HeaderName] = signature;
                http.Response.Body = ResponseBody;
                http.RequestServices = new ServiceCollection().AddLogging().AddMvcCore().Services.BuildServiceProvider();
                Controller.ControllerContext = new ControllerContext { HttpContext = http };

                var result = await Controller.ReceiveAsync();
                await result.ExecuteResultAsync(new ActionContext(http, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
                return http.Response.StatusCode;
            }

            public Task<int> PostAsync(string body, string? signature) => PostAsync(Encoding.UTF8.GetBytes(body), signature);
        }

        private static string Sign(string body, string key = Key) => XeroWebhookSignature.Compute(Encoding.UTF8.GetBytes(body), key);

        [Fact]
        public async Task Intent_to_receive_with_a_good_signature_is_200_with_an_empty_body()
        {
            var h = new Harness();
            Assert.Equal(200, await h.PostAsync(IntentToReceive, Sign(IntentToReceive)));
            Assert.Equal(0, h.ResponseBody.Length);
            Assert.Empty(h.Db.WebhookEvents);
        }

        [Fact]
        public async Task Intent_to_receive_with_a_bad_signature_is_401_with_an_empty_body()
        {
            var h = new Harness();
            Assert.Equal(401, await h.PostAsync(IntentToReceive, Sign(IntentToReceive, "some-other-key")));
            Assert.Equal(0, h.ResponseBody.Length);
        }

        [Theory]
        [InlineData(null)]                 // no header
        [InlineData("")]
        [InlineData("not base64 !!")]
        [InlineData("AAAA")]
        public async Task A_missing_or_malformed_signature_is_401_never_200_or_400(string? signature)
        {
            var h = new Harness();
            Assert.Equal(401, await h.PostAsync(Delivery(), signature));
            Assert.Empty(h.Db.WebhookEvents);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task With_no_webhook_key_configured_everything_is_401(string? key)
        {
            var h = new Harness(key);
            Assert.Equal(401, await h.PostAsync(Delivery(), Sign(Delivery())));
            Assert.Equal(401, await h.PostAsync(Delivery(), Sign(Delivery(), "")));
            Assert.Equal(401, await h.PostAsync(IntentToReceive, null));
            Assert.Empty(h.Db.WebhookEvents);
        }

        [Fact]
        public async Task The_signature_covers_the_raw_bytes_so_one_changed_byte_is_401()
        {
            var h = new Harness();
            var signature = Sign(Delivery());
            Assert.Equal(401, await h.PostAsync(Delivery() + " ", signature));             // trailing space
            Assert.Equal(401, await h.PostAsync(Delivery().Replace("UPDATE", "CREATE"), signature));
            // A body that is not valid UTF-8 is still verified on its bytes.
            var raw = new byte[] { 0xFF, 0xFE, 0x7B, 0x7D };
            Assert.Equal(401, await h.PostAsync(raw, signature));
        }

        [Fact]
        public async Task A_signed_delivery_is_stored_queued_and_answered_200_empty()
        {
            var h = new Harness();
            Assert.Equal(200, await h.PostAsync(Delivery(), Sign(Delivery())));
            Assert.Equal(0, h.ResponseBody.Length);

            var evt = Assert.Single(h.Db.WebhookEvents);
            Assert.Equal((Tenant, "Invoice", Doc.ToString(), "Update", WebhookEventSource.Webhook, WebhookEventStatus.Pending),
                (evt.TenantId, evt.EntityName, evt.EntityId, evt.Operation, evt.Source, evt.Status));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await foreach (var id in h.Queue.ReadAllAsync(cts.Token)) { Assert.Equal(evt.Id, id); break; }
        }

        [Fact]
        public async Task Credit_notes_are_stored_and_other_categories_ignored()
        {
            var h = new Harness();
            var credit = Delivery("CREDITNOTE", "CREATE");
            Assert.Equal(200, await h.PostAsync(credit, Sign(credit)));
            var contact = Delivery("CONTACT");
            Assert.Equal(200, await h.PostAsync(contact, Sign(contact)));
            var evt = Assert.Single(h.Db.WebhookEvents);
            Assert.Equal(("CreditNote", "Create"), (evt.EntityName, evt.Operation));
        }

        [Fact]
        public async Task A_correctly_signed_body_that_is_not_json_is_still_200()
        {
            var h = new Harness();
            Assert.Equal(200, await h.PostAsync("not json", Sign("not json")));
            Assert.Empty(h.Db.WebhookEvents);
        }

        [Fact]
        public void The_endpoint_is_anonymous_and_the_status_endpoint_is_not()
        {
            var receive = typeof(WebhooksController).GetMethod(nameof(WebhooksController.ReceiveAsync))!;
            Assert.NotEmpty(receive.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false));
            var status = typeof(WebhooksController).GetMethod(nameof(WebhooksController.GetStatusAsync))!;
            Assert.NotEmpty(status.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false));
            Assert.Equal("/api/xero/webhooks", WebhooksController.CallbackPath);
        }
    }

    /// <summary>The worker behind both webhooks and the sync.</summary>
    public class WebhookProcessingTests
    {
        private static readonly Guid Tenant = Guid.Parse("c2cc9b6e-9458-4c7d-93cc-f02b81b0594f");
        private static readonly Guid Doc = Guid.Parse("717f2bfc-c6d4-41fd-b238-3f2f0c0cf777");

        private sealed class FakeFiscalization : IFiscalizationService
        {
            public Func<string, Guid, FiscalizeResultModel> OnFiscalize = (_, _) => new FiscalizeResultModel { DocumentNumber = "INV-1", ReceiptNumber = 1024 };
            public List<(string Type, Guid Id, int Company)> Calls = new();
            public Task<FiscalizeResultModel> FiscalizeForConfigAsync(ClientConfig config, AppUserCompany appUserCompany, string documentType, Guid documentId)
            {
                Calls.Add((documentType, documentId, config.AppUserCompanyId));
                return Task.FromResult(OnFiscalize(documentType, documentId));
            }
            public Task<FiscalizeResultModel> FiscalizeInvoiceAsync(Guid invoiceId) => throw new NotSupportedException();
            public Task<FiscalizeResultModel> FiscalizeCreditNoteAsync(Guid creditNoteId, string? baseDocNo = null) => throw new NotSupportedException();
            public Task<CreditNoteBaseDocumentModel> GetCreditNoteBaseDocumentAsync(Guid creditNoteId) => throw new NotSupportedException();
            public Task<FiscalizeResultModel> RefiscalizeInvoiceAsync(Guid invoiceId) => throw new NotSupportedException();
            public Task<FiscalizeResultModel> UpdateInvoiceFiscalInfoAsync(Guid invoiceId, UpdateInvoiceFiscalInfoRequest update) => throw new NotSupportedException();
            public Task<FiscalCancelResultModel> FiscalCancelInvoiceAsync(Guid invoiceId) => throw new NotSupportedException();
            public Task<FiscalizeResultModel> RefiscalizeCreditNoteAsync(Guid creditNoteId, string? baseDocNo = null) => throw new NotSupportedException();
            public Task<byte[]> GetInvoiceFiscalizePdfAsync(Guid invoiceId) => throw new NotSupportedException();
            public Task<byte[]> GetCreditNoteFiscalizePdfAsync(Guid creditNoteId) => throw new NotSupportedException();
        }

        private sealed class Harness
        {
            public FakeFiscalization Fiscalization = new();
            public ServiceProvider Services;
            public WebhookProcessingService Worker;

            public Harness()
            {
                var name = Guid.NewGuid().ToString();
                Services = new ServiceCollection()
                    .AddDbContext<DataContext>(o => o.UseInMemoryDatabase(name))
                    .AddScoped<IFiscalRecordRepository, FiscalRecordRepository>()
                    .AddSingleton<IFiscalizationService>(Fiscalization)
                    .BuildServiceProvider();
                Worker = new WebhookProcessingService(Services, new WebhookQueue(), NullLogger<WebhookProcessingService>.Instance);
            }

            public T Db<T>(Func<DataContext, T> work)
            {
                using var scope = Services.CreateScope();
                return work(scope.ServiceProvider.GetRequiredService<DataContext>());
            }

            public int Connect(long companyId, Guid tenant, bool autoCredit = true)
            {
                return Db(db =>
                {
                    var user = new AppUser { UserId = companyId };
                    var auc = new AppUserCompany { AppUser = user, CompanyId = companyId };
                    db.AddRange(user, auc);
                    db.SaveChanges();
                    db.Configs.Add(new ClientConfig { AppUserCompanyId = auc.Id, State = Guid.NewGuid(), TenantId = tenant, RefreshToken = "rt", AutoFiscalizeCreditNotes = autoCredit });
                    db.SaveChanges();
                    return auc.Id;
                });
            }

            public async Task<WebhookEvent> RunAsync(string entity = "Invoice", string operation = "Update", string? entityId = null, string source = WebhookEventSource.Webhook)
            {
                var id = Db(db =>
                {
                    var e = new WebhookEvent { TenantId = Tenant, EntityName = entity, EntityId = entityId ?? Doc.ToString(), Operation = operation, Source = source };
                    db.WebhookEvents.Add(e);
                    db.SaveChanges();
                    return e.Id;
                });
                await Worker.ProcessAsync(id, CancellationToken.None);
                return Db(db => db.WebhookEvents.AsNoTracking().Single(e => e.Id == id));
            }
        }

        [Fact]
        public async Task A_change_is_fiscalised_through_the_shared_service_with_the_companys_connection()
        {
            var h = new Harness();
            var company = h.Connect(1, Tenant);
            var evt = await h.RunAsync();
            Assert.Equal(WebhookEventStatus.Processed, evt.Status);
            Assert.Equal(("INV-1", company), (evt.DocumentNumber, evt.AppUserCompanyId!.Value));
            Assert.Equal((FiscalDocumentType.Invoice, Doc, company), Assert.Single(h.Fiscalization.Calls));
        }

        [Fact]
        public async Task A_draft_or_a_purchase_bill_is_skipped_with_the_reason_not_failed()
        {
            var h = new Harness();
            h.Connect(1, Tenant);
            h.Fiscalization.OnFiscalize = (_, _) => throw new DocumentNotFiscalisableException("Invoice INV-9 is still a draft in Xero. Approve it in Xero, then fiscalise it.");
            var evt = await h.RunAsync();
            Assert.Equal(WebhookEventStatus.Skipped, evt.Status);
            Assert.Contains("still a draft in Xero", evt.Detail);
        }

        [Fact]
        public async Task A_credit_note_that_failed_unallocated_is_retried_by_the_next_update_and_then_succeeds()
        {
            var h = new Harness();
            h.Connect(1, Tenant);
            h.Fiscalization.OnFiscalize = (_, _) => throw new FiscalizeXero.Api.Exceptions.UserFacingException(CreditNoteOriginalInvoice.UnknownMessage("CN-7"));
            var first = await h.RunAsync("CreditNote");
            Assert.Equal(WebhookEventStatus.Failed, first.Status);
            Assert.Contains("has not been allocated to an invoice in Xero", first.Detail);

            // The user allocates the credit in Xero; Xero sends UPDATE for the same credit note.
            h.Fiscalization.OnFiscalize = (_, _) => new FiscalizeResultModel { DocumentNumber = "CN-7", ReceiptNumber = 2048 };
            var second = await h.RunAsync("CreditNote");
            Assert.Equal(WebhookEventStatus.Processed, second.Status);
            Assert.Equal(2, h.Fiscalization.Calls.Count);
            Assert.All(h.Fiscalization.Calls, c => Assert.Equal(FiscalDocumentType.CreditNote, c.Type));
        }

        [Fact]
        public async Task An_already_fiscalised_document_is_skipped_without_touching_Xero()
        {
            var h = new Harness();
            var company = h.Connect(1, Tenant);
            h.Db(db => { db.FiscalRecords.Add(new FiscalRecord { AppUserCompanyId = company, DocumentId = Doc.ToString(), DocumentType = FiscalDocumentType.Invoice, DocumentNumber = "INV-1", ReceiptNumber = 7 }); return db.SaveChanges(); });
            var evt = await h.RunAsync(source: WebhookEventSource.Sync);
            Assert.Equal(WebhookEventStatus.Skipped, evt.Status);
            Assert.Contains("Already fiscalised (receipt 7)", evt.Detail);
            Assert.Empty(h.Fiscalization.Calls);
        }

        [Fact]
        public async Task Switched_off_unknown_and_unconnected_changes_are_skipped()
        {
            var h = new Harness();
            Assert.Contains("No connected company", (await h.RunAsync()).Detail);

            h.Connect(1, Tenant, autoCredit: false);
            Assert.Contains("switched off", (await h.RunAsync("CreditNote")).Detail);
            Assert.Equal(WebhookEventStatus.Skipped, (await h.RunAsync("Contact")).Status);
            Assert.Equal(WebhookEventStatus.Skipped, (await h.RunAsync(entityId: "45")).Status);        // not a Xero id
            Assert.Contains("not fiscalised", (await h.RunAsync(operation: "Delete")).Detail);
            Assert.Empty(h.Fiscalization.Calls);
        }

        [Fact]
        public async Task An_organisation_connected_to_two_companies_is_never_fiscalised()
        {
            var h = new Harness();
            h.Connect(1, Tenant);
            h.Connect(2, Tenant);
            var evt = await h.RunAsync();
            Assert.Equal(WebhookEventStatus.Skipped, evt.Status);
            Assert.Contains("more than one Fiscalize company", evt.Detail);
            Assert.Empty(h.Fiscalization.Calls);
        }
    }
}

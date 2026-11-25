using FiscalizeXero.Xero;
using FiscalizeXero.Xero.Converters;
using FiscalizeXero.Xero.Exceptions;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.OAuth;
using FiscalizeXero.Xero.Services;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace FiscalizeXero.Api.Tests
{
    public class XeroClientTests
    {
        // ---- dates

        [Fact]
        public void XerosMicrosoftStyleDatesAreReadAsUtc()
        {
            Assert.Equal(new DateTime(2015, 8, 13, 2, 52, 36, 790, DateTimeKind.Utc), XeroDateConverter.Parse("/Date(1439434356790+0000)/"));
            Assert.Equal(new DateTime(2015, 8, 13, 2, 52, 36, 790, DateTimeKind.Utc), XeroDateConverter.Parse("/Date(1439434356790)/"));
            Assert.Equal(new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc), XeroDateConverter.Parse("2026-11-23T00:00:00"));
            Assert.Null(XeroDateConverter.Parse(""));
            Assert.Null(XeroDateConverter.Parse("not a date"));
        }

        [Fact]
        public void AnInvoiceAsXeroSendsItIsReadWithItsLinesAndDates()
        {
            const string json = """
            {"Invoices":[{"Type":"ACCREC","InvoiceID":"243216c5-369e-4056-ac67-05388f86dc81","InvoiceNumber":"INV-0005",
              "Reference":"PO 88","Contact":{"ContactID":"025867f1-d741-4d6b-b1af-9ac774b59ba7","Name":"City Agency"},
              "DateString":"2026-11-20T00:00:00","Date":"/Date(1795132800000+0000)/","DueDateString":"2026-12-20T00:00:00",
              "Status":"AUTHORISED","LineAmountTypes":"Exclusive","SubTotal":100.00,"TotalTax":15.00,"Total":115.00,
              "AmountDue":115.00,"AmountPaid":0.00,"CurrencyCode":"USD","CurrencyRate":1.0,"UpdatedDateUTC":"/Date(1795219200000+0000)/",
              "LineItems":[{"LineItemID":"5f7a612b-fdcc-4d33-90fa-a9f6bc6db32f","ItemCode":"MILK","Description":"Milk 1L","Quantity":2.0000,
                "UnitAmount":50.00,"TaxType":"OUTPUT","TaxAmount":15.00,"LineAmount":100.00,"Item":{"ItemID":"aa7a612b-fdcc-4d33-90fa-a9f6bc6db32f","Name":"Milk","Code":"MILK"}}]}],
             "pagination":{"page":1,"pageSize":50,"pageCount":1,"itemCount":1}}
            """;
            var envelope = JsonSerializer.Deserialize<InvoicesEnvelope>(json, RestClient.ReadOptions)!;
            var invoice = Assert.Single(envelope.Invoices!);

            Assert.True(invoice.IsSale);
            Assert.True(invoice.IsApproved);
            Assert.False(invoice.IsTaxInclusive);
            Assert.Equal("2026-11-20", invoice.Date);
            Assert.Equal(115.00m, invoice.Total);
            Assert.Equal(DateTimeKind.Utc, invoice.UpdatedDateUTC!.Value.Kind);
            Assert.Equal(1, envelope.Pagination!.ItemCount);
            var line = Assert.Single(invoice.LineItems);
            Assert.Equal(("MILK", 2m, 15.00m, "Milk"), (line.ItemCode, line.Quantity, line.TaxAmount, line.Item!.Name));
        }

        [Fact]
        public void ACreditNotesAllocationsNameTheInvoicesItWasAppliedTo()
        {
            const string json = """
            {"CreditNotes":[{"Type":"ACCRECCREDIT","CreditNoteID":"aea95d78-ea48-456b-9b08-6bc012600072","CreditNoteNumber":"CN-0002",
              "Status":"PAID","Total":50.00,"RemainingCredit":0.00,
              "Allocations":[{"Amount":50.00,"Date":"/Date(1795132800000+0000)/","Invoice":{"InvoiceID":"243216c5-369e-4056-ac67-05388f86dc81","InvoiceNumber":"INV-0005"}}]}]}
            """;
            var note = Assert.Single(JsonSerializer.Deserialize<CreditNotesEnvelope>(json, RestClient.ReadOptions)!.CreditNotes!);
            Assert.True(note.IsCustomerCredit);
            Assert.Equal("INV-0005", Assert.Single(note.Allocations!).Invoice!.InvoiceNumber);
        }

        // ---- errors

        [Fact]
        public void ValidationErrorsFromXeroBecomeTheMessage()
        {
            const string body = """
            {"ErrorNumber":10,"Type":"ValidationException","Message":"A validation exception occurred",
             "Elements":[{"InvoiceID":"x","ValidationErrors":[{"Message":"Invoice not of valid status for modification"},{"Message":"This document cannot be edited as it has a payment or credit note allocated to it."}]}]}
            """;
            var ex = new XeroApiException(HttpStatusCode.BadRequest, body, "corr-1");

            Assert.True(ex.IsValidation);
            Assert.Equal(10, ex.XeroErrorNumber);
            Assert.Equal("ValidationException", ex.ErrorType);
            Assert.Equal(2, ex.ValidationErrors.Count);
            Assert.StartsWith("Invoice not of valid status for modification; This document cannot be edited", ex.Message);
            Assert.Contains("xero_correlation_id=corr-1", ex.ToString());
        }

        [Fact]
        public void GatewayAndOAuthErrorShapesAreUnderstood()
        {
            var unauthorised = new XeroApiException(HttpStatusCode.Unauthorized,
                """{"Type":null,"Title":"Unauthorized","Status":401,"Detail":"TokenExpired: token expired at 11/23/2026 07:00:00"}""");
            Assert.True(unauthorised.IsAuthenticationFailure);
            Assert.StartsWith("TokenExpired", unauthorised.Message);

            var oauth = new XeroApiException(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
            Assert.Equal("invalid_grant", oauth.Message);

            Assert.Equal("Xero could not find that record.", new XeroApiException(HttpStatusCode.NotFound, "").Message);
            // An HTML error page is never shown to the user.
            Assert.DoesNotContain("<html", new XeroApiException(HttpStatusCode.BadGateway, "<html><body>Bad gateway</body></html>").Message);
        }

        [Fact]
        public void ADailyLimitIsToldApartFromAMinuteLimit()
        {
            var day = new XeroApiException(HttpStatusCode.TooManyRequests, "", null, "day");
            var minute = new XeroApiException(HttpStatusCode.TooManyRequests, "", null, "minute");

            Assert.True(day.IsDailyLimit);
            Assert.Contains("daily limit", day.Message);
            Assert.False(minute.IsDailyLimit);
            Assert.True(minute.IsThrottled);
        }

        // ---- retries

        [Fact]
        public void AMinuteRateLimitIsWaitedOutButADailyOneIsNot()
        {
            using var minute = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            minute.Headers.TryAddWithoutValidation("Retry-After", "42");
            using var day = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            day.Headers.TryAddWithoutValidation("Retry-After", "30000");
            day.Headers.TryAddWithoutValidation("X-Rate-Limit-Problem", "day");

            Assert.True(XeroRetryPolicy.ShouldRetry(minute, HttpMethod.Post, 1));
            Assert.Equal(TimeSpan.FromSeconds(42), XeroRetryPolicy.DelayFor(1, XeroRetryPolicy.RetryAfter(minute)));
            Assert.False(XeroRetryPolicy.ShouldRetry(day, HttpMethod.Get, 1));
            Assert.Equal("day", XeroRetryPolicy.RateLimitProblem(day));
        }

        [Fact]
        public void WritesAreNotRepeatedAfterAServerErrorThatMayHaveBeenApplied()
        {
            Assert.True(XeroRetryPolicy.ShouldRetry(HttpStatusCode.InternalServerError, HttpMethod.Get, 1));
            Assert.False(XeroRetryPolicy.ShouldRetry(HttpStatusCode.InternalServerError, HttpMethod.Post, 1));
            Assert.False(XeroRetryPolicy.ShouldRetry(HttpStatusCode.InternalServerError, HttpMethod.Put, 1));
            Assert.True(XeroRetryPolicy.ShouldRetry(HttpStatusCode.ServiceUnavailable, HttpMethod.Post, 1));
            Assert.False(XeroRetryPolicy.ShouldRetry(HttpStatusCode.ServiceUnavailable, HttpMethod.Post, XeroRetryPolicy.MaxAttempts));
            Assert.False(XeroRetryPolicy.ShouldRetry(HttpStatusCode.BadRequest, HttpMethod.Get, 1));
            Assert.False(XeroRetryPolicy.ShouldRetry(new HttpRequestException("dropped"), HttpMethod.Post, 1));
            Assert.True(XeroRetryPolicy.ShouldRetry(new HttpRequestException("dropped"), HttpMethod.Get, 1));
        }

        // ---- queries

        [Fact]
        public void DocumentListsAlwaysFilterToSalesAndQuoteSearchTextSafely()
        {
            var where = XeroQuery.Where(InvoiceService.SalesType, new DocumentQuery
            {
                Statuses = new[] { XeroStatus.Authorised, XeroStatus.Paid },
                Search = "O\"Brien",
                ContactId = Guid.Parse("025867f1-d741-4d6b-b1af-9ac774b59ba7"),
                DateFrom = new DateTime(2026, 11, 1)
            }, "InvoiceNumber");

            Assert.StartsWith("Type==\"ACCREC\"&&(Status==\"AUTHORISED\"||Status==\"PAID\")", where);
            Assert.Contains("Contact.ContactID==guid(\"025867f1-d741-4d6b-b1af-9ac774b59ba7\")", where);
            Assert.Contains("Date>=DateTime(2026, 11, 01)", where);
            // The quote in the search text is doubled, so it cannot end the string and add a clause of its own.
            Assert.Contains("InvoiceNumber.ToLower().Contains(\"o\"\"brien\")", where);
            Assert.Contains("Reference!=null&&", where);
        }

        [Fact]
        public void TheQueryStringIsEscapedAndPageSizeKeptWithinXerosLimit()
        {
            var qs = XeroQuery.DocumentQueryString(CreditNoteService.CustomerCreditType, new DocumentQuery { Page = 0, PageSize = 5000 }, "CreditNoteNumber");
            Assert.StartsWith("?where=Type%3D%3D%22ACCRECCREDIT%22", qs);
            Assert.Contains("&order=Date%20DESC", qs);
            Assert.EndsWith("&page=1&pageSize=1000", qs);
        }

        // ---- OAuth

        private static string Jwt(object payload)
        {
            static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return $"{B64("{\"alg\":\"RS256\"}")}.{B64(JsonSerializer.Serialize(payload))}.signature";
        }

        [Fact]
        public void TheConsentAddressCarriesTheAppTheScopesAndTheState()
        {
            var client = new XeroOAuthClient(new HttpClient(), new XeroOAuthSettings
            {
                ClientId = "ABC123", ClientSecret = "s", RedirectUri = "http://localhost:50910/redirect-url"
            });
            var url = client.BuildConsentUrl("state-1");

            Assert.StartsWith("https://login.xero.com/identity/connect/authorize?response_type=code&client_id=ABC123", url);
            Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A50910%2Fredirect-url", url);
            Assert.Contains("offline_access", url);
            Assert.Contains("accounting.invoices", url);
            Assert.DoesNotContain("accounting.transactions", url); // refused for apps created after 2 March 2026
            Assert.EndsWith("&state=state-1", url);
        }

        [Fact]
        public void TheOrganisationJustConnectedIsTheOneStampedWithThisConsent()
        {
            var thisConsent = Guid.NewGuid();
            var earlier = new XeroConnection { TenantId = Guid.NewGuid(), TenantType = "ORGANISATION", TenantName = "Old Co", AuthEventId = Guid.NewGuid(), UpdatedDateUtc = DateTime.UtcNow };
            var picked = new XeroConnection { TenantId = Guid.NewGuid(), TenantType = "ORGANISATION", TenantName = "Demo Company (Global)", AuthEventId = thisConsent, UpdatedDateUtc = DateTime.UtcNow.AddDays(-1) };
            var practice = new XeroConnection { TenantId = Guid.NewGuid(), TenantType = "PRACTICE", AuthEventId = thisConsent };
            var token = Jwt(new { authentication_event_id = thisConsent.ToString(), xero_userid = "u" });

            var chosen = XeroTokenClaims.ConnectionsFromConsent(new[] { earlier, picked, practice }, token);

            Assert.Equal("Demo Company (Global)", Assert.Single(chosen).TenantName);
        }

        [Fact]
        public void WithoutAnEventIdTheMostRecentOrganisationIsUsed()
        {
            var older = new XeroConnection { TenantId = Guid.NewGuid(), TenantType = "ORGANISATION", TenantName = "Older", UpdatedDateUtc = DateTime.UtcNow.AddDays(-3) };
            var newer = new XeroConnection { TenantId = Guid.NewGuid(), TenantType = "ORGANISATION", TenantName = "Newer", UpdatedDateUtc = DateTime.UtcNow };

            Assert.Equal("Newer", Assert.Single(XeroTokenClaims.ConnectionsFromConsent(new[] { older, newer }, "not-a-jwt")).TenantName);
            Assert.Empty(XeroTokenClaims.ConnectionsFromConsent(Array.Empty<XeroConnection>(), null));
        }

        [Fact]
        public void ADeadRefreshTokenIsRecognisedSoItIsNotRetried()
        {
            Assert.True(new XeroTokenResponse { Error = "invalid_grant", Status = HttpStatusCode.BadRequest }.IsConnectionLost);
            Assert.True(new XeroTokenResponse { Error = "invalid_client", Status = HttpStatusCode.Unauthorized }.IsConnectionLost);
            Assert.False(new XeroTokenResponse { Error = "unreachable", Status = HttpStatusCode.ServiceUnavailable }.IsConnectionLost);
            Assert.False(new XeroTokenResponse { AccessToken = "a" }.IsConnectionLost);
            Assert.True(new XeroTokenResponse { AccessToken = "a" }.IsSuccess);
        }
    }
}

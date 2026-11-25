// Developer tool: connects to Xero with the real app registration and checks what the API allows, using the same
// client library as the API. Never part of a release.
//
//   dotnet run --project tools/XeroProbe -- connect     sign in to Xero and pick an organisation (opens the browser)
//   dotnet run --project tools/XeroProbe -- survey      read-only: organisation, tax rates, documents, a contact
//   dotnet run --project tools/XeroProbe -- writeback   Demo Company only: tries each way of writing a fiscal result back
//
// The client secret is read from the XERO_CLIENT_SECRET environment variable, else from the API project's
// user-secrets (key Xero:ClientSecret). Tokens are kept in ~/.fiscalize-xero-probe/tokens.json, outside the repo.

using FiscalizeXero.Xero;
using FiscalizeXero.Xero.Exceptions;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.OAuth;
using FiscalizeXero.Xero.Services;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

const string ApiUserSecretsId = "7abcd5e2-8715-48ab-b0b3-c4400d195703";
const string DefaultClientId = "D21A1CD5B61F49E0A58BAB7FA3671E9B";
const string RedirectUri = "http://localhost:50910/redirect-url";

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
var tokenFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fiscalize-xero-probe", "tokens.json");

var secrets = ReadUserSecrets(ApiUserSecretsId);
var settings = new XeroOAuthSettings
{
    ClientId = Environment.GetEnvironmentVariable("XERO_CLIENT_ID") ?? secrets.GetValueOrDefault("Xero:ClientId") ?? DefaultClientId,
    ClientSecret = Environment.GetEnvironmentVariable("XERO_CLIENT_SECRET") ?? secrets.GetValueOrDefault("Xero:ClientSecret") ?? string.Empty,
    RedirectUri = RedirectUri
};
if (Environment.GetEnvironmentVariable("XERO_SCOPE") is { Length: > 0 } scope) settings.Scope = scope;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var oauth = new XeroOAuthClient(http, settings);

try
{
    switch (command)
    {
        case "connect": return await ConnectAsync();
        case "survey": return await SurveyAsync();
        case "writeback": return await WritebackAsync();
        default:
            Console.WriteLine("Usage: XeroProbe connect | survey | writeback");
            return command == "help" ? 0 : 2;
    }
}
catch (XeroApiException ex)
{
    Console.WriteLine($"Xero refused the call: HTTP {(int)ex.StatusCode} {ex.Message} (correlation id {ex.CorrelationId ?? "none"})");
    return 1;
}

// ---------------------------------------------------------------- connect

async Task<int> ConnectAsync()
{
    if (!settings.IsConfigured)
    {
        Console.WriteLine("No client secret. Set it with:");
        Console.WriteLine("  cd FiscalizeXero.Api && dotnet user-secrets set Xero:ClientSecret \"<secret>\"");
        return 2;
    }

    var state = Guid.NewGuid().ToString("N");
    using var listener = new HttpListener();
    listener.Prefixes.Add("http://localhost:50910/");
    try { listener.Start(); }
    catch (HttpListenerException ex)
    {
        Console.WriteLine($"Port 50910 is busy ({ex.Message}). Stop the web app's dev server and try again.");
        return 2;
    }

    var url = oauth.BuildConsentUrl(state);
    Console.WriteLine("Opening Xero in your browser. Sign in, choose Demo Company, and allow access.");
    Console.WriteLine(url);
    try { Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false }); } catch { /* the address is printed above */ }

    string? code = null, error = null;
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    while (code == null && error == null)
    {
        HttpListenerContext context;
        try { context = await listener.GetContextAsync().WaitAsync(timeout.Token); }
        catch (OperationCanceledException) { Console.WriteLine("Gave up after 5 minutes with no answer from Xero."); return 1; }

        var query = context.Request.QueryString;
        var isCallback = context.Request.Url?.AbsolutePath == "/redirect-url";
        string page;
        if (!isCallback) page = "Not the page Xero sends you to.";
        else if (query["error"] is { } e) { error = $"{e}: {query["error_description"]}"; page = "Xero did not grant access: " + WebUtility.HtmlEncode(error); }
        else if (query["state"] != state) { error = "state mismatch"; page = "This answer did not come from the sign-in this tool started."; }
        else { code = query["code"]; page = "Connected. You can close this tab and go back to the terminal."; }

        var bytes = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=utf-8><title>Fiscalize Xero probe</title><p style=\"font:16px system-ui;margin:3rem\">{page}</p>");
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }
    listener.Stop();

    if (error != null || string.IsNullOrEmpty(code)) { Console.WriteLine($"Not connected: {error ?? "no code returned"}"); return 1; }

    var tokens = await oauth.ExchangeCodeAsync(code);
    if (!tokens.IsSuccess) { Console.WriteLine($"Token exchange failed: {tokens.Error} {tokens.ErrorDescription}"); return 1; }

    var connections = await ConnectionService.ListAsync(tokens.AccessToken);
    var chosen = XeroTokenClaims.ConnectionsFromConsent(connections, tokens.AccessToken);
    Console.WriteLine($"Granted scopes: {tokens.Scope}");
    Console.WriteLine($"Organisations connected to this app under your sign-in: {connections.Count}");
    foreach (var c in connections)
        Console.WriteLine($"  {(chosen.Contains(c) ? "*" : " ")} {c.TenantName} ({c.TenantType}) tenant {c.TenantId}");
    if (chosen.Count != 1) { Console.WriteLine("Expected exactly one organisation from this consent; not saving."); return 1; }

    SaveTokens(new SavedTokens(tokens.AccessToken, tokens.RefreshToken ?? "", DateTime.UtcNow.AddSeconds(tokens.ExpiresIn),
        chosen[0].TenantId, chosen[0].Id, chosen[0].TenantName ?? ""));
    Console.WriteLine($"Saved. Using: {chosen[0].TenantName}");
    return 0;
}

// ---------------------------------------------------------------- survey (read-only)

async Task<int> SurveyAsync()
{
    var auth = await AuthAsync();
    if (auth == null) return 2;

    var org = await OrganisationService.GetAsync(auth);
    Console.WriteLine($"ORGANISATION  {org?.Name} | demo={org?.IsDemoCompany} | country={org?.CountryCode} | currency={org?.BaseCurrency} | " +
                      $"taxNumber={Show(org?.TaxNumber)} | registration={Show(org?.RegistrationNumber)} | defaultSalesTax={org?.DefaultSalesTax} | shortCode={org?.ShortCode}");

    var rates = await TaxRateService.ListAsync(auth);
    Console.WriteLine($"\nTAX RATES ({rates.Count})");
    foreach (var r in rates.OrderBy(r => r.Status).ThenBy(r => r.Name))
        Console.WriteLine($"  {r.TaxType,-14} {r.EffectiveRate,7}%  {r.Status,-8} {r.Name}  [{string.Join(" + ", (r.TaxComponents ?? new()).Select(c => $"{c.Name} {c.Rate}%"))}]");

    Console.WriteLine("\nSALES INVOICES by status");
    foreach (var status in new[] { XeroStatus.Draft, XeroStatus.Submitted, XeroStatus.Authorised, XeroStatus.Paid, XeroStatus.Voided })
    {
        var page = await InvoiceService.ListAsync(auth, new DocumentQuery { Statuses = new[] { status }, PageSize = 5 });
        Console.WriteLine($"  {status,-11} {page.ItemCount,4}  e.g. {string.Join(", ", page.Items.Take(3).Select(i => i.InvoiceNumber))}");
    }

    var sample = (await InvoiceService.ListAsync(auth, new DocumentQuery { Statuses = new[] { XeroStatus.Authorised, XeroStatus.Paid }, PageSize = 1 })).Items.FirstOrDefault();
    if (sample != null)
    {
        var inv = await InvoiceService.GetByIdAsync(auth, sample.InvoiceID) ?? sample;
        Console.WriteLine($"\nSAMPLE INVOICE {inv.InvoiceNumber} | {inv.Status} | date={inv.Date} | due={inv.DueDateString} | {inv.CurrencyCode} rate={inv.CurrencyRate} | " +
                          $"amounts={inv.LineAmountTypes} | sub={inv.SubTotal} tax={inv.TotalTax} total={inv.Total} due={inv.AmountDue} | ref={Show(inv.Reference)} | updated={inv.UpdatedDateUTC:u}");
        foreach (var l in inv.LineItems)
            Console.WriteLine($"    item={Show(l.ItemCode)} qty={l.Quantity} unit={l.UnitAmount} disc%={l.DiscountRate} discAmt={l.DiscountAmount} line={l.LineAmount} taxType={l.TaxType} tax={l.TaxAmount} | {Trim(l.Description)}");
        var gross = inv.LineItems.Sum(l => (l.LineAmount ?? 0m) + (inv.IsTaxInclusive ? 0m : l.TaxAmount ?? 0m));
        Console.WriteLine($"    lines + tax = {gross} (document total {inv.Total}) {(gross == inv.Total ? "match" : "DIFFERENT")}");

        if (inv.Contact != null)
        {
            var c = await ContactService.GetByIdAsync(auth, inv.Contact.ContactID);
            Console.WriteLine($"\nCONTACT {c?.Name} | taxNumber={Show(c?.TaxNumber)} | companyNumber={Show(c?.CompanyNumber)} | account={Show(c?.AccountNumber)} | email={Show(c?.EmailAddress)}");
            foreach (var p in c?.Phones ?? new()) Console.WriteLine($"    phone {p.PhoneType}: {Show(p.Full)}");
            foreach (var a in c?.Addresses ?? new()) Console.WriteLine($"    address {a.AddressType}: {Show(string.Join(" | ", new[] { a.AddressLine1, a.AddressLine2, a.AddressLine3, a.AddressLine4, a.City, a.Region, a.PostalCode, a.Country }.Where(s => !string.IsNullOrWhiteSpace(s))))}");
        }
    }

    var credits = await CreditNoteService.ListAsync(auth, new DocumentQuery { PageSize = 50 });
    Console.WriteLine($"\nCUSTOMER CREDIT NOTES ({credits.ItemCount})");
    foreach (var cn in credits.Items.Take(15))
        Console.WriteLine($"  {cn.CreditNoteNumber,-10} {cn.Status,-10} total={cn.Total} remaining={cn.RemainingCredit} amounts={cn.LineAmountTypes} " +
                          $"allocations=[{string.Join(", ", (cn.Allocations ?? new()).Select(a => $"{a.Invoice?.InvoiceNumber} {a.Amount}"))}]");

    var items = await ItemService.ListAsync(auth);
    Console.WriteLine($"\nITEMS ({items.Count})  e.g. " + string.Join("; ", items.Take(4).Select(i => $"{i.Code}={i.Name} price={i.SalesDetails?.UnitPrice} tax={i.SalesDetails?.TaxType}")));
    return 0;
}

// ---------------------------------------------------------------- writeback (Demo Company only)

async Task<int> WritebackAsync()
{
    var auth = await AuthAsync();
    if (auth == null) return 2;

    var org = await OrganisationService.GetAsync(auth);
    if (org?.IsDemoCompany != true)
    {
        Console.WriteLine($"Refusing: '{org?.Name}' is not the Xero Demo Company, and this test writes to documents.");
        return 2;
    }
    Console.WriteLine($"Testing against {org.Name}. Notes and attachments added here cannot be removed, but the Demo Company resets itself.\n");

    var pdf = TinyPdf("Fiscalize write-back test");
    var stamp = "FISC-TEST-" + DateTime.UtcNow.ToString("HHmmss");

    var authorised = (await InvoiceService.ListAsync(auth, new DocumentQuery { Statuses = new[] { XeroStatus.Authorised }, PageSize = 20 })).Items;
    var unpaid = authorised.FirstOrDefault(i => (i.AmountPaid ?? 0m) == 0m && (i.AmountCredited ?? 0m) == 0m);
    var partPaid = authorised.FirstOrDefault(i => (i.AmountPaid ?? 0m) > 0m);
    var paid = (await InvoiceService.ListAsync(auth, new DocumentQuery { Statuses = new[] { XeroStatus.Paid }, PageSize = 1 })).Items.FirstOrDefault();
    var credits = (await CreditNoteService.ListAsync(auth, new DocumentQuery { Statuses = new[] { XeroStatus.Authorised, XeroStatus.Paid }, PageSize = 50 })).Items;
    var allocated = credits.FirstOrDefault(c => c.Allocations is { Count: > 0 });
    var unallocated = credits.FirstOrDefault(c => c.Allocations is not { Count: > 0 });

    await TestDocumentAsync("Invoice awaiting payment, nothing paid", unpaid);
    await TestDocumentAsync("Invoice awaiting payment, part paid", partPaid);
    await TestDocumentAsync("Invoice paid in full", paid);
    await TestDocumentAsync("Credit note, allocated to an invoice", allocated);
    await TestDocumentAsync("Credit note, not allocated", unallocated);

    var contactId = (unpaid ?? paid)?.Contact?.ContactID;
    if (contactId.HasValue)
    {
        var before = await ContactService.GetByIdAsync(auth, contactId.Value);
        Console.WriteLine($"CONTACT {before?.Name} (taxNumber={Show(before?.TaxNumber)}, companyNumber={Show(before?.CompanyNumber)})");
        await TryAsync("write TaxNumber + CompanyNumber", async () =>
        {
            var after = await ContactService.UpdateTaxDetailsAsync(auth, contactId.Value, "220001234", "2000123456");
            return $"read back taxNumber={Show(after?.TaxNumber)} companyNumber={Show(after?.CompanyNumber)}";
        });
        await TryAsync("restore original values", async () =>
        {
            var after = await ContactService.UpdateTaxDetailsAsync(auth, contactId.Value, before?.TaxNumber ?? "", before?.CompanyNumber ?? "");
            return $"read back taxNumber={Show(after?.TaxNumber)} companyNumber={Show(after?.CompanyNumber)}";
        });
    }
    return 0;

    async Task TestDocumentAsync(string label, SalesDocument? doc)
    {
        if (doc == null) { Console.WriteLine($"{label}: none in the Demo Company, not tested\n"); return; }
        var endpoint = DocumentExtrasService.For(doc);
        Console.WriteLine($"{label}: {doc.DocumentNumber} ({doc.Status})");

        await TryAsync("history note", async () =>
        {
            await DocumentExtrasService.AddNoteAsync(auth, endpoint, doc.DocumentId, $"Fiscalize test note {stamp}");
            var history = await DocumentExtrasService.GetHistoryAsync(auth, endpoint, doc.DocumentId);
            return history.Any(h => h.Details?.Contains(stamp) == true) ? "added and read back" : "accepted but not found when read back";
        });
        await TryAsync("PDF attachment shown online", async () =>
        {
            var a = await DocumentExtrasService.UploadAttachmentAsync(auth, endpoint, doc.DocumentId, $"fiscal-test-{stamp}.pdf", pdf, "application/pdf", includeOnline: true);
            var listed = (await DocumentExtrasService.GetAttachmentsAsync(auth, endpoint, doc.DocumentId)).FirstOrDefault(x => x.AttachmentID == a?.AttachmentID);
            return $"uploaded; includeOnline reported as {listed?.IncludeOnline?.ToString() ?? "not stated"}";
        });
        await TryAsync("set Reference", async () =>
        {
            var original = doc.Reference ?? "";
            var wanted = (original + " " + stamp).Trim();
            var after = doc is CreditNote
                ? (await CreditNoteService.UpdateReferenceAsync(auth, doc.DocumentId, wanted))?.Reference
                : (await InvoiceService.UpdateReferenceAsync(auth, doc.DocumentId, wanted))?.Reference;
            if (doc is CreditNote) await CreditNoteService.UpdateReferenceAsync(auth, doc.DocumentId, original);
            else await InvoiceService.UpdateReferenceAsync(auth, doc.DocumentId, original);
            return after == wanted ? "changed, then put back" : $"accepted but read back as '{after}'";
        });
        await TryAsync("download Xero's own PDF", async () =>
        {
            var bytes = doc is CreditNote ? await CreditNoteService.GetPdfAsync(auth, doc.DocumentId) : await InvoiceService.GetPdfAsync(auth, doc.DocumentId);
            return $"{bytes.Length:N0} bytes, {(bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' ? "a PDF" : "NOT a PDF")}";
        });
        Console.WriteLine();
    }

    async Task TryAsync(string what, Func<Task<string>> action)
    {
        try { Console.WriteLine($"    OK      {what}: {await action()}"); }
        catch (XeroApiException ex) { Console.WriteLine($"    REFUSED {what}: HTTP {(int)ex.StatusCode} {ex.Message}"); }
    }
}

// ---------------------------------------------------------------- helpers

async Task<XeroAuth?> AuthAsync()
{
    var saved = LoadTokens();
    if (saved == null) { Console.WriteLine("Not connected yet. Run: dotnet run --project tools/XeroProbe -- connect"); return null; }

    if (saved.ExpiresAtUtc - DateTime.UtcNow < TimeSpan.FromMinutes(2))
    {
        if (!settings.IsConfigured) { Console.WriteLine("The access token has expired and there is no client secret to refresh it."); return null; }
        var refreshed = await oauth.RefreshAsync(saved.RefreshToken);
        if (!refreshed.IsSuccess) { Console.WriteLine($"Could not refresh the connection ({refreshed.Error}). Run 'connect' again."); return null; }
        saved = saved with { AccessToken = refreshed.AccessToken, RefreshToken = refreshed.RefreshToken ?? saved.RefreshToken, ExpiresAtUtc = DateTime.UtcNow.AddSeconds(refreshed.ExpiresIn) };
        SaveTokens(saved);
    }
    return new XeroAuth { Token = saved.AccessToken, TenantId = saved.TenantId };
}

SavedTokens? LoadTokens() =>
    File.Exists(tokenFile) ? JsonSerializer.Deserialize<SavedTokens>(File.ReadAllText(tokenFile)) : null;

void SaveTokens(SavedTokens tokens)
{
    Directory.CreateDirectory(Path.GetDirectoryName(tokenFile)!);
    File.WriteAllText(tokenFile, JsonSerializer.Serialize(tokens));
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tokenFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
}

static Dictionary<string, string> ReadUserSecrets(string id)
{
    var root = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets");
    var file = Path.Combine(root, id, "secrets.json");
    if (!File.Exists(file)) return new();
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        return doc.RootElement.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.OrdinalIgnoreCase);
    }
    catch (JsonException) { return new(); }
}

static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "(empty)" : value;
static string Trim(string? value) => value == null ? "" : value.ReplaceLineEndings(" ").Length > 60 ? value.ReplaceLineEndings(" ")[..60] + "..." : value.ReplaceLineEndings(" ");

// A one-page PDF with a line of text, built by hand so the tool needs no PDF library.
static byte[] TinyPdf(string text)
{
    var stream = $"BT /F1 18 Tf 72 720 Td ({text}) Tj ET";
    var objects = new[]
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
        $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
    };
    var sb = new StringBuilder("%PDF-1.4\n");
    var offsets = new List<int>();
    for (var i = 0; i < objects.Length; i++)
    {
        offsets.Add(sb.Length);
        sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
    }
    var xref = sb.Length;
    sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
    foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
    sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    return Encoding.ASCII.GetBytes(sb.ToString());
}

record SavedTokens(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc, Guid TenantId, Guid ConnectionId, string TenantName);

# FiscalizeXero

ZIMRA fiscalization service for Xero. Customers connect their Xero organisation over OAuth, and the service
fiscalises their sales invoices and customer credit notes through the Fiscalize platform (fiscalize.onaerp.africa),
then records the fiscal result back on the Xero document.

## Status: backend ported, never run against Xero

`dotnet build FiscalizeXero.slnx` passes with 0 errors and `dotnet test FiscalizeXero.Api.Tests` passes.
The API starts, applies its migration to an empty SQL Server and answers `/health`.

**Nothing has yet been run against a real Xero organisation.** There has been no client secret to test with, so every
call to Xero is built to Xero's documented behaviour only. In particular these are assumptions until
`docs/xero-connection-checklist.md` has been run once:

- the OAuth flow, the granular scopes and picking the organisation from the connection list;
- that history notes and attachments are accepted on paid invoices and allocated credit notes;
- what Xero answers when the Reference of a locked document is updated;
- the webhook "intent to receive" exchange against the live endpoint;
- the `where` filters, paging and `If-Modified-Since` used by the lists and the backstop sync.

The web app (`FiscalizeXero.Angular/`) still talks to the previous product's API and is a separate task. The
Fiscalize platform does not yet know a `Xero` provider (see *Platform pairing*).

## What is implemented

### Connection (`ClientConfigsController`, `XeroAuthService`)

- One Xero app registration, one credential pair (`Xero:ClientId` / `Xero:ClientSecret`). No sandbox/production
  split and no bring-your-own credentials.
- `POST api/ClientConfigs/register` returns the Xero consent URL; Xero redirects to `Xero:RedirectUri` with `code`
  and `state`; the web app passes both to `GET api/ClientConfigs/oauth/callback`.
- Xero's redirect does not name the organisation. The callback exchanges the code, lists the user's connections and
  picks the organisation stamped with this consent (`XeroTenantSelection`). Nothing is stored before that
  organisation is known not to be live under another Fiscalize company; if it is, the answer is 409.
- Tokens: the access token lives 30 minutes, the refresh token 60 days and is **rotated on every refresh**, so the
  new one is stored each time. Refresh pattern: 300-second proactive buffer, per-connection lock, double-checked read
  after the lock, 30-second cooldown after a failure, a forced refresh when Xero answers 401.
- A refresh answered `invalid_grant` clears the stored tokens but keeps the organisation, so the company list says
  "reconnect" instead of "never connected".
- **Disconnect never calls Xero's revocation endpoint.** Revoking a refresh token removes every organisation that
  Xero user connected, which could cut off another Fiscalize company. One organisation is disconnected by deleting
  its connection (`ConnectionService.DisconnectAsync`, id stored as `ClientConfig.XeroConnectionId`).

### Xero client (`FiscalizeXero.Xero`)

`RestClient` (bearer token + `xero-tenant-id`, retry with backoff, one refresh-and-retry on 401, waits out the
minute rate limit, surfaces the daily limit as `XeroApiException.IsDailyLimit`), models and services for invoices,
credit notes, history notes, attachments, contacts, items, tax rates, the organisation and connections, plus
`XeroOAuthClient`.

Xero allows an organisation 60 calls a minute and as few as 1,000 a day. The API is economical on purpose: tax rates
are cached per organisation for 5 minutes, the Items and Customers pages reuse their list for a minute, a change to
an already fiscalised document costs no Xero call, and fiscal records and Fiscalize PDFs are served without asking Xero.

### Fiscalization (`FiscalizationService`)

- Only approved sales documents: an invoice of type `ACCREC` or a credit note of type `ACCRECCREDIT` with status
  `AUTHORISED` or `PAID` (`DocumentGate`). Drafts, documents awaiting approval, voided and deleted documents and all
  purchase documents are refused with a message saying what to do in Xero. Automatic triggers record them as
  **Skipped**, not Failed.
- `XeroFiscalizeMapper` maps lines by item code at their final, tax-inclusive price and checks them against the
  document total. Tax per line comes from the organisation's tax rates (`XeroTaxResolver`); when the rates cannot be
  read the rate is derived from each line's own tax and amount.
- **Credit notes.** Xero has no "original invoice" field. The original is the document number the user chose, else
  taken from the credit note's allocations: the only invoice, or the one with the largest total allocated amount
  (`CreditNoteOriginalInvoice`). An unallocated credit note fails with a message telling the user to allocate it in
  Xero or choose the invoice in Fiscalize; Xero's UPDATE webhook for the allocation then retries it. The original must
  be an invoice fiscalised here, and the credit note number may be at most 20 characters.
- **Buyer details** (`BuyerResolver`, `BuyerCompliance`): values saved in Fiscalize (`ContactVatRecord`) win over
  Xero. From the Xero contact: Tax Number and Company Number, classified by format (exactly 10 digits is a TIN,
  exactly 9 a VAT number, whichever box); phone DEFAULT, else MOBILE, else DDI; `EmailAddress`; the POBOX address when
  it has content, else STREET, parsed by `BuyerAddressParser`. No TIN: cash sale. `FiscalizationValidationMode`
  Strict (default) stops and lists what is missing; Lenient fills placeholders. An invalid TIN/VAT always blocks.
- Saving on the Customers page (and correcting a buyer on an invoice) can copy the TIN to the contact's Tax Number
  and the VAT number to its Company Number. A box holding anything that is not a TIN or VAT number is left alone.
  Everything Xero does not hold stays in Fiscalize.
- **HS codes**: saved per Xero item code (`ItemHsCodeRecord.ItemCode`), else the company's default, else the system
  default `04021099`. Lines without an item code take the default.
- Kept from the previous product: one lock per document so a webhook, a sync and a click collapse into one ZIMRA
  submission; failure tracking (`FiscalFailure`); refiscalise, corrected buyer details, fiscal cancel; the Fiscalize
  platform's own PDF; device API key auto-provisioning.

### Write-back to Xero (all best effort)

Xero has no custom fields and no private note, and it locks paid invoices and allocated credit notes against most
edits. So every write-back is optional, non-fatal, and reported as `writebackWarning` when Xero refuses.

| Setting (Fiscalization settings) | Default | What it does |
|---|---|---|
| `writebackHistoryNote` | on | Adds a note to the document's "History and notes": receipt, global number, device, fiscal day, verification code, link |
| `fiscalStampEnabled` | on | Stamps Xero's own PDF and attaches it as `<number>-fiscal.pdf` (same name each time, so Xero replaces it) |
| `attachmentIncludeOnline` | on | Shows that attachment on the customer's online invoice |
| `writebackReference` | off | Appends `[ZIMRA <receipt>/<verification code>]` to the Reference, keeping the customer's text, within 255 characters |

A fiscal cancel adds a history note and takes the marker back out of the Reference. Xero's API cannot delete an
attachment, so the stamped PDF stays.

### Fiscal PDF and Fiscalize layouts (`FiscalDocumentService`, `Services/Layouts`)

The company chooses the PDF attached in Xero (Fiscalization settings, "Which PDF goes to Xero"; `ClientConfig.FiscalPdfLayout`):

| Choice | What is attached |
|---|---|
| `XeroStamp` (default) | Xero's own PDF (the organisation's branding theme) with the ZIMRA stamp drawn on it |
| `FiscalizeLayout` | The company's layout for **Xero** designed on the Fiscalize platform (Plugin layouts), drawn here with the shared `Fiscalize.LayoutRenderer` on A4, 80mm or 58mm; Fiscalize's standard design with the company letterhead when none is live |
| `FiscalizeStandard` | The Fiscalize platform's own standard invoice PDF for the receipt |

* A layout can never stop a fiscal PDF: when the chosen one cannot be made, the stamped Xero PDF is attached, the document page says
  why, and the fallback is counted for Connected apps.
* All three are downloadable from the invoice and credit note pages (`GET api/Fiscalization/{invoices|creditnotes}/{id}/layout-pdf`
  for the layout).
* `PlatformLayoutService` keeps the last good package per company, document type and paper in `LayoutPackageCaches`, asks the platform
  again with `If-None-Match` once it is 10 minutes old (`Fiscalize:Layouts:RefreshMinutes`), never replaces a good package with a damaged
  one, and uploads the field list (built-in keys plus the organisation's tracking categories as `Doc.<category>`) once a day.
* `XeroLayoutMapper` turns the Xero document, contact, saved buyer details and fiscal record into the renderer's document. Its keys are
  the platform's `Xero` keys in `PluginBuiltInFields` and are never renamed.
* The renderer comes from `libs/Fiscalize.LayoutRenderer.<version>.nupkg` (see `nuget.config`). Its PdfSharpCore asks for ImageSharp
  1.0.4 (known vulnerabilities); the API pins ImageSharp 3.2.1 instead, which is safe because the renderer never decodes an image.

### Connected apps (`Services/ConnectedApps`)

Every Xero connection reports itself to the platform's Connected apps page as a **cloud installation** (no computer): the organisation
(name, id, short code, plan, currency), a connection state (connected / needs attention / disconnected, with a sentence), the settings
that matter to support, the last sync and webhook, documents waiting for a fix, the layouts in use and the fiscal PDFs attached by kind.
It reports every 30 minutes (`Fiscalize:ConnectedApps:IntervalMinutes`, off with `Fiscalize:ConnectedApps:Enabled=false`), a few seconds
after settings are saved, and once more as "disconnected" when Xero is disconnected. Signed with the company's device key; failures are
logged and retried next round. The organisation is read from Xero at most once a day.

### Automatic fiscalization: webhooks first, a sync as the backstop

- `POST api/xero/webhooks` (anonymous). The `x-xero-signature` header is the base64 HMAC-SHA256 of the **raw body
  bytes** keyed with `Xero:WebhookKey`. A correct signature is answered 200 with an empty body at once; a wrong,
  missing or unverifiable one (including when no key is configured) is always 401. Each INVOICE / CREDITNOTE change
  is stored as a `WebhookEvent` and processed by `WebhookProcessingService`.
- `XeroSyncService`: every `Xero:Sync:IntervalHours` (6, plus a fixed per-company jitter) and immediately after a
  (re)connection, lists approved invoices and credit notes modified since the last successful run minus an overlap
  (60 minutes back for a connection never synced) and feeds them through the same queue with source `Sync`.
- The Webhooks page endpoints (`GET api/xero/webhooks/status`) give the URL to register, setup state and the log.

### Platform pairing

After a connection the callback pairs the organisation with the company on the Fiscalize platform
(`api/ExternalIdentities`, provider `Xero`, external identifier the tenant id). A 409 undoes the connection. The
platform does not know the `Xero` provider yet, so any other failure is logged only and never shown to the user.
Sign-in is by email and password against the platform, or a launch token; there is no "Sign in with Xero".

### Database

One migration, `Initial`, applied at startup. Tables: Identity, `AppUsers`, `AppUserCompanies`, `Configs`
(`TenantId`, `TenantName`, `XeroConnectionId`, tokens, settings), `DeviceConfigs`, `FiscalRecords` (unique on
company + `DocumentId`), `ContactVatRecords`, `ItemHsCodeRecords`, `WebhookEvents`, `FiscalFailures`. Xero ids are
stored as lower-case GUID strings.

## Configuration

Local development: API on `http://localhost:5091`, web app on `http://localhost:50910`. The connection string and
the Xero credentials come from `dotnet user-secrets` (run inside `FiscalizeXero.Api`):

```
dotnet user-secrets set ConnectionStrings:DefaultConnection "..."
dotnet user-secrets set Xero:ClientId "..."
dotnet user-secrets set Xero:ClientSecret "..."
```

`appsettings.json` (placeholders only; real values live on the server in `appsettings.Production.json`, see
`appsettings.Production.template.json`):

```json
"Xero": {
  "ClientId": "",
  "ClientSecret": "",
  "RedirectUri": "https://xero.fiscalize.co.zw/redirect-url",
  "Scope": "openid profile email offline_access accounting.invoices accounting.contacts accounting.settings.read accounting.attachments",
  "ScopeVersion": 1,
  "WebhookKey": "",
  "WebhookCallbackBaseUrl": "https://apps.onaerp.africa",
  "Sync": { "Enabled": true, "IntervalHours": 6, "JitterMinutes": 45, "TickSeconds": 300,
            "InitialLookbackMinutes": 60, "OverlapMinutes": 10, "DelayBetweenCompaniesSeconds": 2, "MaxPagesPerRun": 10 }
}
```

In Production the API refuses to start when `AuthSettings:Key` (at least 32 bytes), `ConnectionStrings:DefaultConnection`,
`Xero:ClientId`, `Xero:ClientSecret` or `Xero:RedirectUri` is missing or still a placeholder, or when
`Cors:AllowedOrigins` only lists localhost. A missing `Xero:WebhookKey` is a startup warning and `Degraded` on `/health`.
Raise `ScopeVersion` whenever a scope is added: connections granted under a lower version are asked to re-authorise.

### Xero app registration (developer.xero.com → My Apps → New app)

| Portal field | Value |
|---|---|
| App name | Fiscalize for Xero |
| Integration type | Web app (authorisation code flow) |
| Company or application URL | https://xero.fiscalize.co.zw |
| Redirect URI (production) | https://xero.fiscalize.co.zw/redirect-url |
| Redirect URI (local) | http://localhost:50910/redirect-url |
| Scopes requested | openid, profile, email, offline_access, accounting.invoices, accounting.contacts, accounting.settings.read, accounting.attachments |
| Webhook delivery URL | https://apps.onaerp.africa/FiscalizeXero/api/xero/webhooks |
| Webhook event categories | Invoices, Credit notes |

Apps created after 2 March 2026 only get the granular scopes above. After saving, the client id and secret go into
`Xero:ClientId` / `Xero:ClientSecret` on the server and the webhook key into `Xero:WebhookKey`
(`docs/xero-webhook-key.md`). Never commit either.

## Invariants

- **Fiscalising authenticates as the ZIMRA device** through `DeviceSessionService`, never with a user's token. A
  company whose device has no API key cannot fiscalise.
- **Every Fiscalize platform call made for a signed-in user goes through `FiscalizeSessionService.RunAsync`** (renew
  and retry). The anonymous OAuth callback has no request user, so the pairing call uses the company's own stored
  session with the same renew-once-and-retry.
- **One Xero connection per Fiscalize company.** Everyone signed in to a company uses the same connection, devices,
  settings and fiscal history. Company data lives under one **anchor** `AppUserCompany` row: resolve it through
  `ICurrentUserService.GetCurrentClientConfigAsync()` / `GetCompanyAnchorIdAsync()`, never `user.ActiveCompany.Id`.
- **One Xero organisation, one Fiscalize company.** The callback refuses an organisation live under another company.
  Should one exist anyway, webhooks and the sync skip it (`TenantGuards.ContestedTenants`) and `/health` reports
  `xeroOrganisationsSharedBetweenCompanies` above 0 (`Degraded`, still HTTP 200).
- **Membership comes only from the platform and is re-checked.** Every controller marked `[CompanyScoped]` (all
  `api/xero/…`, connection, fiscalisation and device endpoints) passes `CompanyMembershipFilter`: the platform is
  asked at most every 10 minutes; not listed is 403 at once; unreachable is tolerated for 12 hours. New company-data
  controllers must carry `[CompanyScoped]`.
- **Secrets never reach the browser and are encrypted at rest.** Xero tokens, Fiscalize session tokens and device API
  secrets are AES-256-GCM encrypted by `AesGcmSecretProtector` (EF value converters in `DataContext`), keyed from
  `AuthSettings:Key`; tokens, the connection's owner row and the OAuth `state` are `[JsonIgnore]`. Rotating
  `AuthSettings:Key` makes stored tokens unreadable: companies must reconnect.
- **Only approved sales are ever sent to ZIMRA**, and purchase documents never leave Xero's lists.
- **User-facing messages say "Xero"**, name the document and say what to do next.

`GET /health` (anonymous) reports `status`, `webhookKeyConfigured`, `companiesWithDuplicateConnections`,
`xeroOrganisationsSharedBetweenCompanies` and one line per check; never a configuration value.

## Before going live

1. Register the Xero app (table above) and put the credentials on the server.
2. Run `docs/xero-connection-checklist.md` against a Xero demo company and fix what it finds.
3. Fiscalize platform: add the `Xero` provider to `api/ExternalIdentities`, and confirm the app type used by
   "Open Fiscalize" (`AppLauncherService`, currently the previous product's `1`).
4. Register the webhook and set the key (`docs/xero-webhook-key.md`).
5. Port the web app to the endpoints under `api/xero/…`.
6. Every company needs a ZIMRA device with an API key (Device Setup).

## Release build

`./publish.sh` (or `api` / `web`) bumps the patch version, publishes, renames the shipped `appsettings*.json` so they
cannot overwrite the server's, and zips into `releases/`.

## File map

```
FiscalizeXero.Api/
  Controllers/
    ClientConfigs/     connect, re-authorise, disconnect, fiscalisation settings
    Fiscalization/     fiscalise, refiscalise, fiscal-info, fiscal-cancel, PDFs, attachment, failures; devices and keys
    Xero/              invoices, creditnotes, customers, items, taxrates, organisation, webhooks (+ XeroBaseController)
    Auths/ Users/ HsCodes/
  Services/
    XeroAuth/          XeroAuthService, XeroAuthBuilder, XeroTenantSelection, XeroConnections, XeroTaxRateCache, XeroOptions
    Fiscalization/     FiscalizationService, XeroFiscalizeMapper, DocumentGate, CreditNoteOriginalInvoice, Buyer*,
                       XeroTaxIdentifiers, XeroContactWriteback, FiscalHistoryNote, FiscalReferenceMarker
    FiscalDocuments/   stamped PDF, attachment queue, XeroDocumentTypes
    Sync/              XeroSyncService, SyncPlanner, XeroSyncOptions
    Webhooks/          signature + payload (XeroWebhook.cs), queue, WebhookProcessingService
    HsCodes/ Documents/ Health/ CompanyConnections/ Membership/ FiscalizeSession/ DeviceSession/ PdfStamp/
  Data/                EF entities, DataContext, TenantGuards
  Migrations/          Initial
FiscalizeXero.Xero/        Xero REST client, models, services, OAuth
FiscalizeXero.Fiscalize/   Fiscalize platform client
FiscalizeXero.Shared/      DTOs
FiscalizeXero.Api.Tests/   xunit
tools/XeroProbe/           manual probe against Xero
```

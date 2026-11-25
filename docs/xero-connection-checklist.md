# Connection checklist (connect → use → disconnect → reconnect)

Run this against a Xero **demo company** (or a trial organisation) before every release that touches OAuth, tokens,
webhooks or the connection pages. Until it has been run once from start to finish, nothing in this product has been
proven against the real Xero API. Tick each line and keep the filled-in copy with the release notes.

Prerequisites

- A server (local or staging) with `Xero:ClientId`, `Xero:ClientSecret` and a `Xero:RedirectUri` that matches a
  redirect URI on the Xero app exactly.
- A Fiscalize account with one company and a ZIMRA device that has an API key (Device Setup).
- A Xero login with access to a demo company.

## 1. Connect

1. Sign in, start the Xero connection, choose the demo company on Xero's consent screen and allow access.
2. Expected: the callback completes and names the organisation; `GET api/ClientConfigs/current` shows `tenantId`,
   `tenantName` and a `tokenExpiresAt` about 30 minutes ahead.
3. Expected in the log: `Xero connection stored for ClientConfig …`, then `Xero sync (catch-up) …`.
4. Expected in the database: the `Configs` row has `TenantId`, `XeroConnectionId`, and `enc1:`-prefixed
   `AccessToken` and `RefreshToken`.

## 2. Use the connection

1. Invoices and credit notes list (approved ones only); purchase bills never appear.
2. Fiscalise one approved invoice. Check in Xero: the history note, the `…-fiscal.pdf` attachment (and that it shows
   on the online invoice when that setting is on), and the Reference marker when that setting is on.
3. Repeat step 2 on a **paid** invoice. Record what Xero accepted: note, attachment, Reference. Each refusal must show
   as a warning, never as a failed fiscalisation.
4. Create and approve a credit note **without** allocating it: it must show as failed with "has not been allocated".
   Allocate it to the fiscalised invoice in Xero: within a minute it must fiscalise by itself.
5. Force a token refresh: set `TokenExpiresAt` to now, reload the invoice list. Expected: the list loads, the log
   shows `Xero token refresh succeeded`, and `RefreshToken` changed (Xero rotates it on every refresh).
6. A draft invoice fiscalised by hand is refused with "still a draft in Xero".

## 3. Disconnect

1. Disconnect in the app. Expected: 200 with no warnings, the `Configs` row is gone, and in Xero
   (Settings → Connected apps) Fiscalize is no longer listed **for that organisation**.
2. If the same Xero user has a second organisation connected to another Fiscalize company, that one must still work.

## 4. Reconnect and isolation

1. Reconnect the same organisation: settings and fiscal history are back, no invoice is fiscalised twice.
2. Sign in to a **different** Fiscalize company and try to connect the same organisation. Expected: 409 "This Xero
   organisation is already connected to another Fiscalize company", and the first company's connection still works.
3. Remove Fiscalize from inside Xero (Connected apps). Expected within one refresh: the company list says
   "Xero has disconnected Fiscalize… Reconnect", and nothing keeps calling Xero.

# FiscalizeXero.Angular

Angular 20 web app for **Fiscalize ZIMRA for Xero**: connect a Xero organisation, fiscalise its approved sales invoices
and customer credit notes with ZIMRA, and see the result. It talks only to the `FiscalizeXero.Api` in this repository.

## Stack

- Angular 20.3 standalone components, signals, functional routing and HTTP interceptors
- Tailwind CSS 4.1, `@angular/cdk` for overlays
- PWA service worker in production builds
- No SSR

## Run

```bash
npm ci
npm start          # ng serve --port 50910 (the API is on 5091)
npx ng build       # production build into dist/FiscalizeXeroAngular/browser
```

`http://localhost:50910/redirect-url` is the redirect URI registered on the Xero app for local work, so keep the port.

## Configuration

`src/environments/environment*.ts`:

- dev: `http://localhost:5091/api/`
- prod: `https://apps.onaerp.africa/FiscalizeXero/api/`

`public/.htaccess` carries the SPA rewrite and the Content Security Policy; its `connect-src` must list the API host.

## What's where

| Area | Path |
|---|---|
| Routes | `src/app/app.routes.ts` |
| Layout and sidebar | `src/app/layouts/dashboard-layout/`, `src/app/components/nav-bar/` |
| Auth, guards, interceptor | `src/app/core/{services/auth,guards,interceptors}/` |
| API services | `src/app/core/services/xero/*.service.ts` |
| Xero shapes as the API returns them | `src/app/core/models/xero.models.ts` |
| Xero wording (statuses, dates, amounts) | `src/app/core/utils/xero-format.ts` |
| Connect flow | `src/app/pages/register-client/`, `src/app/pages/oauth-callback/` (`/redirect-url`), `src/app/pages/xero-connection/` |
| Documents | `src/app/pages/{invoices,invoice-detail,credit-notes,credit-note-detail}/` |
| Buyer details and HS codes | `src/app/pages/{customers,customer-detail,items}/` |
| Settings | `src/app/pages/{fiscalization-settings,webhooks,device-setup}/` |

## Conventions

- JSON from the API is camelCase with Xero's own field names (`invoiceID`, `contactID`, `updatedDateUTC`). All Xero ids are GUID strings.
- Xero statuses are shown in Xero's words: AUTHORISED is "Awaiting payment", PAID is "Paid". Only those two can be fiscalised.
- HTTP 424 means Xero, the ZIMRA device or the Fiscalize platform refused; pages show the message and offer the Xero
  connection page. It never signs the user out. 401 with `fiscalizeSessionExpired` raises the "sign in again" prompt.
- Document dates (`dateString`) are calendar dates; timestamps from the API are UTC without a zone (`asUtc`).

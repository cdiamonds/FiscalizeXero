# Deploying Fiscalize for Xero

Two packages, built by `./publish.sh` into `releases/`:

| Package | Goes to | Serves |
|---|---|---|
| `FiscalizeXero-v<ver>.zip` | IIS, the `FiscalizeXero` application under `apps.onaerp.africa` | the API at `https://apps.onaerp.africa/FiscalizeXero/api/` |
| `FiscalizeXeroAngular-v<ver>.zip` | cPanel, document root of `xero.fiscalize.co.zw` | the web app |

The API package never overwrites the server's settings: its `appsettings*.json` files are shipped renamed with a
leading `_`. So a **first** deploy has to create them (step 2). Later deploys skip that step.

## First deploy

### 1. Database
Create an empty SQL Server database (for example `FiscalizeXero`) and a login that owns it. Tables are created
automatically the first time the API starts.

### 2. API settings (on the IIS server, in the application folder)
1. Rename `_appsettings.json` to `appsettings.json` (first deploy only).
2. Copy `appsettings.Production.template.json` from the repository to `appsettings.Production.json` next to it,
   and fill in:
   - `ConnectionStrings:DefaultConnection`: the database from step 1.
   - `AuthSettings:Key`: generate once with `openssl rand -base64 48`. **Never change it afterwards.** Changing
     it signs everyone out and makes stored Xero and device credentials unreadable, so every company must reconnect.
   - `Xero:ClientId` / `Xero:ClientSecret`: from developer.xero.com > My Apps > Fiscalize ZIMRA.
   - `Xero:WebhookKey`: leave empty for now (step 6).
   - `ReverseProxy:TrustedNetworks`: only if Cloudflare (or another proxy) sits in front of IIS. List its IP ranges.
   - Keep `PathBase` = `/FiscalizeXero` and `Cors:AllowedOrigins` = `https://xero.fiscalize.co.zw`.

The API refuses to start, with a message saying exactly what is wrong, if a required value is missing or still a
placeholder, if the signing key is short, or if CORS only allows localhost.

### 3. API binaries
1. Install the **.NET 10 Hosting Bundle** on the server if it isn't there, then restart IIS.
2. Application pool: *No Managed Code*, integrated pipeline, one worker process.
3. Unzip `FiscalizeXero-v<ver>.zip` into the application folder.
4. Open `https://apps.onaerp.africa/FiscalizeXero/health`. Expect `"database": "Healthy"`. Until step 6 the
   overall status is "Degraded" because of the missing webhook key; that is expected.

### 4. Web app
Upload `FiscalizeXeroAngular-v<ver>.zip` to the document root of `xero.fiscalize.co.zw` and extract it there.
`index.html` and `.htaccess` must be at the root. Open `https://xero.fiscalize.co.zw` and sign in.

### 5. Xero app redirect
In developer.xero.com > My Apps > Fiscalize ZIMRA > Configuration, the redirect URI
`https://xero.fiscalize.co.zw/redirect-url` must be listed (it already is as of 23 Nov 2026).

### 6. Webhooks (instant fiscalisation)
1. developer.xero.com > the app > Webhooks. Delivery URL: `https://apps.onaerp.africa/FiscalizeXero/api/xero/webhooks`.
   Tick **Invoices** and **Credit notes**, then save.
2. Copy the **Webhooks key** Xero shows into `Xero:WebhookKey` in `appsettings.Production.json`. Recycle the
   application pool.
3. Back in the portal, click **Send "Intent to receive"**. The status should turn to OK.
4. The app's Webhooks page shows "Listener: Ready" and lists deliveries as they arrive.

Without webhooks everything still fiscalises: the safety-net check finds approved documents every six hours.

### 7. Smoke test
Sign in, connect a Xero organisation (the Demo Company first), set up the ZIMRA device, approve an invoice in Xero, and
watch it appear as fiscalised on the dashboard within a minute (with webhooks) or after the next safety-net check.

## Later deploys
1. `./publish.sh` (or `api` / `web`). It bumps the version numbers; commit those files.
2. API: recycle the pool, unzip over the folder (the `_appsettings*.json` files don't touch the real ones), and check
   `/health`. Any new database changes apply themselves on start.
3. Web app: extract the new zip over the old files.

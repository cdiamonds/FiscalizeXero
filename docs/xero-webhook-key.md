# Setting the Xero webhook key on the server

Until `Xero:WebhookKey` is set, the API answers **every** delivery to `POST …/api/xero/webhooks` with **401**, so
Xero's "intent to receive" check fails and Xero will not enable the webhook. The backstop sync and the Fiscalize
buttons keep fiscalising, which makes a missing key easy to miss. `GET /health` shows it:
`"webhookKeyConfigured": false` (status `Degraded`, still HTTP 200).

Nothing in this procedure has been run against Xero yet; see the README.

## 1. Where the key comes from

developer.xero.com → My Apps → the Fiscalize app → **Webhooks**:

1. Tick the event categories **Invoices** and **Credit notes**.
2. Delivery URL: the address shown on the app's Webhooks page, i.e. `<Xero:WebhookCallbackBaseUrl><PathBase>/api/xero/webhooks`
   (production: `https://apps.onaerp.africa/FiscalizeXero/api/xero/webhooks`).
3. Save. Xero shows the **webhook key**. Treat it as a secret: never paste it into chat, email, a ticket or a file
   in this repository.

## 2. Put the key on the server

Either of these; do not do both.

- **Environment variable on the IIS site**: name `Xero__WebhookKey` (two underscores), value = the key. Recycle the
  application pool.
- **`appsettings.Production.json` next to the binaries** (git-ignored, never published): add `"WebhookKey": "<the key>"`
  inside the existing `"Xero": { … }` object. Restart the site.

## 3. Let Xero verify

Back in the portal press **Send 'Intent to receive'**. Xero posts a run of test deliveries, some signed correctly
and some not, and expects 200 with an empty body for the good ones and 401 for the bad ones, each within 5 seconds.
The status turns to OK when all are answered correctly.

## 4. Check

- `GET /health` → `"webhookKeyConfigured": true`.
- Startup log no longer contains `Webhook key not configured`.
- Approve an invoice in Xero: within a minute the app's Webhooks page lists it with source **Webhook**.

A quick check from a shell that the endpoint refuses a bad signature (it never needs the real key):

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST -H "x-xero-signature: AAAA" \
  --data '{"events":[]}' https://apps.onaerp.africa/FiscalizeXero/api/xero/webhooks   # expect 401
```

## If the key is rotated

Saving the webhook again in the portal can issue a new key. Deliveries are then answered 401 until the server has
the new one; Xero retries for a while and then disables the webhook, and the backstop sync covers the gap.

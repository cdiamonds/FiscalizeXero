# Claude in Chrome: deployment and test tasks for Fiscalize for Xero

Paste **one task at a time** into Claude in Chrome, in this order. Paste the "Rules for every task" block above each
task. Tasks 3 and 4 only work once the API is live on IIS (see `docs/deploy-checklist.md`, steps 1 to 3), which is
done on the server, not in the browser.

| Task | When | Needs from you |
|---|---|---|
| 1. Check the Xero app settings | now | Xero login |
| 2. Upload the web app to cPanel | now | cPanel login, picking the zip file |
| 3. Turn on Xero webhooks | after the API is live | Xero login; you paste the key into the server settings |
| 4. End-to-end test on the Demo Company | after tasks 1 to 3 | Fiscalize and Xero logins |

---

## Rules for every task (paste these first)

```
You are helping deploy "Fiscalize for Xero", a service that fiscalises Xero sales invoices with ZIMRA (Zimbabwe).
Follow these rules strictly:

1. Logins: when a page asks for a password, a 2FA code or a CAPTCHA, stop and ask me to type it. Never guess or reuse credentials.
2. Secrets: never type, repeat or summarise in the chat any client secret, webhook key, password or token. If a secret is
   shown on screen, tell me it is there and where to copy it from; do not read it out.
3. Real books: the Xero organisation "Onadiamonds Investments Pvt Ltd" is a REAL business. Never create, edit, approve,
   void or delete anything in it. All test work happens only in "Demo Company (Global)".
4. In the Xero developer portal, never click "Generate a secret", "Regenerate", "Delete app", or change the app name,
   scopes or company. Regenerating the client secret breaks the live service.
5. Do only what the task says. If something looks different from what is described, or a step would change anything not
   listed, stop and ask me before continuing.
6. If you cannot use a file picker or a download/upload dialog, stop and ask me to choose the file.
7. At the end, give me a short report: what you did, what you checked, anything that looked wrong, and anything left for me.
```

---

## Task 1: check the Xero app settings (read-only)

```
Task: confirm the Xero app is configured correctly. Change nothing.

1. Open https://developer.xero.com/app/manage and sign in (ask me for the login).
2. Open the app named "Fiscalize ZIMRA". If there is no app with that name, stop and tell me which apps you see.
3. On its Configuration page, check and report:
   - App type is "Web app".
   - Client id starts with D21A1CD5. Report only the first 8 characters.
   - The redirect URIs include exactly these two (scheme, host and path must match):
       https://xero.fiscalize.co.zw/redirect-url
       http://localhost:50910/redirect-url
   - Whether a client secret exists. Do NOT generate or reveal one.
4. Open the Webhooks section and report its current state (no webhook / URL shown / status text). Do not create one yet.
5. Report the plan or tier shown for the app (for example Starter) and the number of connections in use, if shown.
```

---

## Task 2: upload the web app to cPanel

Before pasting: tell Claude your cPanel address (for example `https://<server>:2083`) and have
`releases/FiscalizeXeroAngular-v0.1.33.zip` from the project folder ready to pick.

```
Task: publish the Fiscalize for Xero web app on the domain xero.fiscalize.co.zw using cPanel's File Manager.

1. Open the cPanel address I give you and sign in (ask me for the login).
2. Find the document root of xero.fiscalize.co.zw: cPanel > Domains (or Subdomains) > the row for xero.fiscalize.co.zw >
   its Document Root. Tell me the folder before changing anything.
3. In File Manager, open that folder. If it already has files, list them and ask me whether to replace them. Do not
   delete anything without my yes.
4. Upload FiscalizeXeroAngular-v0.1.33.zip into that folder (ask me to pick the file if needed).
5. Extract the zip into the same folder (right-click > Extract). index.html and .htaccess must end up directly in the
   document root, not in a subfolder. Turn on "Show Hidden Files" in File Manager settings to see .htaccess.
6. Delete the uploaded zip file only (not the extracted files).
7. Open https://xero.fiscalize.co.zw in a new tab. Report what you see. It should be the Fiscalize for Xero landing page.
   Then open https://xero.fiscalize.co.zw/login and confirm the sign-in page loads (do not sign in).
8. Also open https://xero.fiscalize.co.zw/dashboard directly. It should show the app (likely the sign-in page), not a
   404 error. A 404 here means .htaccess is missing or not in the root.
```

---

## Task 3: turn on Xero webhooks (after the API is live)

Before pasting: confirm `https://apps.onaerp.africa/FiscalizeXero/health` shows `"database":"Healthy"`.

```
Task: set up the Xero webhook so approved invoices are fiscalised within about a minute.

1. Open https://apps.onaerp.africa/FiscalizeXero/health and report the JSON. "database" must be "Healthy". If the page
   does not load or the database is not healthy, stop and tell me.
2. Open https://developer.xero.com/app/manage, sign in (ask me), open the app "Fiscalize ZIMRA", then Webhooks.
3. Set the delivery URL to exactly:
       https://apps.onaerp.africa/FiscalizeXero/api/xero/webhooks
4. Tick the events "Invoices" and "Credit notes" only. Save.
5. Xero now shows a "Webhooks key". Do NOT read it out or paste it anywhere. Tell me: "The webhooks key is ready to copy
   on the Webhooks page." Then wait. I will copy it into the server settings (Xero:WebhookKey) and restart the API.
6. When I say the server is updated, click "Send 'Intent to receive'" in the portal. Wait up to a minute, refresh, and
   report the status. It should read OK. If it says intent to receive failed, report the exact message and stop.
7. Open https://apps.onaerp.africa/FiscalizeXero/health again. "webhookKeyConfigured" should now be true. Report it.
```

---

## Task 4: end-to-end test on the Demo Company (after tasks 1 to 3)

Before pasting: make sure your Fiscalize account has a **test company** that is NOT the one connected to
Onadiamonds (one Xero organisation belongs to one Fiscalize company). Tell Claude that company's name.

```
Task: prove that Fiscalize for Xero fiscalises a Demo Company invoice automatically, and that the main screens work.
Only ever use Xero's "Demo Company (Global)". Never touch "Onadiamonds Investments Pvt Ltd".

A. Sign in and connect
1. Open https://xero.fiscalize.co.zw/login and sign in with my Fiscalize account (ask me to type it).
2. If asked to choose a company, choose the TEST company I named. If the active company is the one connected to
   "Onadiamonds Investments Pvt Ltd", switch company with the company switcher at the top. If you cannot, stop and ask.
3. Go to Xero Connection. If it already shows "Onadiamonds Investments Pvt Ltd", STOP: you are in the wrong company.
4. Click Connect to Xero. On Xero's consent screen choose "Demo Company (Global)" (ask me to log in to Xero if asked) and
   allow access. You should land back on the dashboard with the Demo Company name at the top.

B. Device and settings (look only, unless told)
5. Open Device Setup and report whether a fiscal device with an API key is shown. If none, stop and tell me (I will set
   up a ZIMRA test device).
6. Open Fiscalization settings and report whether "Auto-fiscalize invoices" and "Auto-fiscalize credit notes" are on.
   Do not change them.

C. Automatic fiscalisation
7. In a new tab open Xero, make sure the organisation picker shows "Demo Company (Global)", then create a sales invoice:
   any existing contact, one line (an existing item, quantity 1, any price), and click Approve.
   Note its number (for example INV-0123).
8. Back in Fiscalize for Xero, open Webhooks. Within about two minutes the invoice should appear under Recent deliveries
   with status "Processed" and "Fiscalised, receipt <number>". Refresh every 30 seconds for up to 5 minutes. Report what
   you see. If it shows Failed, open the invoice in Invoices and report the message.
9. Open Invoices, find the invoice, open it, and report: the Fiscal Data block (receipt number, verification code), the
   QR code, and "PDF in Xero" status.
10. In Xero, open the same invoice: report whether "History and notes" has a fiscal note, and whether the attachment
    "<number>-fiscal.pdf" is there.

D. Screens
11. Open the Dashboard: report the figures in the four tiles and whether the new invoice is listed under Latest receipts.
12. Open Customers and Items and report whether lists load. Do not save anything.

E. Report
13. Give me a table: each step, expected result, what actually happened, pass/fail. Include any error text exactly.

Do not run fiscal cancel, refiscalise or disconnect unless I ask in a separate message.
```

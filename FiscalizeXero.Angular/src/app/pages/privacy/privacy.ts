import { Component } from '@angular/core';
import { LegalShell } from '../../components/legal-shell/legal-shell';

/**
 * Privacy Policy for Fiscalize for Xero. Covers the
 * Xero-specific data flows and defers to the Fiscalize platform Privacy Policy for everything shared.
 */
@Component({
  selector: 'app-privacy',
  imports: [LegalShell],
  template: `
    <app-legal-shell kicker="Privacy Policy" title="Privacy Policy for Fiscalize for Xero" updated="24 November 2026">
      <p>
        This Privacy Policy explains what <strong>Fiscalize for Xero</strong> (the "App") collects from your
        Xero organisation and from you, why, where it goes and how long it is kept. The App is part of the Fiscalize
        platform, so the
        <a href="https://fiscalize.co.zw/privacy-policy" target="_blank" rel="noopener noreferrer">Fiscalize Privacy Policy</a>
        also applies to you. This document adds the Xero-specific detail; where the two differ, this document governs
        data handled by the App.
      </p>
      <p>
        Fiscalize is the data controller for your Fiscalize account data and acts as a data processor, on your instructions,
        for the business and customer data it reads from Xero and submits to ZIMRA. We process personal data in
        accordance with the Cyber and Data Protection Act [Chapter 12:07] of Zimbabwe.
      </p>

      <h2>1. Data we receive from Xero and Xero</h2>
      <p>When you connect a Xero organisation, and for as long as it stays connected, the App accesses:</p>
      <table>
        <thead><tr><th>Data</th><th>Why we need it</th></tr></thead>
        <tbody>
          <tr><td><strong>Your Xero identity</strong>: name and email address, shared by Xero's sign-in through the <code>openid</code>, <code>profile</code> and <code>email</code> scopes</td><td>Xero includes these with the authorisation. The App does not store them or use them for anything.</td></tr>
          <tr><td><strong>Organisation information</strong>: name, legal name, organisation ID, base currency, country, tax number and tax rates</td><td>To identify the connected organisation, show it in the App and apply the correct tax rate and category to each line.</td></tr>
          <tr><td><strong>Sales documents</strong>: approved sales invoices and customer credit notes, including line items, quantities, prices, discounts, tax rates and amounts, the Reference, credit allocations, history notes and attachments the App creates</td><td>To fiscalise them with ZIMRA and to write the fiscal result back. Purchase bills share the same Xero permission; the App does not fiscalise or store them.</td></tr>
          <tr><td><strong>Contacts</strong>: name, tax number and company number (read as TIN and VAT number), addresses, email and phone numbers</td><td>ZIMRA requires buyer details on a fiscal document. A TIN or VAT number you add or correct in the App is written to the contact's tax number or company number in Xero; other buyer details you type in the App are stored by us and not written to Xero.</td></tr>
          <tr><td><strong>Items</strong>: item codes, names, descriptions and sales prices</td><td>To send the correct HS code for each line. HS codes you choose in the App are stored per item code by us; Xero has no field for them.</td></tr>
          <tr><td><strong>OAuth tokens</strong>: access and refresh tokens issued by Xero</td><td>To call the Xero API on your behalf, including when nobody is signed in to the App.</td></tr>
        </tbody>
      </table>
      <p>
        The App requests the Xero scopes <code>accounting.invoices</code>, <code>accounting.contacts</code>, <code>accounting.settings.read</code>, <code>accounting.attachments</code> and <code>offline_access</code>, and the three OpenID scopes above.
        It does not access payroll, bank transactions, payments, manual journals or reports.
      </p>

      <h2>2. Data you give the App directly</h2>
      <ul>
        <li>Your Fiscalize account details (name, email, password) when you sign in, or a sign-in token when you launch the App from the Fiscalize platform.</li>
        <li>Fiscal device API credentials for the ZIMRA device you assign to the company.</li>
        <li>Settings: which document types to fiscalise automatically, the default HS code, fiscal stamp placement and how fiscal results are written back to Xero.</li>
        <li>Buyer details you enter or correct in the App (registered and trading name, TIN, VAT number, phone, email and address) and HS codes.</li>
      </ul>

      <h2>3. Data the App creates</h2>
      <ul>
        <li><strong>Fiscal records</strong>: for each fiscalised Xero document, the ZIMRA receipt number, verification code, QR code, fiscal day, device and timestamps, and a copy of the data submitted.</li>
        <li><strong>Written back to Xero</strong>: the fiscal result as a history note on the document, optionally a stamped PDF attached to the document (which your customer can see on the online invoice if you leave that option on), optionally a short marker in the document's Reference, and a TIN or VAT number you enter in the App on the Xero contact. Anyone with access to your Xero organisation can see these.</li>
        <li><strong>Operational logs</strong>: webhook deliveries from Xero, periodic checks for missed documents, errors, IP addresses and timestamps, kept for security and troubleshooting.</li>
      </ul>

      <h2>4. How we use the data</h2>
      <ul>
        <li>To fiscalise your sales documents with ZIMRA and record the result in Xero and the Fiscalize platform.</li>
        <li>To operate, secure and support the App, including detecting duplicate submissions and abuse.</li>
        <li>To meet our legal obligations, including record-keeping required by Zimbabwean tax law.</li>
        <li>To tell you about connection problems, failed fiscalisations and material changes to the App.</li>
      </ul>
      <p>
        We do not sell data obtained from Xero, use it for advertising, or use it to train artificial
        intelligence models. We use it only to provide and improve the App's fiscalisation service.
      </p>

      <h2>5. Who we share it with</h2>
      <table>
        <thead><tr><th>Recipient</th><th>What and why</th></tr></thead>
        <tbody>
          <tr><td><strong>Zimbabwe Revenue Authority (ZIMRA)</strong></td><td>Invoice, credit note and buyer data, submitted for fiscalisation as the law requires. This disclosure is mandatory.</td></tr>
          <tr><td><strong>Fiscalize platform</strong></td><td>The same data, so the fiscal document is created on your ZIMRA device and appears in your Fiscalize company. Governed by the Fiscalize Privacy Policy.</td></tr>
          <tr><td><strong>Xero Limited</strong></td><td>Fiscal results written back to your Xero organisation, and the API calls needed to read your data. Governed by Xero's privacy statement.</td></tr>
          <tr><td><strong>Hosting and email providers</strong></td><td>Infrastructure providers who store or transmit data for us under contract, and who may not use it for their own purposes.</td></tr>
          <tr><td><strong>Authorities</strong></td><td>Where the law requires or a court or regulator lawfully demands it.</td></tr>
        </tbody>
      </table>
      <p>Fiscal documents may be visible to the customer named on them through ZIMRA's public verification service, as the law intends.</p>

      <h2>6. Where the data is and how it is protected</h2>
      <ul>
        <li>Data is stored on servers operated for Fiscalize. Data in transit is protected with TLS 1.2 or higher. Xero tokens and device credentials are encrypted at rest (AES-256-GCM) in the database, are held server-side only and never sent to your browser, and refresh tokens are rotated and replaced on every refresh. Sign-in endpoints are rate limited.</li>
        <li>Access to production systems is restricted to Fiscalize engineers who need it, using key-based authentication. Access and changes are logged.</li>
        <li>Each Fiscalize account only sees its own companies; a Xero connection is only visible to the Fiscalize users who belong to that company.</li>
        <li>If a breach affects your data we will notify you and, where required, the Postal and Telecommunications Regulatory Authority of Zimbabwe, without undue delay and in any event within 72 hours of becoming aware.</li>
      </ul>

      <h2>7. How long we keep it</h2>
      <table>
        <thead><tr><th>Data</th><th>Retention</th></tr></thead>
        <tbody>
          <tr><td>Xero access and refresh tokens, connection record</td><td>Until you disconnect, the token is revoked, or the connection is replaced. Deleted immediately on disconnect.</td></tr>
          <tr><td>Fiscal records and the submitted document data</td><td>For the period Zimbabwean tax law requires fiscal records to be retained, currently six years from the end of the tax year, even after disconnection or account closure.</td></tr>
          <tr><td>Buyer details and item HS codes entered in the App</td><td>While the company exists on your Fiscalize account; deleted with the company.</td></tr>
          <tr><td>Webhook and operational logs</td><td>Up to 12 months.</td></tr>
          <tr><td>Documents read from Xero that were never fiscalised</td><td>Not stored; read on demand and discarded.</td></tr>
        </tbody>
      </table>

      <h2>8. Disconnecting and deleting</h2>
      <ul>
        <li>Disconnect from the <strong>Xero connection</strong> page in the App, or from <strong>Settings &gt; Connected apps</strong> in Xero. Disconnecting in the App removes the App's connection to that organisation at Xero and deletes the connection record; if you disconnect inside Xero instead, the App finds out the next time it calls Xero and asks you to reconnect. The App stops reading your Xero data at once.</li>
        <li>To delete cached customer and item data, remove the company from your Fiscalize account or write to us.</li>
        <li>Fiscal records cannot be deleted while the law requires them to be kept; they are deleted when that period ends.</li>
      </ul>

      <h2>9. Your rights</h2>
      <p>
        Subject to the law, you may ask for access to the personal data we hold about you, for correction of inaccurate
        data, for deletion where retention is not required, for restriction of processing, and for a copy in a portable
        format. Where you are a customer of a business that uses the App, please contact that business first; we will
        assist them. You may lodge a complaint with the Postal and Telecommunications Regulatory Authority of Zimbabwe.
      </p>

      <h2>10. Children</h2>
      <p>The App is a business tool and is not directed at children under 18. We do not knowingly collect their data.</p>

      <h2>11. Changes to this policy</h2>
      <p>
        We may update this policy. The "Last updated" date above will change and material changes will be announced in the
        App or by email before they take effect.
      </p>

      <h2>12. Contact</h2>
      <p>
        Data protection enquiries: <a href="mailto:legal@fiscalize.co.zw">legal@fiscalize.co.zw</a> ·
        Fiscalize · <a href="https://fiscalize.co.zw" target="_blank" rel="noopener noreferrer">fiscalize.co.zw</a>
      </p>
    </app-legal-shell>
  `
})
export class Privacy {}

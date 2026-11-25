import { Component } from '@angular/core';
import { LegalShell } from '../../components/legal-shell/legal-shell';

/**
 * End User License Agreement / Terms of Service for Fiscalize for Xero. Published as the
 * app's EULA URL. Supplements, and defers to, the Fiscalize platform Terms and Conditions.
 */
@Component({
  selector: 'app-terms',
  imports: [LegalShell],
  template: `
    <app-legal-shell kicker="End User License Agreement" title="Terms of Service for Fiscalize for Xero">
      <p>
        These Terms of Service ("Terms") govern your use of <strong>Fiscalize for Xero</strong> (the "App"), the
        Xero integration operated by Fiscalize ("Fiscalize", "we", "us"). The App is part of the Fiscalize
        platform and is provided to holders of a Fiscalize account. The
        <a href="https://fiscalize.co.zw/terms-and-conditions" target="_blank" rel="noopener noreferrer">Fiscalize Terms and Conditions</a>
        apply to your use of the App in full; these Terms add the points that are specific to the Xero integration.
        Where the two conflict, these Terms prevail for the App only.
      </p>
      <p>
        By connecting a Xero organisation to the App, or by using the App in any way, you agree to these Terms
        on your own behalf and on behalf of the business whose Xero organisation you connect. If you do not agree, do
        not connect a company and do not use the App.
      </p>

      <h2>1. What the App does</h2>
      <p>The App connects a Xero organisation to the Fiscalize platform in order to:</p>
      <ul>
        <li>read approved sales invoices and customer credit notes, together with the contacts, items, tax rates and organisation information they refer to;</li>
        <li>submit those documents to the Zimbabwe Revenue Authority ("ZIMRA") Fiscal Device Management System through a ZIMRA-registered fiscal device managed on the Fiscalize platform ("fiscalisation");</li>
        <li>write the resulting fiscal information (receipt number, verification code, QR code and device details) back onto the Xero document as a history note and, where you enable it, as an attached stamped PDF or a marker in the document's Reference; and</li>
        <li>show the fiscal status of your Xero documents in the App and let authorised users fiscalise, re-fiscalise, correct buyer details or cancel a fiscal document.</li>
      </ul>
      <p>
        Fiscalisation may run automatically, on a schedule or in response to change notifications from Xero, for the
        document types you have switched on under Fiscalization settings. You can switch each type off at any time.
      </p>

      <h2>2. Eligibility and accounts</h2>
      <ul>
        <li>You need an active Fiscalize account and a Fiscalize company with at least one ZIMRA-registered fiscal device. The App does not create fiscal devices.</li>
        <li>You must have the authority to connect the Xero organisation you select and to bind its owner to these Terms.</li>
        <li>You are responsible for everyone who uses the App under your Fiscalize account, and for keeping your Fiscalize and Xero credentials confidential.</li>
        <li>You must be at least 18 years old.</li>
      </ul>

      <h2>3. Your Xero connection</h2>
      <ul>
        <li>The App uses Xero's OAuth 2.0 to connect. You authorise the App on Xero's own consent screen and choose the organisation there; the App never sees your Xero password.</li>
        <li>You may disconnect at any time, either from the Xero connection page in the App or from <strong>Settings &gt; Connected apps</strong> inside Xero. Disconnecting revokes the App's access token and removes the connection from the App. It does not delete fiscal records already created (see section 6).</li>
        <li>Xero may require you to re-authorise the connection from time to time, for example when the connection has not been used for 60 days or the App requests an updated set of permissions.</li>
        <li>Your use of Xero remains subject to Xero's own terms. Fiscalize is not affiliated with, endorsed by or responsible for Xero Limited.</li>
      </ul>

      <h2>4. Your responsibilities</h2>
      <p>You agree that you are solely responsible for:</p>
      <ul>
        <li>the accuracy and completeness of the data in your Xero organisation, including customer tax identifiers, item HS codes, tax rates and prices, and for setting a correct default HS code for your company;</li>
        <li>reviewing fiscal documents produced by the App and raising any discrepancy promptly;</li>
        <li>complying with the Value Added Tax Act, ZIMRA's fiscalisation rules and all other laws that apply to your business, including the Cyber and Data Protection Act [Chapter 12:07] in respect of personal data about your customers that you process through the App; and</li>
        <li>not using the App to create false, misleading or fraudulent fiscal documents, to circumvent fiscalisation, or to access a Xero organisation you are not authorised to use.</li>
      </ul>

      <h2>5. Fiscalisation and ZIMRA</h2>
      <ul>
        <li>A fiscal document is created when ZIMRA accepts a submission. Once ZIMRA has accepted a document it cannot be deleted; it can only be reversed by a credit note under ZIMRA's rules. The App reflects this: a fiscalised invoice is corrected by a credit note, not by editing the original.</li>
        <li>Availability and response times of ZIMRA's systems are outside our control. The App queues and retries submissions where it safely can, and shows a clear status when a submission has failed.</li>
        <li>Fiscal records the App stores are tax records. We keep them for as long as Zimbabwean tax law requires, even after you disconnect or close your account.</li>
      </ul>

      <h2>6. Data</h2>
      <p>
        How the App collects, uses, stores and shares data, including data obtained from Xero, is described in
        the <a href="/privacy">Fiscalize for Xero Privacy Policy</a>. You retain ownership of your data. You grant us
        the licence needed to operate the App: to store and process your Xero data, to transmit it to ZIMRA and the
        Fiscalize platform, and to write fiscal results back into Xero.
      </p>

      <h2>7. Fees</h2>
      <p>
        The App is included in your Fiscalize subscription. Fiscalize subscription fees, credits and refunds are governed
        by the Fiscalize Terms and Conditions. Xero charges separately for its own subscription.
      </p>

      <h2>8. Acceptable use</h2>
      <p>You must not:</p>
      <ul>
        <li>reverse engineer, copy, resell or sublicense the App, or use it to build a competing service;</li>
        <li>interfere with the App's operation, bypass its security, or access data belonging to another Fiscalize account or another Xero organisation;</li>
        <li>use automated means to place excessive load on the App, Xero or ZIMRA; or</li>
        <li>use the App in breach of Xero's developer or end-user terms.</li>
      </ul>

      <h2>9. Availability, changes and support</h2>
      <ul>
        <li>We aim to keep the App available at all times but do not guarantee uninterrupted operation. Maintenance, Xero or ZIMRA outages and events beyond our control may interrupt service.</li>
        <li>We may change, add or remove features of the App. Material changes that reduce functionality will be announced in the App or by email.</li>
        <li>Support is provided through the Fiscalize platform's support channels. Fiscalize does not provide support for Xero itself.</li>
      </ul>

      <h2>10. Intellectual property</h2>
      <p>
        The App, its design, code and documentation are owned by Fiscalize and its licensors and protected by copyright and
        other laws. Xero is a trademark of Xero Limited and is used only to identify the service the App
        connects to.
      </p>

      <h2>11. Disclaimer and limitation of liability</h2>
      <p>
        The App is provided "as is". To the fullest extent permitted by law, Fiscalize disclaims all warranties, express or
        implied, including fitness for a particular purpose. The App assists with compliance but does not constitute tax,
        legal or accounting advice; responsibility for your tax compliance remains with you.
      </p>
      <p>
        To the fullest extent permitted by law, Fiscalize's total liability arising out of or relating to the App in any
        twelve-month period shall not exceed the Fiscalize subscription fees paid by you in that period, and Fiscalize shall
        not be liable for indirect, consequential or special loss, loss of profit, penalties imposed by ZIMRA for data you
        supplied, or loss caused by Xero, Xero or ZIMRA systems.
      </p>

      <h2>12. Suspension and termination</h2>
      <ul>
        <li>You may stop using the App at any time by disconnecting your Xero organisation.</li>
        <li>We may suspend or terminate your access to the App if you breach these Terms, if your Fiscalize account is suspended, if Xero withdraws the App's access, or if required by law or a regulator.</li>
        <li>On termination we remove the App's connection to your organisation at Xero and delete the connection record. Fiscal records are retained as described in section 5.</li>
      </ul>

      <h2>13. Governing law</h2>
      <p>
        These Terms are governed by the laws of Zimbabwe. Disputes are subject to the dispute resolution provisions of the
        Fiscalize Terms and Conditions and, failing resolution, to the jurisdiction of the courts of Zimbabwe.
      </p>

      <h2>14. Changes to these Terms</h2>
      <p>
        We may update these Terms. The "Last updated" date above will change and, for material changes, we will notify you
        in the App or by email at least 14 days before they take effect. Continued use of the App after that date is
        acceptance of the updated Terms.
      </p>

      <h2>15. Contact</h2>
      <p>
        Fiscalize · <a href="https://fiscalize.co.zw" target="_blank" rel="noopener noreferrer">fiscalize.co.zw</a> ·
        <a href="mailto:legal@fiscalize.co.zw">legal@fiscalize.co.zw</a>
      </p>
    </app-legal-shell>
  `
})
export class Terms {}

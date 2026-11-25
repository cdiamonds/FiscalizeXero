// Xero shapes returned by the FiscalizeXero API. Field names are Xero's own with a lower-case first letter
// (invoiceID, contactID, updatedDateUTC). All ids are GUID strings.

export type XeroDocumentStatus = 'DRAFT' | 'SUBMITTED' | 'AUTHORISED' | 'PAID' | 'VOIDED' | 'DELETED';
export type XeroLineAmountTypes = 'Exclusive' | 'Inclusive' | 'NoTax';

export interface XeroContactAddress {
  addressType?: string | null;
  addressLine1?: string | null;
  addressLine2?: string | null;
  addressLine3?: string | null;
  addressLine4?: string | null;
  city?: string | null;
  region?: string | null;
  postalCode?: string | null;
  country?: string | null;
  attentionTo?: string | null;
}

export interface XeroContactPhone {
  phoneType?: string | null;
  phoneNumber?: string | null;
  phoneAreaCode?: string | null;
  phoneCountryCode?: string | null;
}

/** The contact as Xero embeds it on a document (often only the id and name). */
export interface XeroContact {
  contactID: string;
  name?: string | null;
  emailAddress?: string | null;
  taxNumber?: string | null;
  companyNumber?: string | null;
  addresses?: XeroContactAddress[] | null;
  phones?: XeroContactPhone[] | null;
}

export interface XeroLineItem {
  lineItemID?: string | null;
  description?: string | null;
  quantity?: number | null;
  unitAmount?: number | null;
  itemCode?: string | null;
  accountCode?: string | null;
  /** The Xero tax rate's code, e.g. OUTPUT or TAX001. */
  taxType?: string | null;
  taxAmount?: number | null;
  /** Quantity x unit amount less discount; includes tax only on a tax-inclusive document. */
  lineAmount?: number | null;
  discountRate?: number | null;
  discountAmount?: number | null;
  item?: { itemID?: string | null; name?: string | null; code?: string | null } | null;
}

/** What a sales invoice and a customer credit note have in common. */
export interface XeroSalesDocument {
  documentId: string;
  documentNumber?: string | null;
  type?: string | null;
  contact?: XeroContact | null;
  /** A calendar date written as 2026-11-23T00:00:00. */
  dateString?: string | null;
  status?: XeroDocumentStatus | string | null;
  lineAmountTypes?: XeroLineAmountTypes | string | null;
  lineItems?: XeroLineItem[] | null;
  subTotal?: number | null;
  totalTax?: number | null;
  total?: number | null;
  currencyCode?: string | null;
  currencyRate?: number | null;
  reference?: string | null;
  hasAttachments?: boolean | null;
  /** UTC, written without a zone. */
  updatedDateUTC?: string | null;
}

export interface Invoice extends XeroSalesDocument {
  invoiceID: string;
  invoiceNumber?: string | null;
  dueDateString?: string | null;
  amountDue?: number | null;
  amountPaid?: number | null;
  amountCredited?: number | null;
  totalDiscount?: number | null;
  sentToContact?: boolean | null;
  creditNotes?: { creditNoteID: string; creditNoteNumber?: string | null; appliedAmount?: number | null }[] | null;
}

export interface CreditNoteAllocation {
  allocationID?: string | null;
  amount?: number | null;
  date?: string | null;
  invoice?: { invoiceID: string; invoiceNumber?: string | null } | null;
}

export interface CreditNote extends XeroSalesDocument {
  creditNoteID: string;
  creditNoteNumber?: string | null;
  remainingCredit?: number | null;
  /** The invoices this credit was applied to in Xero; empty until the credit is allocated. */
  allocations?: CreditNoteAllocation[] | null;
}

export interface XeroOrganisation {
  organisationID: string;
  name?: string | null;
  legalName?: string | null;
  shortCode?: string | null;
  baseCurrency?: string | null;
  countryCode?: string | null;
  taxNumber?: string | null;
  registrationNumber?: string | null;
  isDemoCompany?: boolean;
  organisationStatus?: string | null;
  timezone?: string | null;
  defaultSalesTax?: string | null;
}

// ---- ClientConfig / OAuth lifecycle ----

export interface ClientConfig {
  id: number;
  appUserCompanyId: number;
  /** The connected Xero organisation. Kept after the connection is lost, so the page can say "reconnect". */
  tenantId?: string | null;
  tenantName?: string | null;
  xeroConnectionId?: string | null;
  isCompanyWide: boolean;
  tokenExpiresAt?: string | null;
  grantedScopeVersion?: number;
  lastSyncAt?: string | null;
  autoFiscalizeInvoices: boolean;
  autoFiscalizeCreditNotes: boolean;
  writebackHistoryNote: boolean;
  fiscalStampEnabled: boolean;
  attachmentIncludeOnline: boolean;
  writebackReference: boolean;
  fiscalStampPosition?: string;
  fiscalStampMarginTop?: number;
  fiscalStampMarginBottom?: number;
  fiscalStampMarginLeft?: number;
  fiscalStampMarginRight?: number;
  fiscalizationValidationMode?: FiscalizationValidationMode;
  defaultHsCode?: string | null;
  effectiveDefaultHsCode?: string | null;
}

/** Corrected buyer details for an already-fiscalized invoice; applying them re-fiscalizes it. */
export interface UpdateInvoiceFiscalInfoRequest {
  buyerData?: UpdateInvoiceFiscalInfoBuyerData;
  /** Clears the buyer and re-fiscalizes as a cash (B2C) sale. */
  removeBuyer?: boolean;
}

export interface UpdateInvoiceFiscalInfoBuyerData {
  registeredName: string;
  tradeName?: string;
  vatNumber?: string;
  tin: string;
  contacts: { phoneNumber: string; email: string };
  address: { province: string; street: string; houseNo: string; city: string };
}

/** One invoice a credit note was applied to in Xero. */
export interface CreditNoteAllocationInfo {
  invoiceId: string;
  invoiceNumber?: string | null;
  amount: number;
  isFiscalized: boolean;
}

/** Which fiscalised invoice a credit note reverses, its Xero allocations, and the invoices to choose from. */
export interface CreditNoteBaseDocument {
  resolvedDocNo?: string | null;
  resolvedIsFiscalized: boolean;
  problem?: string | null;
  allocations: CreditNoteAllocationInfo[];
  candidates: BaseInvoiceCandidate[];
}

export interface BaseInvoiceCandidate {
  documentId: string;
  documentNumber: string;
  documentType?: string;
  receiptNumber?: number | null;
  fiscalizedAt: string;
}

export interface FiscalCancelResult {
  documentId: string;
  documentNumber: string;
  message: string;
  writebackWarning?: string | null;
}

/** A Xero tax rate with its effective percentage and ZIMRA category. */
export interface TaxRateInfo {
  taxType: string;
  name?: string | null;
  percent: number;
  category?: 'Rated' | 'ZeroRated' | 'Exempt' | string | null;
  status?: string | null;
}

/** One row of the Fiscalize HS code catalogue. */
export interface HsCodeLookupItem {
  id: number;
  code: string;
  description?: string | null;
}

export interface PagedHsCodes {
  items: HsCodeLookupItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface RegisterClientResponse {
  clientConfigId: number;
  authUrl: string;
}

export interface FiscalizationSettings {
  autoFiscalizeInvoices: boolean;
  autoFiscalizeCreditNotes: boolean;
  /** Add the fiscal result to the document's "History and notes" in Xero. */
  writebackHistoryNote?: boolean;
  /** Attach the stamped fiscal PDF to the document in Xero. */
  fiscalStampEnabled?: boolean;
  /** Show that attachment to the customer on the online invoice. */
  attachmentIncludeOnline?: boolean;
  /** Add a short fiscal marker to the document's Reference. */
  writebackReference?: boolean;
  /** The PDF attached in Xero: Xero's own with the stamp, the company's Fiscalize layout, or the Fiscalize standard invoice. */
  fiscalPdfLayout?: FiscalPdfLayout;
  /** Paper for the Fiscalize layout. */
  layoutPaperSize?: LayoutPaper;
  /** Where layouts are designed on the Fiscalize platform (read only). */
  layoutDesignerUrl?: string | null;
  fiscalStampPosition?: string;
  fiscalStampMarginTop?: number;
  fiscalStampMarginBottom?: number;
  fiscalStampMarginLeft?: number;
  fiscalStampMarginRight?: number;
  /** HS code used for items that have none of their own. Empty means the system default. */
  defaultHsCode?: string | null;
  /** How buyer details are checked for customers with a TIN. */
  fiscalizationValidationMode?: FiscalizationValidationMode;
}

export type FiscalPdfLayout = 'XeroStamp' | 'FiscalizeLayout' | 'FiscalizeStandard';
export type LayoutPaper = 'A4' | '80mm' | '58mm';

/** The Fiscalize layout the company would use for one document type, as the platform last served it. */
export interface FiscalLayoutStatus {
  documentType: 'Invoice' | 'CreditNote' | string;
  paperSize: string;
  available: boolean;
  isStandardDesign: boolean;
  version: number;
  checkedAt?: string | null;
}

/** Strict: TIN customers must have every buyer detail. Lenient: missing details become placeholders. */
export type FiscalizationValidationMode = 'Strict' | 'Lenient';

/** Who manages the company's Xero connection (GET ClientConfigs/management). */
export interface ConnectionManagement {
  canManage: boolean;
  connectedByName: string | null;
  isYou: boolean;
}

export interface XeroAppConfig {
  redirectUri: string;
  scope: string;
  webhookCallbackBaseUrl: string;
  /** The server holds the Xero app's credentials, so users can connect. */
  configured: boolean;
}

/** What a disconnect did. The connection is always removed; warnings list anything left on Xero's side. */
export interface DisconnectResult {
  disconnected: boolean;
  warnings: string[];
}

// ---- Fiscalization failure ----

/** What kind of problem stopped a document fiscalising, so the UI can say how to fix it. */
export type FiscalFailureCategory = 'Buyer' | 'HsCode' | 'Device' | 'OriginalSale' | 'Platform' | 'Xero' | 'Network' | 'Other';

/** The latest failed attempt to fiscalise a document (cleared when it succeeds). */
export interface FiscalFailure {
  documentId: string;
  documentType: string;
  category: FiscalFailureCategory;
  message: string;
  details: string[];
  /** 'Automatic' (webhook or reconciliation) or 'Manual' (someone clicked Fiscalize). */
  source: 'Automatic' | 'Manual';
  attempts: number;
  firstFailedAt: string;
  lastFailedAt: string;
}

// ---- Fiscalization result ----

export interface FiscalizeResult {
  documentId: string;
  documentNumber: string;
  documentType: string;
  verificationCode?: string;
  qrCodeUrl?: string;
  receiptNumber?: number;
  globalNumber?: number;
  deviceId: number;
  fiscalDayNumber?: number;
  validationError?: string;
  fiscalizedAt: string;
  writebackWarning?: string;
  /** Fiscalization succeeded but the stamped PDF could not be attached in Xero. */
  attachmentWarning?: string | null;
  /** The platform already held this document; the stored fiscal data was refreshed from it. */
  alreadyFiscalized?: boolean;
  /** The stamped PDF is being attached in the background. */
  attachmentQueued?: boolean;
}

export interface FiscalAttachmentStatus {
  attached: boolean;
  fileName?: string | null;
  attachmentDisabled: boolean;
  /** Whether the customer sees the file on the online invoice. */
  includeOnline?: boolean | null;
  warning?: string | null;
  /** Which PDF the company attaches. */
  pdfLayout?: FiscalPdfLayout | null;
}

// ---- Devices ----

export interface FiscalizeDevice {
  id: number;
  deviceID?: number;
  serialNo?: string;
  deviceName?: string;
  shortName?: string;
  tradeName?: string;
  status?: string;
  modelName?: string;
  modelVersion?: string;
  fiscalizeName?: string;
  platform?: string;
  address?: { city?: string; province?: string; [k: string]: any } | string;
  [key: string]: any;
}

export interface FiscalizeDeviceApiKey {
  id: number;
  deviceId: number;
  apiKey?: string;
  apiSecret?: string;
  description?: string;
  permissions?: string;
  status?: string;
  expiresAt?: string;
  createdAt?: string;
  lastUsedAt?: string;
  [key: string]: any;
}

export interface DeviceConfig {
  id: number;
  deviceId: number;
  deviceApiKeyId?: number;
  apiKeyId?: number;
  apiKeyDescription?: string;
  deviceNumber?: string;
  platform?: string;
  isDefault: boolean;
  alias?: string;
  device?: FiscalizeDevice;
  apiKey?: FiscalizeDeviceApiKey;
  /** Saved, but the platform rejected the key on the test login. */
  warning?: string | null;
  [key: string]: any;
}

export interface CreateApiKeyRequest {
  alias?: string;
  description?: string;
  permissions?: string;
  assignedUserEmail?: string;
}

export interface SaveDeviceConfigRequest {
  id?: number;
  deviceId: number;
  deviceApiKeyId?: number;
  alias?: string;
  isDefault?: boolean;
}

// ---- Companies (Fiscalyze platform side) ----

export interface UserCompany {
  id: number;
  /** Fiscalize company id (what the switch/disconnect endpoints take). */
  companyId: number;
  companyName: string;
  isActive: boolean;
  /** A Xero connection exists with a refresh token. */
  isSetupComplete: boolean;
  /** The company's connection was set up by a colleague (it is shared by everyone in the company either way). */
  isUsingSharedConnection: boolean;
  xeroTenantId?: string | null;
  xeroTenantName?: string | null;
  xeroUrl?: string | null;
  needsReauth: boolean;
  /** Why the connection needs attention, when needsReauth is true. */
  connectionProblem?: string | null;
}

// ── Xero webhooks ────────────────────────────────────────────────────────────

export type WebhookEventStatus = 'Pending' | 'Processed' | 'Skipped' | 'Failed';

export interface WebhookEvent {
  id: number;
  entityName: string;
  entityId: string;
  documentNumber?: string | null;
  operation: string;
  /** 'Webhook' (Xero told us) or 'Sync' (found by the periodic check). */
  source?: 'Webhook' | 'Sync' | 'Retry';
  status: WebhookEventStatus;
  detail?: string | null;
  receivedAt: string;
  processedAt?: string | null;
}

export interface WebhookCounts {
  received: number;
  processed: number;
  skipped: number;
  failed: number;
  pending: number;
}

export interface WebhookStatus {
  /** The server holds Xero's webhook key and can accept deliveries. */
  listenerConfigured: boolean;
  /** Exact URL to register as the delivery URL in the Xero developer portal. */
  callbackUrl: string;
  eventCategories: string[];
  connected: boolean;
  tenantId?: string | null;
  tenantName?: string | null;
  autoFiscalizeInvoices: boolean;
  autoFiscalizeCreditNotes: boolean;
  syncEnabled: boolean;
  syncIntervalHours: number;
  lastSyncAt?: string | null;
  lastSyncError?: string | null;
  syncRetryAt?: string | null;
  lastReceivedAt?: string | null;
  last24Hours: WebhookCounts;
  recent: WebhookEvent[];
}

/** One page of a server-paged list (GET xero/{type}/paged). */
export interface DocumentPage<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
  search?: string | null;
  /** The Xero statuses the page was filtered to; empty when every status was asked for. */
  statuses?: string[];
}

export interface DocumentPageQuery {
  page: number;
  pageSize: number;
  search?: string;
  /** '' = approved (awaiting payment and paid), 'all', or a comma list of Xero statuses. */
  status?: string;
  /** Read Xero again instead of the short server cache (items only). */
  refresh?: boolean;
}

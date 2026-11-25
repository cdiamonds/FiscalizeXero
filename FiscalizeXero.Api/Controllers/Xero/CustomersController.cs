using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Shared.Models.TaxDetails;
using FiscalizeXero.Xero.Exceptions;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using XeroAuthModel = FiscalizeXero.Xero.XeroAuth;

namespace FiscalizeXero.Api.Controllers.Xero
{
    /// <summary>
    /// Xero customers (contacts that have been sold to) with their ZIMRA buyer details: names, TIN, VAT, phone,
    /// email, address. Anything the user saves here lives in our database and wins over the Xero contact, so nobody
    /// has to go into Xero to fix a buyer.
    /// </summary>
    [Route("api/xero/customers")]
    [ApiController]
    [Authorize]
    public class CustomersController : XeroBaseController
    {
        /// <summary>Most active customers one overview reads (10 Xero pages of 1,000).</summary>
        private const int OverviewMaxCustomers = 10_000;
        private const int OverviewChunk = 1000;
        /// <summary>How long the overview's customer list is reused, so paging and filtering it don't call Xero again.</summary>
        private static readonly TimeSpan OverviewLifetime = TimeSpan.FromSeconds(60);

        private readonly IContactVatRepository _contactVatRepository;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CustomersController> _logger;

        public CustomersController(
            ICurrentUserService currentUserService,
            IXeroAuthBuilder authBuilder,
            IContactVatRepository contactVatRepository,
            IMemoryCache cache,
            ILogger<CustomersController> logger)
            : base(currentUserService, authBuilder)
        {
            _contactVatRepository = contactVatRepository;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>One page of active customers by name. <c>search</c> is Xero's own contact search (name, contact number, company number, email).</summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<CustomerTaxDetailsModel>), StatusCodes.Status200OK)]
        public async Task<ActionResult> ListAsync([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var (auth, config, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            var contacts = (await ContactService.ListCustomersAsync(auth, Math.Max(page, 1), Math.Clamp(pageSize, 1, 200), search)).Items;
            var saved = (await _contactVatRepository.GetManyAsync(config!.AppUserCompanyId, contacts.Select(c => Key(c.ContactID))))
                .ToDictionary(r => r.ContactId, StringComparer.OrdinalIgnoreCase);

            return Ok(contacts.Select(c => ToModel(c, saved.GetValueOrDefault(Key(c.ContactID)))).ToList());
        }

        /// <summary>
        /// Every active customer with its ZIMRA compliance, filtered and paged, plus counts per status. Search matches
        /// name, email, phone, TIN or VAT number. <paramref name="status"/> is NoTin, Compliant, MissingDetails,
        /// Invalid, Attention (missing or invalid), or empty for all. The customer list is reused for a minute;
        /// <paramref name="refresh"/> reads Xero again.
        /// </summary>
        [HttpGet("overview")]
        [ProducesResponseType(typeof(CustomerOverviewModel), StatusCodes.Status200OK)]
        public async Task<ActionResult> OverviewAsync([FromQuery] string? search, [FromQuery] string? status, [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25, [FromQuery] bool refresh = false)
        {
            var (auth, config, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            pageSize = Math.Clamp(pageSize, 5, 100);
            page = Math.Max(page, 1);

            var (contacts, truncated) = await AllCustomersAsync(auth, refresh);
            var saved = (await _contactVatRepository.GetAllForCompanyAsync(config!.AppUserCompanyId))
                .GroupBy(r => r.ContactId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var models = contacts.Select(c => ToModel(c, saved.GetValueOrDefault(Key(c.ContactID))));
            var term = search?.Trim();
            if (!string.IsNullOrEmpty(term))
            {
                var digits = new string(term.Where(char.IsDigit).ToArray());
                models = models.Where(m =>
                    Contains(m.DisplayName, term) || Contains(m.RegisteredName, term) ||
                    Contains(m.TradeName, term) || Contains(m.Email, term) || Contains(m.Tin, term) || Contains(m.VatNumber, term) ||
                    (digits.Length >= 4 && (m.Phone ?? string.Empty).Contains(digits)));
            }
            var matching = models.ToList();

            var counts = new CustomerComplianceCounts
            {
                All = matching.Count,
                Compliant = matching.Count(m => m.ComplianceStatus == BuyerStatus.Compliant),
                MissingDetails = matching.Count(m => m.ComplianceStatus == BuyerStatus.MissingDetails),
                Invalid = matching.Count(m => m.ComplianceStatus == BuyerStatus.Invalid),
                NoTin = matching.Count(m => m.ComplianceStatus == BuyerStatus.NoTin),
            };

            // "Attention" is every customer that would stop or need placeholders: missing details or invalid TIN/VAT.
            var filtered = string.IsNullOrWhiteSpace(status) ? matching
                : string.Equals(status.Trim(), "Attention", StringComparison.OrdinalIgnoreCase)
                    ? matching.Where(m => m.ComplianceStatus is BuyerStatus.MissingDetails or BuyerStatus.Invalid).ToList()
                    : matching.Where(m => string.Equals(m.ComplianceStatus, status.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

            return Ok(new CustomerOverviewModel
            {
                Items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                Total = filtered.Count,
                Page = page,
                PageSize = pageSize,
                Counts = counts,
                ValidationMode = config.FiscalizationValidationMode.ToString(),
                Truncated = truncated
            });
        }

        /// <summary>Every active customer of the organisation, read from Xero in pages of 1,000 and kept for a minute.</summary>
        private async Task<(List<Contact> Contacts, bool Truncated)> AllCustomersAsync(XeroAuthModel auth, bool refresh)
        {
            var key = OverviewKey(auth.TenantId);
            // A reload asked for within seconds of the last read is served from memory: up to 10 Xero calls each
            // time would let one stuck tab use up the organisation's daily allowance that fiscalisation needs.
            if (refresh && _cache.TryGetValue(key + ":fresh", out _)) refresh = false;
            if (!refresh && _cache.TryGetValue(key, out (List<Contact> Contacts, bool Truncated) cached) && cached.Contacts != null)
                return cached;

            var contacts = new List<Contact>();
            var truncated = false;
            for (var page = 1; ; page++)
            {
                var batch = await ContactService.ListCustomersAsync(auth, page, OverviewChunk);
                contacts.AddRange(batch.Items);
                if (batch.Items.Count < OverviewChunk || page >= batch.PageCount) break;
                if (contacts.Count >= OverviewMaxCustomers) { truncated = true; break; }
            }

            var result = (contacts, truncated);
            _cache.Set(key, result, OverviewLifetime);
            _cache.Set(key + ":fresh", true, TimeSpan.FromSeconds(15));
            return result;
        }

        private static string OverviewKey(Guid tenantId) => $"xero-customers:{tenantId}";

        private static bool Contains(string? value, string term)
            => !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(CustomerTaxDetailsModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetAsync(Guid id)
        {
            var (auth, config, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            var contact = await FindAsync(auth, id);
            if (contact == null)
                return NotFound(new ErrorModel { Code = 404, Message = $"Xero has no contact with id {id} in this organisation." });

            var rec = await _contactVatRepository.GetAsync(config!.AppUserCompanyId, Key(id));
            var model = ToModel(contact, rec);
            model.ValidationMode = config.FiscalizationValidationMode.ToString();
            return Ok(model);
        }

        /// <summary>
        /// Save buyer details for a customer. A value left out (null) is read from Xero; an empty value is cleared, so
        /// nothing is sent for it. With <c>UpdateXero</c> the TIN and VAT number are also copied to the Xero contact
        /// (Tax Number and Company Number).
        /// </summary>
        [HttpPut("{id:guid}/tax-details")]
        [ProducesResponseType(typeof(CustomerTaxDetailsModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public async Task<ActionResult> SaveTaxDetailsAsync(Guid id, [FromBody] SaveCustomerTaxDetailsModel model)
        {
            var (auth, config, error) = await ResolveAuthAsync();
            if (auth == null) return error!;

            var contact = await FindAsync(auth, id);
            if (contact == null)
                return NotFound(new ErrorModel { Code = 404, Message = $"Xero has no contact with id {id} in this organisation." });

            var (values, invalid) = Normalise(model);
            if (invalid != null)
                return BadRequest(new ErrorModel { Code = 400, Message = invalid });

            bool? updated = null;
            string? warning = null;
            if (model.UpdateXero)
            {
                // Best effort: fiscalising never depends on it, and whatever Xero does not hold stays in Fiscalize.
                var outcome = await XeroContactUpdater.ApplyAsync(auth, contact, values, _logger);
                (values, contact, updated, warning) = (outcome.Values, outcome.Contact, outcome.Updated, outcome.Warning);
            }

            await _contactVatRepository.UpsertAsync(config!.AppUserCompanyId, Key(id), values);
            // Documents that failed on buyer details are tried again now rather than at their next scheduled retry.
            var failures = HttpContext?.RequestServices.GetService<FiscalizeXero.Api.Services.Fiscalization.IFiscalFailureService>();
            if (failures != null) await failures.RetrySoonAsync(config.AppUserCompanyId, FiscalizeXero.Api.Data.FiscalFailureCategory.Buyer);
            _cache.Remove(OverviewKey(auth.TenantId));

            var rec = await _contactVatRepository.GetAsync(config.AppUserCompanyId, Key(id));
            var result = ToModel(contact, rec);
            result.ValidationMode = config.FiscalizationValidationMode.ToString();
            result.XeroUpdated = updated;
            result.XeroWarning = warning;
            return Ok(result);
        }

        private static async Task<Contact?> FindAsync(XeroAuthModel auth, Guid id)
        {
            try { return await ContactService.GetByIdAsync(auth, id); }
            catch (XeroApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
        }

        /// <summary>Cleans what the user typed and says what is wrong with it, using the rules fiscalising applies.</summary>
        internal static (ContactVatRecord Values, string? Error) Normalise(SaveCustomerTaxDetailsModel m)
        {
            // null = not set here (use Xero); "" = the user cleared the box, so nothing is sent for it.
            static string? Keep(string? v) => v == null ? null : v.Trim();
            static string? Digits(string? v) => Keep(v) is { } c ? new string(c.Where(ch => !char.IsWhiteSpace(ch) && ch is not ('-' or '.' or '/')).ToArray()) : null;

            var values = new ContactVatRecord
            {
                RegisteredName = Keep(m.RegisteredName),
                TradeName = Keep(m.TradeName),
                Tin = Digits(m.Tin),
                VatNumber = Digits(m.VatNumber),
                Phone = Keep(m.Phone),
                Email = Keep(m.Email),
                HouseNo = Keep(m.HouseNo),
                Street = Keep(m.Street),
                City = Keep(m.City),
                Province = Keep(m.Province)
            };

            string? error = null;
            if (!string.IsNullOrEmpty(values.Tin) && (BuyerCompliance.NormaliseDigits(values.Tin, 10) == null || values.Tin == "0000000000"))
                error = "A TIN is exactly 10 digits, for example 2000123456.";
            else if (!string.IsNullOrEmpty(values.VatNumber) && BuyerCompliance.NormaliseDigits(values.VatNumber, 9) == null)
                error = "A VAT number is exactly 9 digits, for example 220123456.";
            else if (!string.IsNullOrEmpty(values.Phone) && BuyerContactResolver.NormalisePhone(values.Phone) == null)
                error = "A phone number needs 7 to 15 digits, for example 0771234567 or +263771234567.";
            else if (!string.IsNullOrEmpty(values.Email) && BuyerContactResolver.FirstEmail(values.Email) == null)
                error = "Enter a valid email address, for example accounts@company.co.zw.";
            else if (Too(values.TradeName, BuyerCompliance.MaxTradeNameLength) || Too(values.RegisteredName, 100) || Too(values.Email, 200)
                || Too(values.HouseNo, 50) || Too(values.Street, 100) || Too(values.City, 50) || Too(values.Province, 50))
                error = "One of the values is too long.";

            if (error == null && !string.IsNullOrEmpty(values.Phone)) values.Phone = BuyerContactResolver.NormalisePhone(values.Phone);
            if (error == null && !string.IsNullOrEmpty(values.Email)) values.Email = BuyerContactResolver.FirstEmail(values.Email);
            return (values, error);

            static bool Too(string? v, int max) => v != null && v.Length > max;
        }

        internal static CustomerTaxDetailsModel ToModel(Contact contact, ContactVatRecord? saved)
        {
            var resolved = BuyerResolver.Resolve(contact, saved);
            // The same assessment the fiscaliser makes, so the badge always matches what fiscalising will do.
            var a = BuyerCompliance.Assess(resolved, contact);
            // And what Xero alone would give, shown as the fallback for every field left blank in Fiscalize.
            var xero = BuyerCompliance.Assess(BuyerResolver.Resolve(contact, null), contact);
            var address = BuyerCompliance.SourceAddress(contact);
            return new CustomerTaxDetailsModel
            {
                CustomerId = Key(contact.ContactID),
                DisplayName = contact.Name,
                Email = a.Email,
                Phone = a.Phone,
                PhoneSource = a.PhoneSource,
                DefaultPhone = Clean(contact.Phone("DEFAULT")?.Full),
                Mobile = Clean(contact.Phone("MOBILE")?.Full),
                DirectDial = Clean(contact.Phone("DDI")?.Full),
                AddressText = address == null ? null : string.Join(", ", new[] { address.AddressLine1, address.AddressLine2, address.AddressLine3, address.AddressLine4, address.City, address.Region, address.PostalCode, address.Country }
                    .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim())),
                HouseNo = a.Address.HouseNo,
                Street = a.Address.Street,
                City = a.Address.City,
                Province = a.Address.Province,
                RegisteredName = resolved.RegisteredName,
                TradeName = resolved.TradeName,
                VatNumber = resolved.VatNumber,
                Tin = resolved.Tin,
                Source = BuyerResolver.HasOverride(saved) ? "Override" : "Xero",
                Saved = new BuyerDetailsValuesModel
                {
                    RegisteredName = saved?.RegisteredName, TradeName = saved?.TradeName, Tin = saved?.Tin, VatNumber = saved?.VatNumber,
                    Phone = saved?.Phone, Email = saved?.Email, HouseNo = saved?.HouseNo, Street = saved?.Street, City = saved?.City, Province = saved?.Province
                },
                Xero = new BuyerDetailsValuesModel
                {
                    RegisteredName = xero.RegisteredName, TradeName = xero.TradeName, Tin = xero.RawTin, VatNumber = xero.RawVatNumber,
                    Phone = xero.Phone, Email = xero.Email, HouseNo = xero.Address.HouseNo, Street = xero.Address.Street, City = xero.Address.City, Province = xero.Address.Province
                },
                XeroTaxNumber = Clean(contact.TaxNumber),
                XeroCompanyNumber = Clean(contact.CompanyNumber),
                ComplianceStatus = a.Status,
                Missing = a.Missing.ToList(),
                Problems = a.Problems.ToList()
            };
        }

        private static string Key(Guid contactId) => FiscalDocumentType.Key(contactId);

        private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}

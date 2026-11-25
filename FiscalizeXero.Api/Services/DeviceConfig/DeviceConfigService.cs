using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.DeviceSession;
using FiscalizeXero.Shared.Models.Fiscalize;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Services.DeviceConfig
{
    public class DeviceConfigService : IDeviceConfigService
    {
        private readonly DataContext _context;
        private readonly IDeviceSessionService _deviceSessionService;
        private readonly ILogger<DeviceConfigService> _logger;

        private readonly Sync.IXeroSyncRequests? _sync;

        public DeviceConfigService(DataContext context, IDeviceSessionService deviceSessionService, ILogger<DeviceConfigService> logger,
            Sync.IXeroSyncRequests? sync = null)
        {
            _sync = sync;
            _context = context;
            _deviceSessionService = deviceSessionService;
            _logger = logger;
        }

        public async Task<List<AppUserDeviceConfig>> GetAllAsync(int appUserCompanyId)
        {
            return await _context.DeviceConfigs
                .Where(dc => dc.AppUserCompanyId == appUserCompanyId)
                .OrderByDescending(dc => dc.IsDefault)
                .ThenByDescending(dc => dc.DateConfigured)
                .ToListAsync();
        }

        public async Task<AppUserDeviceConfig?> GetDefaultAsync(int appUserCompanyId)
        {
            return await _context.DeviceConfigs
                .Include(dc => dc.AppUserCompany)
                .FirstOrDefaultAsync(dc => dc.AppUserCompanyId == appUserCompanyId && dc.IsDefault);
        }

        public async Task<(AppUserDeviceConfig Config, string? Warning)> SaveAsync(int appUserCompanyId, SaveDeviceConfigModel model)
        {
            var existing = await _context.DeviceConfigs
                .FirstOrDefaultAsync(dc => dc.AppUserCompanyId == appUserCompanyId && dc.DeviceId == model.DeviceId);

            // The platform only hands out a key's secret when the key is created or rotated, so a key picked from
            // the list has none. Without the secret the device can never log in, so refuse rather than store it.
            var keyChanged = existing == null || !string.Equals(existing.ApiKey, model.ApiKey, StringComparison.Ordinal);
            var secretAvailable = !string.IsNullOrEmpty(model.ApiSecret) || (!keyChanged && !string.IsNullOrEmpty(existing?.ApiSecret));
            if (!string.IsNullOrEmpty(model.ApiKey) && !secretAvailable)
                throw new FiscalizeXero.Api.Exceptions.UserFacingException(
                    "This key's secret is not available, so the device could not sign in with it. Rotate the key (or create a new one) to get a secret, then save.");

            // The device a user configures (or reconfigures) is the one they mean to fiscalise with, so it becomes
            // the default; "Set as default" on the page switches between configured devices without re-saving.
            var others = await _context.DeviceConfigs
                .Where(dc => dc.AppUserCompanyId == appUserCompanyId && dc.DeviceId != model.DeviceId)
                .ToListAsync();
            foreach (var other in others) other.IsDefault = false;

            if (existing == null)
            {
                existing = new AppUserDeviceConfig
                {
                    AppUserCompanyId = appUserCompanyId,
                    DateConfigured = DateTime.UtcNow
                };
                _context.DeviceConfigs.Add(existing);
            }
            else
            {
                existing.DateUpdated = DateTime.UtcNow;
            }
            existing.IsDefault = true;

            existing.DeviceId = model.DeviceId;
            existing.Platform = model.Platform;
            existing.DeviceNumber = model.DeviceNumber;
            existing.ApiKeyId = model.ApiKeyId;
            existing.ApiKey = model.ApiKey;
            existing.ApiKeyDescription = model.ApiKeyDescription;

            // Only update ApiSecret when a non-empty value is explicitly provided.
            // DeviceConfigModel omits ApiSecret for security; a re-save that doesn't
            // include the secret must not wipe the value stored in the database.
            if (!string.IsNullOrEmpty(model.ApiSecret))
                existing.ApiSecret = model.ApiSecret;

            await _context.SaveChangesAsync();

            // Drop any stale cached session so the next use picks up fresh credentials.
            _deviceSessionService.InvalidateSession(existing.Id);

            // Warm up the session cache immediately so the first fiscalization
            // request after a credential change doesn't pay the login round-trip.
            if (!string.IsNullOrEmpty(existing.ApiKey) && !string.IsNullOrEmpty(existing.ApiSecret))
            {
                try
                {
                    await _deviceSessionService.GetAuthAsync(existing);
                    _logger.LogInformation(
                        "Device {DeviceNumber} session warmed up after credential save (config {Id})",
                        existing.DeviceNumber, existing.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Device {DeviceNumber} credential validation failed after save (config {Id}). " +
                        "Credentials stored but may be rejected by Fiscalize.",
                        existing.DeviceNumber, existing.Id);
                    return (existing, $"Saved, but the Fiscalize platform rejected this key when the device tried to sign in ({ex.Message}). Rotate the key and save again.");
                }
            }

            await DeviceReadyAsync(appUserCompanyId);
            return (existing, null);
        }

        /// <summary>
        /// A working device was just saved: run the backstop sync for the company now (it waits while there is no
        /// device, so this is what catches up on documents approved meanwhile) and retry device failures straight away.
        /// </summary>
        private async Task DeviceReadyAsync(int appUserCompanyId)
        {
            try
            {
                var configId = await _context.Configs.Where(c => c.AppUserCompanyId == appUserCompanyId && c.TenantId != null)
                    .Select(c => (long?)c.Id).FirstOrDefaultAsync();
                if (configId.HasValue) _sync?.RequestSync(configId.Value);
                var now = DateTime.UtcNow;
                await _context.FiscalFailures
                    .Where(f => f.AppUserCompanyId == appUserCompanyId && f.Category == FiscalFailureCategory.Device)
                    .ExecuteUpdateAsync(u => u.SetProperty(f => f.NextRetryAt, now));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not start the catch-up after a device was saved (company {CompanyId})", appUserCompanyId);
            }
        }

        public async Task<AppUserDeviceConfig?> SetDefaultAsync(int appUserCompanyId, int configId)
        {
            var configs = await _context.DeviceConfigs
                .Where(dc => dc.AppUserCompanyId == appUserCompanyId)
                .ToListAsync();

            var target = configs.FirstOrDefault(dc => dc.Id == configId);
            if (target == null) return null;

            foreach (var c in configs)
                c.IsDefault = c.Id == configId;

            await _context.SaveChangesAsync();

            _deviceSessionService.InvalidateSession(target.Id);
            await DeviceReadyAsync(appUserCompanyId);

            return target;
        }

        public async Task<bool> DeleteAsync(int appUserCompanyId, int configId)
        {
            var config = await _context.DeviceConfigs.FindAsync(configId);
            if (config == null || config.AppUserCompanyId != appUserCompanyId) return false;

            var wasDefault = config.IsDefault;
            var companyId = config.AppUserCompanyId;

            _context.DeviceConfigs.Remove(config);
            await _context.SaveChangesAsync();

            _deviceSessionService.InvalidateSession(configId);

            if (wasDefault)
            {
                var next = await _context.DeviceConfigs
                    .Where(dc => dc.AppUserCompanyId == companyId)
                    .OrderByDescending(dc => dc.DateConfigured)
                    .FirstOrDefaultAsync();

                if (next != null)
                {
                    next.IsDefault = true;
                    await _context.SaveChangesAsync();
                }
            }

            return true;
        }

        public async Task UpdateCredentialsAsync(int configId, long? apiKeyId, string? apiKey, string? apiSecret)
        {
            var config = await _context.DeviceConfigs.FindAsync(configId);
            if (config == null) return;

            config.ApiKeyId = apiKeyId;
            config.ApiKey = apiKey;
            config.ApiSecret = apiSecret;
            config.DateUpdated = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            _deviceSessionService.InvalidateSession(configId);

            _logger.LogInformation(
                "Credentials updated for device config {Id} (device #{DeviceNumber})",
                configId, config.DeviceNumber);
        }
    }
}

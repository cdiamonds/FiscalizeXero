using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.AppUserCompanies;
using FiscalizeXero.Fiscalize.Models.Auths;

namespace FiscalizeXero.Api.Services.AppUsers
{
    public class AppUserService : IAppUserService
    {
        private readonly IAppUserRepository _appUserRepository;
        private readonly IAppUserCompanyService _appUserCompanyService;

        public AppUserService(IAppUserRepository appUserRepository, IAppUserCompanyService appUserCompanyService)
        {
            _appUserRepository = appUserRepository;
            _appUserCompanyService = appUserCompanyService;
        }

        public async Task<IEnumerable<AppUser>> GetAllAsync()
        {
            return await _appUserRepository.GetAllAsync();
        }

        public async Task<AppUser?> GetByIdAsync(int id)
        {
            return await _appUserRepository.GetByIdAsync(id);
        }

        public async Task<AppUser?> GetByEmailAsync(string email)
        {
            return await _appUserRepository.GetByEmailAsync(email);
        }

        public async Task<AppUser> CreateAsync(AppUser appUser)
        {
            return await _appUserRepository.CreateAsync(appUser);
        }

        public async Task<AppUser> UpdateAsync(AppUser appUser)
        {
            return await _appUserRepository.UpdateAsync(appUser);
        }

        public async Task<bool> DeleteAsync(int id)
        {
            return await _appUserRepository.DeleteAsync(id);
        }

        public async Task<AppUser> GetOrCreateAppUserAsync(LoginResponseModel loginResponse, string? email = null)
        {
            var userId = loginResponse.UserId;
            var activeCompany = loginResponse.ActiveCompany;

            var existingUser = await _appUserRepository.GetByUserIdAsync(userId);

            if (existingUser != null)
            {
                if (activeCompany != null)
                {
                    var existingCompany = existingUser.Companies
                        .FirstOrDefault(c => c.CompanyId == activeCompany.Id);

                    if (existingCompany == null)
                    {
                        // Company not associated yet — create it with the token
                        var newCompany = await _appUserCompanyService.CreateAsync(new AppUserCompany
                        {
                            AppUserId = existingUser.Id,
                            CompanyId = activeCompany.Id,
                            CompanyName = activeCompany.Name,
                            FiscalizeToken = loginResponse.Token,
                            FiscalizeTokenExpiresAt = FiscalizeXero.Api.Services.FiscalizeSession.PlatformSessions.Expiry(loginResponse.TokeExpiry),
                            FiscalizeRefreshToken = loginResponse.RefreshToken
                        });

                        existingUser.ActiveCompanyId = newCompany.Id;
                    }
                    else
                    {
                        // Mutate in-memory — same EF context tracks this, single save below picks it up
                        existingCompany.FiscalizeToken = loginResponse.Token;
                        existingCompany.FiscalizeTokenExpiresAt = FiscalizeXero.Api.Services.FiscalizeSession.PlatformSessions.Expiry(loginResponse.TokeExpiry);
                        // A launch from the Fiscalize platform may bring a short-lived token without a refresh token:
                        // then keep the one already saved, so the session can still be renewed once that token expires.
                        if (!string.IsNullOrEmpty(loginResponse.RefreshToken))
                            existingCompany.FiscalizeRefreshToken = loginResponse.RefreshToken;
                        existingUser.ActiveCompanyId = existingCompany.Id;
                    }
                }

                await _appUserRepository.UpdateAsync(existingUser);

                // Refresh to include all navigation properties
                existingUser = await _appUserRepository.GetByUserIdAsync(userId);

                return existingUser!;
            }

            // Create new AppUser
            var newAppUser = new AppUser
            {
                UserId = userId,
                Email = email
            };

            var createdUser = await _appUserRepository.CreateAsync(newAppUser);

            // Add the active company with the token and set it as active
            if (activeCompany != null)
            {
                var newCompany = await _appUserCompanyService.CreateAsync(new AppUserCompany
                {
                    AppUserId = createdUser.Id,
                    CompanyId = activeCompany.Id,
                    CompanyName = activeCompany.Name,
                    FiscalizeToken = loginResponse.Token,
                    FiscalizeTokenExpiresAt = FiscalizeXero.Api.Services.FiscalizeSession.PlatformSessions.Expiry(loginResponse.TokeExpiry),
                    FiscalizeRefreshToken = loginResponse.RefreshToken
                });

                createdUser.ActiveCompanyId = newCompany.Id;
                await _appUserRepository.UpdateAsync(createdUser);

                // Refresh to include all navigation properties
                createdUser = await _appUserRepository.GetByUserIdAsync(userId);
            }

            return createdUser!;
        }

        public async Task<AppUser?> GetByUserIdAsync(long userId)
        {
            return await _appUserRepository.GetByUserIdAsync(userId);
        }
    }
}

using FiscalizeXero.Api.Data;
using FiscalizeXero.Fiscalize.Models.Auths;

namespace FiscalizeXero.Api.Services.AppUsers
{
    public interface IAppUserService
    {
        Task<IEnumerable<AppUser>> GetAllAsync();
        Task<AppUser?> GetByIdAsync(int id);
        Task<AppUser?> GetByUserIdAsync(long userId);
        Task<AppUser?> GetByEmailAsync(string email);
        Task<AppUser> GetOrCreateAppUserAsync(LoginResponseModel loginResponse, string? email = null);
        Task<AppUser> CreateAsync(AppUser appUser);
        Task<AppUser> UpdateAsync(AppUser appUser);
        Task<bool> DeleteAsync(int id);
    }
}

using FiscalizeXero.Api.Data;

namespace FiscalizeXero.Api.Repositories
{
    public interface IAppUserRepository
    {
        Task<IEnumerable<AppUser>> GetAllAsync();
        Task<AppUser?> GetByIdAsync(int id);
        Task<AppUser?> GetByUserIdAsync(long userId);
        Task<AppUser?> GetByEmailAsync(string email);
        Task<AppUser> CreateAsync(AppUser appUser);
        Task<AppUser> UpdateAsync(AppUser appUser);
        Task<bool> DeleteAsync(int id);
    }
}

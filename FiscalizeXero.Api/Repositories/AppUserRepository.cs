using FiscalizeXero.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Repositories
{
    public class AppUserRepository : IAppUserRepository
    {
        private readonly DataContext _context;

        public AppUserRepository(DataContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<AppUser>> GetAllAsync()
        {
            return await _context.AppUsers
                .Include(u => u.Companies)
                .Include(u => u.ActiveCompany)
                    .ThenInclude(c => c!.ClientConfig)
                .ToListAsync();
        }

        public async Task<AppUser?> GetByIdAsync(int id)
        {
            return await _context.AppUsers
                .Include(u => u.Companies)
                .Include(u => u.ActiveCompany)
                    .ThenInclude(c => c!.ClientConfig)
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<AppUser?> GetByUserIdAsync(long userId)
        {
            return await _context.AppUsers
                .Include(u => u.Companies)
                .Include(u => u.ActiveCompany)
                    .ThenInclude(c => c!.ClientConfig)
                .FirstOrDefaultAsync(u => u.UserId == userId);
        }

        public async Task<AppUser?> GetByEmailAsync(string email)
        {
            return await _context.AppUsers
                .Include(u => u.Companies)
                .Include(u => u.ActiveCompany)
                    .ThenInclude(c => c!.ClientConfig)
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<AppUser> CreateAsync(AppUser appUser)
        {
            _context.AppUsers.Add(appUser);
            await _context.SaveChangesAsync();
            return appUser;
        }

        public async Task<AppUser> UpdateAsync(AppUser appUser)
        {
            _context.AppUsers.Update(appUser);
            await _context.SaveChangesAsync();
            return appUser;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var appUser = await _context.AppUsers.FindAsync(id);
            if (appUser == null)
                return false;

            _context.AppUsers.Remove(appUser);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}

using FiscalizeXero.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api
{
    public class SeedData
    {
        /// <summary>Applies pending migrations at startup, so a fresh deployment creates its own schema.</summary>
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DataContext>();
            await context.Database.MigrateAsync();
        }
    }
}

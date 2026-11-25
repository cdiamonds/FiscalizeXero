using FiscalizeXero.Api.Security;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Data
{
    public class DataContext : IdentityDbContext
    {
        private readonly ISecretProtector? _protector;

        public DataContext(DbContextOptions options) : base(options)
        {
        }

        /// <summary>Production constructor: secrets are encrypted at rest with the registered protector.</summary>
        public DataContext(DbContextOptions options, ISecretProtector protector) : base(options)
        {
            _protector = protector;
        }

        #region TABLES-----------------------------------
        public DbSet<AppUser> AppUsers { get; set; }
        public DbSet<AppUserCompany> AppUserCompanies { get; set; }
        public DbSet<ClientConfig> Configs { get; set; }
        public DbSet<AppUserDeviceConfig> DeviceConfigs { get; set; }
        public DbSet<FiscalRecord> FiscalRecords { get; set; }
        public DbSet<ContactVatRecord> ContactVatRecords { get; set; }
        public DbSet<ItemHsCodeRecord> ItemHsCodeRecords { get; set; }
        public DbSet<WebhookEvent> WebhookEvents { get; set; }
        public DbSet<FiscalFailure> FiscalFailures { get; set; }
        public DbSet<FiscalSuppression> FiscalSuppressions { get; set; }
        public DbSet<LayoutPackageCache> LayoutPackageCaches { get; set; }
        #endregion

        /// <summary>
        /// Every time this service stores is UTC, but SQL Server hands datetime2 values back without a kind, so they
        /// reached the browser without a "Z" and were shown two hours early in Zimbabwe. Marking them UTC on the way
        /// out fixes every screen that shows a stored time.
        /// </summary>
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);
            configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
            configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        }

        private sealed class UtcDateTimeConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>
        {
            public UtcDateTimeConverter() : base(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
        }

        private sealed class NullableUtcDateTimeConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>
        {
            public NullableUtcDateTimeConverter() : base(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v) { }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Secrets at rest. Encrypted values are longer than the plaintext (nonce + tag + base64), so these
            // columns are unbounded. Without a protector (design-time tooling, tests) they are stored as given.
            if (_protector != null)
            {
                var protect = new ProtectedStringConverter(_protector);
                modelBuilder.Entity<ClientConfig>().Property(c => c.RefreshToken).HasConversion(protect);
                modelBuilder.Entity<ClientConfig>().Property(c => c.AccessToken).HasConversion(protect);
                modelBuilder.Entity<AppUserCompany>().Property(c => c.FiscalizeToken).HasConversion(protect);
                modelBuilder.Entity<AppUserCompany>().Property(c => c.FiscalizeRefreshToken).HasConversion(protect);
                modelBuilder.Entity<AppUser>().Property(u => u.FiscalizeToken).HasConversion(protect);
                modelBuilder.Entity<AppUser>().Property(u => u.FiscalizeRefreshToken).HasConversion(protect);
                modelBuilder.Entity<AppUserDeviceConfig>().Property(d => d.ApiSecret).HasConversion(protect);
            }

            // AppUser -> AppUserCompany (one-to-many)
            modelBuilder.Entity<AppUser>()
                .HasMany(u => u.Companies)
                .WithOne(c => c.AppUser)
                .HasForeignKey(c => c.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);

            // AppUserCompany -> ClientConfig (one-to-one)
            modelBuilder.Entity<AppUserCompany>()
                .HasOne(c => c.ClientConfig)
                .WithOne(cc => cc.AppUserCompany)
                .HasForeignKey<ClientConfig>(cc => cc.AppUserCompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            // AppUserCompany -> AppUserDeviceConfig (one-to-many)
            modelBuilder.Entity<AppUserCompany>()
                .HasMany(c => c.DeviceConfigs)
                .WithOne(dc => dc.AppUserCompany)
                .HasForeignKey(dc => dc.AppUserCompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique constraint: One AppUserCompany per AppUser+CompanyId combination
            modelBuilder.Entity<AppUserCompany>()
                .HasIndex(c => new { c.AppUserId, c.CompanyId })
                .IsUnique();

            // AppUserCompany -> FiscalRecord (one-to-many)
            modelBuilder.Entity<AppUserCompany>()
                .HasMany<FiscalRecord>()
                .WithOne(r => r.AppUserCompany)
                .HasForeignKey(r => r.AppUserCompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            // Fast lookup by company + document
            modelBuilder.Entity<FiscalRecord>()
                .HasIndex(r => new { r.AppUserCompanyId, r.DocumentId })
                .IsUnique();

            // AppUserCompany -> ContactVatRecord (one-to-many)
            modelBuilder.Entity<AppUserCompany>()
                .HasMany<ContactVatRecord>()
                .WithOne(r => r.AppUserCompany)
                .HasForeignKey(r => r.AppUserCompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            // Fast lookup by company + contact
            modelBuilder.Entity<ContactVatRecord>()
                .HasIndex(r => new { r.AppUserCompanyId, r.ContactId })
                .IsUnique();

            // AppUserCompany -> ItemHsCodeRecord (one-to-many)
            modelBuilder.Entity<AppUserCompany>()
                .HasMany<ItemHsCodeRecord>()
                .WithOne(r => r.AppUserCompany)
                .HasForeignKey(r => r.AppUserCompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            // Fast lookup by company + item
            modelBuilder.Entity<ItemHsCodeRecord>()
                .HasIndex(r => new { r.AppUserCompanyId, r.ItemCode })
                .IsUnique();

            // Webhook delivery log: the page lists an organisation's latest events; the worker drains Pending rows
            modelBuilder.Entity<WebhookEvent>()
                .HasIndex(e => new { e.TenantId, e.ReceivedAt });
            modelBuilder.Entity<WebhookEvent>()
                .HasIndex(e => e.Status);

            // One failure row per document; lists look failures up by company and document id.
            modelBuilder.Entity<FiscalFailure>()
                .HasIndex(f => new { f.AppUserCompanyId, f.DocumentType, f.DocumentId })
                .IsUnique();
            modelBuilder.Entity<FiscalFailure>()
                .HasIndex(f => f.NextRetryAt);

            modelBuilder.Entity<FiscalSuppression>()
                .HasIndex(s => new { s.AppUserCompanyId, s.DocumentId })
                .IsUnique();
            modelBuilder.Entity<AppUserCompany>()
                .HasMany<FiscalSuppression>()
                .WithOne()
                .HasForeignKey(s => s.AppUserCompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AppUserCompany>()
                .HasMany<FiscalFailure>()
                .WithOne()
                .HasForeignKey(f => f.AppUserCompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<LayoutPackageCache>()
                .HasIndex(l => new { l.AppUserCompanyId, l.DocumentType, l.PaperSize })
                .IsUnique();
            modelBuilder.Entity<AppUserCompany>()
                .HasMany<LayoutPackageCache>()
                .WithOne()
                .HasForeignKey(l => l.AppUserCompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}

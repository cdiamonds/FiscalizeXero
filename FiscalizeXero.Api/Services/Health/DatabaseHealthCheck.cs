using FiscalizeXero.Api.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FiscalizeXero.Api.Services.Health
{
    /// <summary>Reports Unhealthy when SQL Server cannot be reached within five seconds.</summary>
    public sealed class DatabaseHealthCheck : IHealthCheck
    {
        private readonly DataContext _context;
        public DatabaseHealthCheck(DataContext context) => _context = context;

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                return await _context.Database.CanConnectAsync(cts.Token)
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy("Database not reachable");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Database check failed", ex);
            }
        }
    }
}

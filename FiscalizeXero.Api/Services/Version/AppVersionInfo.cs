using System.Diagnostics;
using System.Reflection;

namespace FiscalizeXero.Api.Services.Version
{
    /// <summary>
    /// What is running: the API version (the csproj &lt;Version&gt;, bumped by publish.sh), the git commit it was built from
    /// (the SDK appends it to the informational version as "+&lt;sha&gt;"), when the assembly was built and when this process
    /// started. Public by design (no configuration value), so anyone deploying can check which build answers.
    /// </summary>
    public sealed record AppVersionInfo(string Version, string? Commit, DateTime? BuiltAt, DateTime StartedAt, string Environment, string Runtime)
    {
        private static readonly DateTime ProcessStarted = SafeStart();

        public static AppVersionInfo Current(string environment)
        {
            var assembly = typeof(AppVersionInfo).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
            var plus = informational.IndexOf('+');
            var version = plus >= 0 ? informational[..plus] : informational;
            var commit = plus >= 0 ? informational[(plus + 1)..] : null;
            if (commit is { Length: > 12 }) commit = commit[..12];

            DateTime? builtAt = null;
            try
            {
                if (!string.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location))
                    builtAt = File.GetLastWriteTimeUtc(assembly.Location);
            }
            catch (IOException) { /* not knowable: left out */ }
            catch (UnauthorizedAccessException) { }

            return new AppVersionInfo(version, string.IsNullOrWhiteSpace(commit) ? null : commit, builtAt, ProcessStarted, environment,
                System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        }

        private static DateTime SafeStart()
        {
            try { return Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
            catch (Exception) { return DateTime.UtcNow; }
        }
    }
}

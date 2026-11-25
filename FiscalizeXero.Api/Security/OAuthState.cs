namespace FiscalizeXero.Api.Security
{
    /// <summary>
    /// The OAuth <c>state</c> value. A version-7 GUID: as unguessable as before (74 random bits), and its first 48
    /// bits are the creation time, so the callback can refuse an old consent link without storing anything extra.
    /// </summary>
    public static class OAuthState
    {
        /// <summary>How long a consent link stays usable. Xero's own authorisation code lives five minutes.</summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

        public static Guid New() => Guid.CreateVersion7();

        /// <summary>When the state was created, or null for a state that is not version 7 (made before this change).</summary>
        public static DateTime? CreatedAt(Guid state)
        {
            if (state.Version != 7) return null;
            var bytes = state.ToByteArray(bigEndian: true);
            long ms = 0;
            for (var i = 0; i < 6; i++) ms = (ms << 8) | bytes[i];
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        }

        /// <summary>Created within <see cref="Lifetime"/> (with a minute's clock tolerance), and not in the future.</summary>
        public static bool IsFresh(Guid state, DateTime nowUtc)
        {
            var created = CreatedAt(state);
            return created.HasValue && created.Value <= nowUtc.AddMinutes(1) && nowUtc - created.Value <= Lifetime;
        }
    }
}

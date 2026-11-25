namespace FiscalizeXero.Shared.Models.Fiscalize
{
    public class DeviceLoginRequest
    {
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
    }

    public class DeviceLoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public long CompanyId { get; set; }
        public int DeviceID { get; set; }
        public long InternalDeviceId { get; set; }
        public string? CompanyTIN { get; set; }
        public string? CompanyName { get; set; }
        public string? Platform { get; set; }
    }

    public class DeviceRefreshRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }
}

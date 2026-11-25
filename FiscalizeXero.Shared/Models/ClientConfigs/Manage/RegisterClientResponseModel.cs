namespace FiscalizeXero.Shared.Models.ClientConfigs.Manage
{
    public class RegisterClientResponseModel
    {
        public long ClientConfigId { get; set; }
        public string AuthUrl { get; set; } = string.Empty;
    }
}

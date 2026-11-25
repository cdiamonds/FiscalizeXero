namespace FiscalizeXero.Shared.Models.ClientConfigs.Manage
{
    public class OAuthCallbackModel
    {
        public string State { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        //public string Location { get; set; } = string.Empty;
        //public string AccountsServer { get; set; } = string.Empty;
    }
}

namespace FiscalizeXero.Shared.Models.ClientConfigs.Manage
{
    /// <summary>
    /// Outcome of disconnecting a company from Xero. The connection record is always removed;
    /// anything that could not be cleaned up on Xero's side is listed in <see cref="Warnings"/>.
    /// </summary>
    public class DisconnectResultModel
    {
        public bool Disconnected { get; set; } = true;
        public List<string> Warnings { get; set; } = new();
    }
}

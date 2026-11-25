namespace FiscalizeXero.Shared.Models.ClientConfigs.Manage
{
    /// <summary>Who manages the company's Xero connection, for the settings screens.</summary>
    public class ConnectionManagementModel
    {
        /// <summary>The signed-in person may disconnect, change fiscalisation settings and device keys.</summary>
        public bool CanManage { get; set; }
        /// <summary>The person who connected Xero, when known.</summary>
        public string? ConnectedByName { get; set; }
        /// <summary>The signed-in person is the one who connected.</summary>
        public bool IsYou { get; set; }
    }
}

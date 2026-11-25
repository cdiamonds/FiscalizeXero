namespace FiscalizeXero.Shared.Models.Auths
{
    public class SessionModel
    {
        //public string UserId { get; set; }
        public long UserId { get; set; }
        public string UserName { get; set; }
        public bool EmailConfirmed { get; set; }
        public long? ActiveCompanyId { get; set; }
        public string? ActiveCompanyName { get; set; }
        public SessionDeviceModel? Device { get; set; }
    }

    public class SessionDeviceModel
    {
        public long InternalDeviceId { get; set; }
        public int DeviceID { get; set; }
    }
}

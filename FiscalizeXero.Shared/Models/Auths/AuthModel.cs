namespace FiscalizeXero.Shared.Models.Auths
{
    public class ExchangeLaunchTokenRequest
    {
        public string Token { get; set; } = null!;
    }

    public class AuthModel
    {
        public FiscalizeAuthModel Fiscalize { get; set; }
    }

    public class FiscalizeAuthModel
    {
        public string ApiUrl { get; set; }
        public string Token { get; set; }
    }
}

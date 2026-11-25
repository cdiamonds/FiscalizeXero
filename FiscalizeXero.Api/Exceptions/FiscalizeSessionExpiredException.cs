namespace FiscalizeXero.Api.Exceptions
{
    public class FiscalizeSessionExpiredException : Exception
    {
        public FiscalizeSessionExpiredException()
            : base("Your Fiscalize session has expired. Please log in to Fiscalize again.")
        {
        }

        public FiscalizeSessionExpiredException(string message)
            : base(message)
        {
        }
    }
}

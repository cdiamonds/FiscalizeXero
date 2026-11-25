namespace FiscalizeXero.Api.Exceptions
{
    /// <summary>
    /// The Fiscalize platform couldn't be reached (down, overloaded, network) while a session needed renewing. Not a
    /// timed-out session: the user is told to try again in a moment and is never asked to sign in for it.
    /// </summary>
    public class FiscalizePlatformUnavailableException : Exception
    {
        public FiscalizePlatformUnavailableException()
            : base("The Fiscalize platform isn't answering right now. Try again in a moment; you don't need to sign in again.") { }
    }
}

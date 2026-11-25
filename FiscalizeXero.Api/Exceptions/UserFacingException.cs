namespace FiscalizeXero.Api.Exceptions
{
    /// <summary>
    /// A problem explained in words written for the person using the app: its message is safe to show on screen.
    /// Anything else that goes wrong is logged and shown as a generic message, so framework or database text (which
    /// can reveal internals) never reaches the browser. Derives from InvalidOperationException so existing handling
    /// of "this can't be done" keeps working.
    /// </summary>
    public class UserFacingException : InvalidOperationException
    {
        public UserFacingException(string message) : base(message) { }
        public UserFacingException(string message, Exception inner) : base(message, inner) { }
    }
}

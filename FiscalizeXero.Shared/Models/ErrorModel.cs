namespace FiscalizeXero.Shared.Models
{
    public class ErrorModel
    {
        /// <summary>
        /// This is a code for the error. It can either be http code of special code for the api or third party system which would have generated the error
        /// </summary>
        public int Code { get; set; }
        /// <summary>
        /// Summary of the error. In case its model validation, details about all other errors will be in the errors object
        /// </summary>
        public string Message { get; set; }
        /// <summary>
        /// Detailed errors mostly applies to validation
        /// </summary>
        public List<string>? Errors { get; set; }
    }
}

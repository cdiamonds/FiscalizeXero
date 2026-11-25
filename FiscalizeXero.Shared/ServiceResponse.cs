using System.Net;

namespace FiscalizeXero.Shared
{
    public class ServiceResponse<T>
    {
        public T? Data { get; set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        //public bool IsSuccess { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public List<string>? Errors { get; set; }
        public bool LogError { get; set; } = false;
        public string LogMessage { get; set; }

        /// <summary>
        /// Returns a response based on another response with ability to add additional details
        /// </summary>
        /// <typeparam name="TIn"></typeparam>
        /// <param name="responseIn"></param>
        /// <param name="message"></param>
        /// <param name="errors"></param>
        /// <param name="logError"></param>
        /// <param name="logMessage"></param>
        /// <returns></returns>
        public static ServiceResponse<T> Transform<TIn>(
        ServiceResponse<TIn> responseIn,
        //T dataOut,
        string? message = null,
        List<string>? errors = null,
        bool? logError = null,
        string? logMessage = null)
        {
            return new ServiceResponse<T>
            {
                //Data = dataOut,
                Status = responseIn.Status,
                Message = message ?? responseIn.Message,
                Errors = errors ?? responseIn.Errors,
                LogError = logError ?? responseIn.LogError,
                LogMessage = logMessage ?? responseIn.LogMessage
            };
        }

        public static ServiceResponse<T> Success(
        T? dataOut,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? message = null)
        {
            return new ServiceResponse<T>
            {
                Data = dataOut,
                Status = (dataOut == null) ? HttpStatusCode.NoContent : statusCode,
                Message = message,
                Errors = new List<string>(),
                LogError = false,
                LogMessage = ""
            };
        }

        public static ServiceResponse<T> NotFound(string? message = null)
        {
            return new ServiceResponse<T>
            {
                //Data = null,
                Status = HttpStatusCode.NotFound,
                Message = string.IsNullOrEmpty(message) ? "Record not found!" : message,
                Errors = new List<string>(),
                LogError = false,
                LogMessage = ""
            };
        }

        /// <summary>
        /// Returns/ handles errors 
        /// </summary>
        /// <param name="ex"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public static ServiceResponse<T> Error(Exception ex, string message)
        {
            return new ServiceResponse<T>
            {
                //Data = default,
                Status = HttpStatusCode.InternalServerError,
                Message = message,
                Errors = new List<string> { $"Exception: {ex.Message}" },
                LogError = true,
                LogMessage = ex.StackTrace
            };
        }

        /// <summary>
        /// Returns a response where you would like to specify status and other details
        /// an example here are validations
        /// </summary>
        /// <param name="status"></param>
        /// <param name="message"></param>
        /// <param name="logErrorMessage"></param>
        /// <param name="logError"></param>
        /// <returns></returns>
        public static ServiceResponse<T> Error(HttpStatusCode status, string message, string logErrorMessage = "", bool logError = true)
        {
            return new ServiceResponse<T>
            {
                //Data = default,
                Status = status,
                Message = message,
                Errors = new List<string> { { message } },
                LogError = logError,
                LogMessage = string.IsNullOrEmpty(logErrorMessage) ? message : logErrorMessage
            };
        }
    }
}
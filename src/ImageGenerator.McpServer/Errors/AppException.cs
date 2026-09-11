namespace ImageGenerator.McpServer.Errors;

public sealed class AppException : Exception
{
    public AppErrorCode Code { get; }

    public HttpStatusCode? StatusCode { get; }

    public TimeSpan? RetryAfter { get; }

    public AppException(AppErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public AppException(AppErrorCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public AppException(AppErrorCode code, string message, HttpStatusCode statusCode, TimeSpan? retryAfter)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }
}

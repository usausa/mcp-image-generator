namespace ImageGenerator.McpServer.Errors;

public enum AppErrorCode
{
    InvalidParameter,
    InputNotFound,
    InputNotAllowed,
    OutputNotAllowed,
    OutputExists,
    GenerationRequestFailed,
    GenerationTimeout,
    GenerationNoData,
    ImageDecodeFailed,
    ImageTooLarge
}

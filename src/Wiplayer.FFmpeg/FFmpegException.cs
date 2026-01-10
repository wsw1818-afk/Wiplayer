namespace Wiplayer.FFmpeg;

/// <summary>
/// FFmpeg 관련 예외
/// </summary>
public class FFmpegException : Exception
{
    /// <summary>FFmpeg 오류 코드</summary>
    public int ErrorCode { get; }

    /// <summary>FFmpeg 오류 메시지</summary>
    public string? FFmpegError { get; }

    public FFmpegException(string message) : base(message)
    {
    }

    public FFmpegException(string message, int errorCode)
        : base($"{message} (FFmpeg error: {FFmpegSetup.GetErrorMessage(errorCode)})")
    {
        ErrorCode = errorCode;
        FFmpegError = FFmpegSetup.GetErrorMessage(errorCode);
    }

    public FFmpegException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

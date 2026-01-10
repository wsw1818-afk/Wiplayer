namespace Wiplayer.Core.Player;

/// <summary>
/// 미디어 플레이어 인터페이스
/// </summary>
public interface IMediaPlayer : IDisposable
{
    /// <summary>현재 플레이어 상태</summary>
    PlayerState State { get; }

    /// <summary>현재 재생 위치 (초)</summary>
    double Position { get; }

    /// <summary>미디어 총 길이 (초)</summary>
    double Duration { get; }

    /// <summary>볼륨 (0.0 ~ 1.0)</summary>
    double Volume { get; set; }

    /// <summary>음소거 여부</summary>
    bool IsMuted { get; set; }

    /// <summary>재생 속도 (0.2x ~ 4.0x)</summary>
    double PlaybackSpeed { get; set; }

    /// <summary>현재 로드된 미디어 정보</summary>
    MediaInfo? CurrentMedia { get; }

    /// <summary>상태 변경 이벤트</summary>
    event EventHandler<PlayerStateChangedEventArgs>? StateChanged;

    /// <summary>재생 위치 변경 이벤트</summary>
    event EventHandler<PositionChangedEventArgs>? PositionChanged;

    /// <summary>오류 발생 이벤트</summary>
    event EventHandler<PlayerErrorEventArgs>? ErrorOccurred;

    /// <summary>미디어 파일/URL 열기</summary>
    Task<bool> OpenAsync(string path);

    /// <summary>재생 시작</summary>
    void Play();

    /// <summary>일시정지</summary>
    void Pause();

    /// <summary>정지 (미디어 언로드)</summary>
    void Stop();

    /// <summary>특정 위치로 이동 (초)</summary>
    Task SeekAsync(double position);

    /// <summary>상대적 이동 (초, +/- 가능)</summary>
    Task SeekRelativeAsync(double offset);

    /// <summary>프레임 단위 이동</summary>
    void StepFrame(bool forward);
}

/// <summary>
/// 플레이어 상태 변경 이벤트 인자
/// </summary>
public class PlayerStateChangedEventArgs : EventArgs
{
    public PlayerState OldState { get; }
    public PlayerState NewState { get; }

    public PlayerStateChangedEventArgs(PlayerState oldState, PlayerState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}

/// <summary>
/// 재생 위치 변경 이벤트 인자
/// </summary>
public class PositionChangedEventArgs : EventArgs
{
    public double Position { get; }
    public double Duration { get; }

    public PositionChangedEventArgs(double position, double duration)
    {
        Position = position;
        Duration = duration;
    }
}

/// <summary>
/// 플레이어 오류 이벤트 인자
/// </summary>
public class PlayerErrorEventArgs : EventArgs
{
    public string Message { get; }
    public Exception? Exception { get; }
    public PlayerErrorType ErrorType { get; }

    public PlayerErrorEventArgs(string message, PlayerErrorType errorType, Exception? exception = null)
    {
        Message = message;
        ErrorType = errorType;
        Exception = exception;
    }
}

/// <summary>
/// 플레이어 오류 유형
/// </summary>
public enum PlayerErrorType
{
    FileNotFound,
    InvalidFormat,
    CodecNotSupported,
    NetworkError,
    DecodingError,
    RenderingError,
    Unknown
}

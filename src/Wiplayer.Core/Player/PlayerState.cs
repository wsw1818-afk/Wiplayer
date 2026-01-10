namespace Wiplayer.Core.Player;

/// <summary>
/// 플레이어 상태를 나타내는 열거형
/// </summary>
public enum PlayerState
{
    /// <summary>정지 상태 - 미디어가 로드되지 않음</summary>
    Stopped,

    /// <summary>로딩 중 - 미디어 파일을 열고 있음</summary>
    Loading,

    /// <summary>재생 중</summary>
    Playing,

    /// <summary>일시정지</summary>
    Paused,

    /// <summary>버퍼링 중 - 네트워크 스트림에서 데이터 대기</summary>
    Buffering,

    /// <summary>재생 완료</summary>
    Ended,

    /// <summary>오류 발생</summary>
    Error
}

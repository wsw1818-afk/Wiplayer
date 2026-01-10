using System.Windows.Media.Imaging;
using Wiplayer.Core.Player;
using Wiplayer.Subtitle;

namespace Wiplayer.Services;

/// <summary>
/// 플레이어 서비스 인터페이스
/// </summary>
public interface IPlayerService
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

    /// <summary>재생 속도</summary>
    double PlaybackSpeed { get; set; }

    /// <summary>현재 미디어 정보</summary>
    MediaInfo? CurrentMedia { get; }

    /// <summary>비디오 프레임 (바인딩용)</summary>
    WriteableBitmap? VideoFrame { get; }

    /// <summary>자막 관리자</summary>
    SubtitleManager SubtitleManager { get; }

    /// <summary>구간 반복</summary>
    ABRepeat ABRepeat { get; }

    /// <summary>상태 변경 이벤트</summary>
    event EventHandler<PlayerStateChangedEventArgs>? StateChanged;

    /// <summary>위치 변경 이벤트</summary>
    event EventHandler<PositionChangedEventArgs>? PositionChanged;

    /// <summary>오류 발생 이벤트</summary>
    event EventHandler<PlayerErrorEventArgs>? ErrorOccurred;

    /// <summary>프레임 렌더링 이벤트</summary>
    event EventHandler? FrameRendered;

    /// <summary>미디어 파일 열기</summary>
    Task<bool> OpenAsync(string path);

    /// <summary>재생</summary>
    void Play();

    /// <summary>일시정지</summary>
    void Pause();

    /// <summary>재생/일시정지 토글</summary>
    void TogglePlayPause();

    /// <summary>정지</summary>
    void Stop();

    /// <summary>처음으로 이동 후 일시정지 (멀티뷰 Stop용)</summary>
    void ResetToStart();

    /// <summary>시크</summary>
    Task SeekAsync(double position);

    /// <summary>상대 시크</summary>
    Task SeekRelativeAsync(double offset);

    /// <summary>프레임 이동</summary>
    void StepFrame(bool forward);

    /// <summary>자막 파일 로드</summary>
    bool LoadSubtitle(string path);

    /// <summary>오디오 트랙 변경</summary>
    void SetAudioTrack(int index);

    /// <summary>자막 트랙 변경</summary>
    void SetSubtitleTrack(int index);

    /// <summary>스크린샷 저장</summary>
    Task<string?> SaveScreenshotAsync(string? folderPath = null);

    /// <summary>화면 회전 각도 (0, 90, 180, 270)</summary>
    int RotationAngle { get; set; }

    /// <summary>밝기 (-100 ~ 100)</summary>
    int Brightness { get; set; }

    /// <summary>대비 (-100 ~ 100)</summary>
    int Contrast { get; set; }

    /// <summary>채도 (-100 ~ 100)</summary>
    int Saturation { get; set; }

    /// <summary>최근 재생 목록</summary>
    IReadOnlyList<Core.Playlist.PlaylistItem> RecentFiles { get; }

    /// <summary>최근 파일 열기</summary>
    void OpenRecentFile(string path);

    /// <summary>북마크 관리자</summary>
    Core.Playlist.Bookmarks Bookmarks { get; }

    /// <summary>현재 위치에 북마크 추가</summary>
    void AddBookmark(string? title = null);

    /// <summary>북마크로 이동</summary>
    Task GoToBookmark(Core.Playlist.BookmarkItem bookmark);
}

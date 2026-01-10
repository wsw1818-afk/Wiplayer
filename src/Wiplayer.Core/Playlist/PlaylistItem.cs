namespace Wiplayer.Core.Playlist;

/// <summary>
/// 재생목록 항목
/// </summary>
public class PlaylistItem
{
    /// <summary>고유 ID</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>파일 경로 또는 URL</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>표시 제목</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>재생 시간 (초)</summary>
    public double Duration { get; set; }

    /// <summary>마지막 재생 위치 (초)</summary>
    public double LastPosition { get; set; }

    /// <summary>마지막 재생 시각</summary>
    public DateTime? LastPlayedAt { get; set; }

    /// <summary>재생 횟수</summary>
    public int PlayCount { get; set; }

    /// <summary>추가된 시각</summary>
    public DateTime AddedAt { get; set; } = DateTime.Now;

    /// <summary>파일명 (경로에서 추출)</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>표시용 제목 (Title이 비어있으면 FileName 사용)</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? FileName : Title;

    /// <summary>파일 존재 여부</summary>
    public bool Exists => File.Exists(Path) || Path.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    /// <summary>이어보기 가능 여부 (10초 이상 시청, 끝까지 보지 않음)</summary>
    public bool CanResume => LastPosition > 10 && Duration > 0 && LastPosition < Duration - 30;

    /// <summary>시청 진행률 (0.0 ~ 1.0)</summary>
    public double Progress => Duration > 0 ? LastPosition / Duration : 0;

    /// <summary>재생 시간 포맷팅</summary>
    public string DurationText => FormatTime(Duration);

    /// <summary>마지막 위치 포맷팅</summary>
    public string LastPositionText => FormatTime(LastPosition);

    private static string FormatTime(double seconds)
    {
        if (seconds <= 0) return "--:--";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}

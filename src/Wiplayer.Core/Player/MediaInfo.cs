namespace Wiplayer.Core.Player;

/// <summary>
/// 미디어 파일 정보
/// </summary>
public class MediaInfo
{
    /// <summary>파일 경로 또는 URL</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>파일명</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>총 재생 시간 (초)</summary>
    public double Duration { get; set; }

    /// <summary>컨테이너 포맷 (mp4, mkv 등)</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>파일 크기 (바이트)</summary>
    public long FileSize { get; set; }

    /// <summary>전체 비트레이트 (bps)</summary>
    public long Bitrate { get; set; }

    /// <summary>비디오 스트림 정보 목록</summary>
    public List<VideoStreamInfo> VideoStreams { get; set; } = new();

    /// <summary>오디오 스트림 정보 목록</summary>
    public List<AudioStreamInfo> AudioStreams { get; set; } = new();

    /// <summary>자막 스트림 정보 목록</summary>
    public List<SubtitleStreamInfo> SubtitleStreams { get; set; } = new();

    /// <summary>챕터 정보 목록</summary>
    public List<ChapterInfo> Chapters { get; set; } = new();

    /// <summary>메타데이터 (제목, 아티스트 등)</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>네트워크 스트림 여부</summary>
    public bool IsNetworkStream => Path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                                   || Path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                                   || Path.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 비디오 스트림 정보
/// </summary>
public class VideoStreamInfo
{
    public int Index { get; set; }
    public string Codec { get; set; } = string.Empty;
    public string CodecLongName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; }
    public string PixelFormat { get; set; } = string.Empty;
    public long Bitrate { get; set; }
    public string Language { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    /// <summary>해상도 문자열 (예: "1920x1080")</summary>
    public string Resolution => $"{Width}x{Height}";

    /// <summary>화면비</summary>
    public double AspectRatio => Height > 0 ? (double)Width / Height : 0;
}

/// <summary>
/// 오디오 스트림 정보
/// </summary>
public class AudioStreamInfo
{
    public int Index { get; set; }
    public string Codec { get; set; } = string.Empty;
    public string CodecLongName { get; set; } = string.Empty;
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string ChannelLayout { get; set; } = string.Empty;
    public long Bitrate { get; set; }
    public string Language { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    /// <summary>채널 설명 (예: "스테레오", "5.1")</summary>
    public string ChannelDescription => Channels switch
    {
        1 => "모노",
        2 => "스테레오",
        6 => "5.1",
        8 => "7.1",
        _ => $"{Channels}ch"
    };
}

/// <summary>
/// 자막 스트림 정보
/// </summary>
public class SubtitleStreamInfo
{
    public int Index { get; set; }
    public string Codec { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }

    /// <summary>외부 자막 파일 경로 (내장 자막이면 null)</summary>
    public string? ExternalPath { get; set; }

    /// <summary>외부 자막 여부</summary>
    public bool IsExternal => !string.IsNullOrEmpty(ExternalPath);
}

/// <summary>
/// 챕터 정보
/// </summary>
public class ChapterInfo
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public double Duration => EndTime - StartTime;
}

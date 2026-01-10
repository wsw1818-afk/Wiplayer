namespace Wiplayer.Core.Utils;

/// <summary>
/// 미디어 파일 관련 유틸리티
/// </summary>
public static class MediaFileHelper
{
    /// <summary>지원하는 비디오 확장자</summary>
    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".flv", ".webm", ".m4v",
        ".mpg", ".mpeg", ".m2ts", ".mts", ".ts", ".vob", ".3gp", ".3g2",
        ".ogv", ".rm", ".rmvb", ".asf", ".divx", ".f4v"
    };

    /// <summary>지원하는 오디오 확장자</summary>
    public static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a", ".opus",
        ".ape", ".aiff", ".alac", ".dsd", ".dsf", ".dff", ".mka"
    };

    /// <summary>지원하는 자막 확장자</summary>
    public static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".sub", ".idx", ".smi", ".vtt", ".sup"
    };

    /// <summary>지원하는 재생목록 확장자</summary>
    public static readonly HashSet<string> PlaylistExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".m3u", ".m3u8", ".pls", ".wpl", ".xspf"
    };

    /// <summary>비디오 파일인지 확인</summary>
    public static bool IsVideoFile(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path));

    /// <summary>오디오 파일인지 확인</summary>
    public static bool IsAudioFile(string path) =>
        AudioExtensions.Contains(Path.GetExtension(path));

    /// <summary>미디어 파일인지 확인 (비디오 + 오디오)</summary>
    public static bool IsMediaFile(string path) =>
        IsVideoFile(path) || IsAudioFile(path);

    /// <summary>자막 파일인지 확인</summary>
    public static bool IsSubtitleFile(string path) =>
        SubtitleExtensions.Contains(Path.GetExtension(path));

    /// <summary>재생목록 파일인지 확인</summary>
    public static bool IsPlaylistFile(string path) =>
        PlaylistExtensions.Contains(Path.GetExtension(path));

    /// <summary>네트워크 스트림인지 확인</summary>
    public static bool IsNetworkStream(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase);

    /// <summary>동일 폴더에서 자막 파일 찾기</summary>
    public static IEnumerable<string> FindSubtitleFiles(string videoPath)
    {
        var dir = Path.GetDirectoryName(videoPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            yield break;

        var baseName = Path.GetFileNameWithoutExtension(videoPath);

        foreach (var file in Directory.EnumerateFiles(dir))
        {
            if (!IsSubtitleFile(file))
                continue;

            var subBaseName = Path.GetFileNameWithoutExtension(file);

            // 정확히 일치하거나 접두사가 일치
            if (subBaseName.Equals(baseName, StringComparison.OrdinalIgnoreCase) ||
                subBaseName.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase) ||
                subBaseName.StartsWith(baseName + "_", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    /// <summary>동일 폴더에서 미디어 파일 찾기</summary>
    public static IEnumerable<string> FindMediaFilesInFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            yield break;

        foreach (var file in Directory.EnumerateFiles(folderPath)
            .Where(IsMediaFile)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            yield return file;
        }
    }

    /// <summary>동일 폴더에서 다음 미디어 파일 찾기</summary>
    public static string? FindNextMediaFile(string currentPath)
    {
        var dir = Path.GetDirectoryName(currentPath);
        if (string.IsNullOrEmpty(dir))
            return null;

        var files = FindMediaFilesInFolder(dir).ToList();
        var index = files.FindIndex(f => f.Equals(currentPath, StringComparison.OrdinalIgnoreCase));

        return index >= 0 && index < files.Count - 1 ? files[index + 1] : null;
    }

    /// <summary>동일 폴더에서 이전 미디어 파일 찾기</summary>
    public static string? FindPreviousMediaFile(string currentPath)
    {
        var dir = Path.GetDirectoryName(currentPath);
        if (string.IsNullOrEmpty(dir))
            return null;

        var files = FindMediaFilesInFolder(dir).ToList();
        var index = files.FindIndex(f => f.Equals(currentPath, StringComparison.OrdinalIgnoreCase));

        return index > 0 ? files[index - 1] : null;
    }

    /// <summary>파일 열기 대화상자 필터 생성</summary>
    public static string GetOpenFileFilter()
    {
        var video = string.Join(";", VideoExtensions.Select(e => "*" + e));
        var audio = string.Join(";", AudioExtensions.Select(e => "*" + e));
        var all = video + ";" + audio;

        return $"미디어 파일|{all}|" +
               $"비디오 파일|{video}|" +
               $"오디오 파일|{audio}|" +
               "모든 파일|*.*";
    }

    /// <summary>자막 파일 열기 대화상자 필터 생성</summary>
    public static string GetSubtitleFileFilter()
    {
        var subtitles = string.Join(";", SubtitleExtensions.Select(e => "*" + e));
        return $"자막 파일|{subtitles}|모든 파일|*.*";
    }

    /// <summary>시간 포맷팅 (초 -> 문자열)</summary>
    public static string FormatDuration(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            return "--:--";

        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    /// <summary>파일 크기 포맷팅</summary>
    public static string FormatFileSize(long bytes)
    {
        if (bytes < 0) return "알 수 없음";

        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double size = bytes;

        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }

        return $"{size:0.##} {suffixes[i]}";
    }

    /// <summary>비트레이트 포맷팅</summary>
    public static string FormatBitrate(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0) return "알 수 없음";

        if (bitsPerSecond >= 1_000_000)
            return $"{bitsPerSecond / 1_000_000.0:0.#} Mbps";
        else
            return $"{bitsPerSecond / 1_000.0:0} Kbps";
    }
}

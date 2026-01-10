using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Wiplayer.Subtitle.Parsers;

/// <summary>
/// SRT 자막 파서
/// </summary>
public partial class SrtParser : ISubtitleParser
{
    // 타임코드 패턴: 00:00:00,000 --> 00:00:00,000
    [GeneratedRegex(@"(\d{1,2}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})[,.](\d{3})")]
    private static partial Regex TimeCodeRegex();

    // HTML 태그 제거 패턴
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    // ASS 스타일 태그 제거 패턴
    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex AssTagRegex();

    public bool CanParse(string filePath)
    {
        return filePath.EndsWith(".srt", StringComparison.OrdinalIgnoreCase);
    }

    public List<SubtitleEntry> Parse(string filePath)
    {
        // 인코딩 자동 감지
        var encoding = DetectEncoding(filePath);
        var content = File.ReadAllText(filePath, encoding);

        return ParseContent(content);
    }

    public List<SubtitleEntry> ParseContent(string content)
    {
        var entries = new List<SubtitleEntry>();
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        int i = 0;
        while (i < lines.Length)
        {
            // 빈 줄 건너뛰기
            while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
                i++;

            if (i >= lines.Length)
                break;

            // 인덱스 번호 (무시)
            if (int.TryParse(lines[i].Trim(), out _))
                i++;

            if (i >= lines.Length)
                break;

            // 타임코드
            var timeMatch = TimeCodeRegex().Match(lines[i]);
            if (!timeMatch.Success)
            {
                i++;
                continue;
            }

            var startTime = ParseTime(
                int.Parse(timeMatch.Groups[1].Value),
                int.Parse(timeMatch.Groups[2].Value),
                int.Parse(timeMatch.Groups[3].Value),
                int.Parse(timeMatch.Groups[4].Value));

            var endTime = ParseTime(
                int.Parse(timeMatch.Groups[5].Value),
                int.Parse(timeMatch.Groups[6].Value),
                int.Parse(timeMatch.Groups[7].Value),
                int.Parse(timeMatch.Groups[8].Value));

            i++;

            // 자막 텍스트 (빈 줄이 나올 때까지)
            var textBuilder = new StringBuilder();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
            {
                if (textBuilder.Length > 0)
                    textBuilder.AppendLine();
                textBuilder.Append(lines[i]);
                i++;
            }

            var rawText = textBuilder.ToString();
            var cleanText = CleanText(rawText);

            if (!string.IsNullOrWhiteSpace(cleanText))
            {
                entries.Add(new SubtitleEntry
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    RawText = rawText,
                    Text = cleanText
                });
            }
        }

        return entries.OrderBy(e => e.StartTime).ToList();
    }

    private static double ParseTime(int hours, int minutes, int seconds, int milliseconds)
    {
        return hours * 3600 + minutes * 60 + seconds + milliseconds / 1000.0;
    }

    private static string CleanText(string text)
    {
        // HTML 태그 제거
        text = HtmlTagRegex().Replace(text, "");

        // ASS 스타일 태그 제거
        text = AssTagRegex().Replace(text, "");

        return text.Trim();
    }

    private static Encoding DetectEncoding(string filePath)
    {
        // BOM 확인
        var bom = new byte[4];
        using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            file.Read(bom, 0, 4);
        }

        // UTF-8 BOM
        if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return Encoding.UTF8;

        // UTF-16 LE BOM
        if (bom[0] == 0xFF && bom[1] == 0xFE)
            return Encoding.Unicode;

        // UTF-16 BE BOM
        if (bom[0] == 0xFE && bom[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        // UTF-32 BOM
        if (bom[0] == 0 && bom[1] == 0 && bom[2] == 0xFE && bom[3] == 0xFF)
            return Encoding.UTF32;

        // BOM이 없으면 파일 내용으로 추측
        var bytes = File.ReadAllBytes(filePath);

        // UTF-8 여부 확인
        if (IsValidUtf8(bytes))
            return Encoding.UTF8;

        // 한국어 인코딩 (EUC-KR)
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(51949); // EUC-KR
        }
        catch
        {
            return Encoding.Default;
        }
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        int i = 0;
        while (i < bytes.Length)
        {
            if (bytes[i] <= 0x7F) // ASCII
            {
                i++;
            }
            else if (bytes[i] >= 0xC2 && bytes[i] <= 0xDF) // 2-byte
            {
                if (i + 1 >= bytes.Length) return false;
                if (bytes[i + 1] < 0x80 || bytes[i + 1] > 0xBF) return false;
                i += 2;
            }
            else if (bytes[i] >= 0xE0 && bytes[i] <= 0xEF) // 3-byte
            {
                if (i + 2 >= bytes.Length) return false;
                if (bytes[i + 1] < 0x80 || bytes[i + 1] > 0xBF) return false;
                if (bytes[i + 2] < 0x80 || bytes[i + 2] > 0xBF) return false;
                i += 3;
            }
            else if (bytes[i] >= 0xF0 && bytes[i] <= 0xF4) // 4-byte
            {
                if (i + 3 >= bytes.Length) return false;
                if (bytes[i + 1] < 0x80 || bytes[i + 1] > 0xBF) return false;
                if (bytes[i + 2] < 0x80 || bytes[i + 2] > 0xBF) return false;
                if (bytes[i + 3] < 0x80 || bytes[i + 3] > 0xBF) return false;
                i += 4;
            }
            else
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// 자막 파서 인터페이스
/// </summary>
public interface ISubtitleParser
{
    /// <summary>이 파서가 해당 파일을 처리할 수 있는지</summary>
    bool CanParse(string filePath);

    /// <summary>파일 파싱</summary>
    List<SubtitleEntry> Parse(string filePath);

    /// <summary>문자열 파싱</summary>
    List<SubtitleEntry> ParseContent(string content);
}

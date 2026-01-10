using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Wiplayer.Subtitle.Parsers;

/// <summary>
/// ASS/SSA 자막 파서
/// </summary>
public partial class AssParser : ISubtitleParser
{
    private readonly Dictionary<string, SubtitleStyle> _styles = new();

    // ASS 태그 제거 패턴
    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex AssTagRegex();

    // 이스케이프된 개행
    [GeneratedRegex(@"\\[Nn]")]
    private static partial Regex NewlineRegex();

    public bool CanParse(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".ass" or ".ssa";
    }

    public List<SubtitleEntry> Parse(string filePath)
    {
        var encoding = DetectEncoding(filePath);
        var content = File.ReadAllText(filePath, encoding);
        return ParseContent(content);
    }

    public List<SubtitleEntry> ParseContent(string content)
    {
        _styles.Clear();
        var entries = new List<SubtitleEntry>();
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        string currentSection = "";

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // 섹션 헤더
            if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                currentSection = trimmedLine.ToLowerInvariant();
                continue;
            }

            // 스타일 정의
            if (currentSection == "[v4+ styles]" || currentSection == "[v4 styles]")
            {
                if (trimmedLine.StartsWith("Style:", StringComparison.OrdinalIgnoreCase))
                {
                    ParseStyle(trimmedLine.Substring(6).Trim());
                }
            }

            // 이벤트 (대화)
            if (currentSection == "[events]")
            {
                if (trimmedLine.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                {
                    var entry = ParseDialogue(trimmedLine.Substring(9).Trim());
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
            }
        }

        return entries.OrderBy(e => e.StartTime).ToList();
    }

    private void ParseStyle(string styleLine)
    {
        var parts = SplitAssLine(styleLine);
        if (parts.Length < 3)
            return;

        var style = new SubtitleStyle
        {
            Name = parts[0]
        };

        if (parts.Length > 1) style.FontName = parts[1];
        if (parts.Length > 2 && double.TryParse(parts[2], out var fontSize)) style.FontSize = fontSize;
        if (parts.Length > 3) style.PrimaryColor = ParseAssColor(parts[3]);
        if (parts.Length > 4) style.SecondaryColor = ParseAssColor(parts[4]);
        if (parts.Length > 5) style.OutlineColor = ParseAssColor(parts[5]);
        if (parts.Length > 6) style.BackColor = ParseAssColor(parts[6]);
        if (parts.Length > 7) style.Bold = parts[7] == "-1" || parts[7] == "1";
        if (parts.Length > 8) style.Italic = parts[8] == "-1" || parts[8] == "1";
        if (parts.Length > 9) style.Underline = parts[9] == "-1" || parts[9] == "1";
        if (parts.Length > 10) style.StrikeOut = parts[10] == "-1" || parts[10] == "1";
        if (parts.Length > 16 && double.TryParse(parts[16], out var outline)) style.OutlineWidth = outline;
        if (parts.Length > 17 && double.TryParse(parts[17], out var shadow)) style.ShadowDepth = shadow;
        if (parts.Length > 18 && int.TryParse(parts[18], out var align)) style.Alignment = align;
        if (parts.Length > 19 && int.TryParse(parts[19], out var marginL)) style.MarginL = marginL;
        if (parts.Length > 20 && int.TryParse(parts[20], out var marginR)) style.MarginR = marginR;
        if (parts.Length > 21 && int.TryParse(parts[21], out var marginV)) style.MarginV = marginV;

        _styles[style.Name] = style;
    }

    private SubtitleEntry? ParseDialogue(string dialogueLine)
    {
        var parts = SplitAssLine(dialogueLine, 10); // 최대 10개 필드 (마지막은 텍스트)
        if (parts.Length < 10)
            return null;

        // Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        var entry = new SubtitleEntry();

        if (int.TryParse(parts[0], out var layer))
            entry.Layer = layer;

        entry.StartTime = ParseAssTime(parts[1]);
        entry.EndTime = ParseAssTime(parts[2]);

        var styleName = parts[3];
        if (_styles.TryGetValue(styleName, out var style))
        {
            entry.Style = style;
        }

        // 마지막 필드가 텍스트 (콤마 포함 가능)
        entry.RawText = parts[9];
        entry.Text = CleanText(parts[9]);

        return string.IsNullOrWhiteSpace(entry.Text) ? null : entry;
    }

    private static string[] SplitAssLine(string line, int maxParts = -1)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        int count = 0;

        foreach (var c in line)
        {
            if (c == ',' && (maxParts < 0 || count < maxParts - 1))
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
                count++;
            }
            else
            {
                current.Append(c);
            }
        }

        parts.Add(current.ToString().Trim());
        return parts.ToArray();
    }

    private static double ParseAssTime(string timeStr)
    {
        // 형식: H:MM:SS.CC (centiseconds)
        var parts = timeStr.Split(':');
        if (parts.Length != 3)
            return 0;

        if (!int.TryParse(parts[0], out var hours))
            return 0;

        if (!int.TryParse(parts[1], out var minutes))
            return 0;

        var secParts = parts[2].Split('.');
        if (secParts.Length != 2)
            return 0;

        if (!int.TryParse(secParts[0], out var seconds))
            return 0;

        if (!int.TryParse(secParts[1], out var centiseconds))
            return 0;

        return hours * 3600 + minutes * 60 + seconds + centiseconds / 100.0;
    }

    private static uint ParseAssColor(string colorStr)
    {
        // ASS 색상 형식: &HAABBGGRR 또는 &HBBGGRR
        colorStr = colorStr.TrimStart('&', 'H', 'h');

        if (uint.TryParse(colorStr, System.Globalization.NumberStyles.HexNumber, null, out var color))
        {
            return color;
        }

        return 0xFFFFFFFF;
    }

    private static string CleanText(string text)
    {
        // ASS 태그 제거
        text = AssTagRegex().Replace(text, "");

        // 이스케이프된 개행 처리
        text = NewlineRegex().Replace(text, "\n");

        return text.Trim();
    }

    private static Encoding DetectEncoding(string filePath)
    {
        // SrtParser와 동일한 인코딩 감지 로직
        var bom = new byte[4];
        using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            file.Read(bom, 0, 4);
        }

        if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return Encoding.UTF8;

        if (bom[0] == 0xFF && bom[1] == 0xFE)
            return Encoding.Unicode;

        if (bom[0] == 0xFE && bom[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        return Encoding.UTF8;
    }
}

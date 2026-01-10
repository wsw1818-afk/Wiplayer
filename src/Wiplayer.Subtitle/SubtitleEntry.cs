namespace Wiplayer.Subtitle;

/// <summary>
/// 자막 항목
/// </summary>
public class SubtitleEntry
{
    /// <summary>시작 시간 (초)</summary>
    public double StartTime { get; set; }

    /// <summary>종료 시간 (초)</summary>
    public double EndTime { get; set; }

    /// <summary>자막 텍스트 (태그 제거 전)</summary>
    public string RawText { get; set; } = string.Empty;

    /// <summary>자막 텍스트 (태그 제거 후)</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>스타일 정보 (ASS/SSA용)</summary>
    public SubtitleStyle? Style { get; set; }

    /// <summary>레이어 (겹치는 자막의 순서)</summary>
    public int Layer { get; set; }

    /// <summary>표시 시간 (초)</summary>
    public double Duration => EndTime - StartTime;

    /// <summary>특정 시간에 표시해야 하는지</summary>
    public bool IsActiveAt(double time) => time >= StartTime && time <= EndTime;

    /// <summary>시작/종료 시간 포맷팅</summary>
    public string TimeRange => $"{FormatTime(StartTime)} --> {FormatTime(EndTime)}";

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }
}

/// <summary>
/// 자막 스타일 (ASS/SSA용)
/// </summary>
public class SubtitleStyle
{
    /// <summary>스타일 이름</summary>
    public string Name { get; set; } = "Default";

    /// <summary>폰트 이름</summary>
    public string FontName { get; set; } = "맑은 고딕";

    /// <summary>폰트 크기</summary>
    public double FontSize { get; set; } = 48;

    /// <summary>기본 색상 (ABGR)</summary>
    public uint PrimaryColor { get; set; } = 0x00FFFFFF;

    /// <summary>보조 색상</summary>
    public uint SecondaryColor { get; set; } = 0x0000FFFF;

    /// <summary>테두리 색상</summary>
    public uint OutlineColor { get; set; } = 0x00000000;

    /// <summary>그림자 색상</summary>
    public uint BackColor { get; set; } = 0x80000000;

    /// <summary>굵게</summary>
    public bool Bold { get; set; }

    /// <summary>이탤릭</summary>
    public bool Italic { get; set; }

    /// <summary>밑줄</summary>
    public bool Underline { get; set; }

    /// <summary>취소선</summary>
    public bool StrikeOut { get; set; }

    /// <summary>테두리 두께</summary>
    public double OutlineWidth { get; set; } = 2;

    /// <summary>그림자 거리</summary>
    public double ShadowDepth { get; set; } = 2;

    /// <summary>정렬 (1-9, 숫자패드 기준)</summary>
    public int Alignment { get; set; } = 2; // 하단 중앙

    /// <summary>여백 (왼쪽)</summary>
    public int MarginL { get; set; } = 10;

    /// <summary>여백 (오른쪽)</summary>
    public int MarginR { get; set; } = 10;

    /// <summary>여백 (세로)</summary>
    public int MarginV { get; set; } = 10;
}

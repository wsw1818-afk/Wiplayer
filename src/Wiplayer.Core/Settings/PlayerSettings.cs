using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wiplayer.Core.Settings;

/// <summary>
/// 플레이어 전체 설정
/// </summary>
public class PlayerSettings
{
    /// <summary>일반 설정</summary>
    public GeneralSettings General { get; set; } = new();

    /// <summary>재생 설정</summary>
    public PlaybackSettings Playback { get; set; } = new();

    /// <summary>자막 설정</summary>
    public SubtitleSettings Subtitle { get; set; } = new();

    /// <summary>영상 설정</summary>
    public VideoSettings Video { get; set; } = new();

    /// <summary>오디오 설정</summary>
    public AudioSettings Audio { get; set; } = new();

    /// <summary>단축키 설정</summary>
    public KeyboardSettings Keyboard { get; set; } = new();

    /// <summary>스크린샷 설정</summary>
    public ScreenshotSettings Screenshot { get; set; } = new();

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Wiplayer", "settings.json");

    /// <summary>설정 저장</summary>
    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(SettingsPath, json);
    }

    /// <summary>설정 로드</summary>
    public static PlayerSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                return JsonSerializer.Deserialize<PlayerSettings>(json, options) ?? new PlayerSettings();
            }
        }
        catch
        {
            // 설정 파일 손상 시 기본값 사용
        }

        return new PlayerSettings();
    }
}

/// <summary>일반 설정</summary>
public class GeneralSettings
{
    /// <summary>언어 (ko-KR, en-US 등)</summary>
    public string Language { get; set; } = "ko-KR";

    /// <summary>항상 위에 표시</summary>
    public bool AlwaysOnTop { get; set; } = false;

    /// <summary>시스템 트레이로 최소화</summary>
    public bool MinimizeToTray { get; set; } = false;

    /// <summary>최근 재생 기록 저장 개수</summary>
    public int RecentFilesCount { get; set; } = 20;

    /// <summary>자동 업데이트 확인</summary>
    public bool AutoCheckUpdate { get; set; } = true;
}

/// <summary>재생 설정</summary>
public class PlaybackSettings
{
    /// <summary>마지막 위치에서 이어보기</summary>
    public bool ResumePlayback { get; set; } = true;

    /// <summary>자동으로 다음 파일 재생</summary>
    public bool AutoPlayNext { get; set; } = true;

    /// <summary>재생 완료 후 동작</summary>
    public EndAction EndAction { get; set; } = EndAction.Stop;

    /// <summary>기본 재생 속도</summary>
    public double DefaultSpeed { get; set; } = 1.0;

    /// <summary>짧은 점프 시간 (초)</summary>
    public double ShortJumpSeconds { get; set; } = 5;

    /// <summary>긴 점프 시간 (초)</summary>
    public double LongJumpSeconds { get; set; } = 30;

    /// <summary>하드웨어 가속 사용</summary>
    public bool UseHardwareAcceleration { get; set; } = true;

    /// <summary>선호 하드웨어 가속 방식</summary>
    public HwAccelType PreferredHwAccel { get; set; } = HwAccelType.Auto;
}

/// <summary>재생 완료 후 동작</summary>
public enum EndAction
{
    Stop,           // 정지
    Repeat,         // 반복 재생
    PlayNext,       // 다음 파일
    Exit            // 프로그램 종료
}

/// <summary>하드웨어 가속 유형</summary>
public enum HwAccelType
{
    Auto,           // 자동 선택
    D3D11VA,        // DirectX 11
    DXVA2,          // DirectX 9
    NVDEC,          // NVIDIA
    QSV,            // Intel Quick Sync
    AMF,            // AMD
    None            // 소프트웨어 디코딩
}

/// <summary>자막 설정</summary>
public class SubtitleSettings
{
    /// <summary>자막 자동 로드</summary>
    public bool AutoLoad { get; set; } = true;

    /// <summary>기본 폰트</summary>
    public string FontFamily { get; set; } = "맑은 고딕";

    /// <summary>폰트 크기</summary>
    public double FontSize { get; set; } = 48;

    /// <summary>폰트 색상 (ARGB)</summary>
    public uint FontColor { get; set; } = 0xFFFFFFFF;

    /// <summary>테두리 색상</summary>
    public uint OutlineColor { get; set; } = 0xFF000000;

    /// <summary>테두리 두께</summary>
    public double OutlineWidth { get; set; } = 2;

    /// <summary>그림자 사용</summary>
    public bool UseShadow { get; set; } = true;

    /// <summary>기본 싱크 조절 (밀리초)</summary>
    public int DefaultSyncOffset { get; set; } = 0;

    /// <summary>자막 위치 (0.0=상단, 1.0=하단)</summary>
    public double VerticalPosition { get; set; } = 0.9;

    /// <summary>선호 자막 언어</summary>
    public List<string> PreferredLanguages { get; set; } = new() { "ko", "kor", "korean", "en", "eng" };
}

/// <summary>영상 설정</summary>
public class VideoSettings
{
    /// <summary>밝기 (-100 ~ 100)</summary>
    public int Brightness { get; set; } = 0;

    /// <summary>대비 (-100 ~ 100)</summary>
    public int Contrast { get; set; } = 0;

    /// <summary>채도 (-100 ~ 100)</summary>
    public int Saturation { get; set; } = 0;

    /// <summary>샤프닝 (0 ~ 100)</summary>
    public int Sharpness { get; set; } = 0;

    /// <summary>화면비 유지</summary>
    public bool KeepAspectRatio { get; set; } = true;

    /// <summary>기본 화면비 (0=원본)</summary>
    public double DefaultAspectRatio { get; set; } = 0;

    /// <summary>렌더러 종류</summary>
    public VideoRendererType Renderer { get; set; } = VideoRendererType.D3D11;

    /// <summary>스케일링 품질 (최고 품질 기본)</summary>
    public VideoScalingQuality ScalingQuality { get; set; } = VideoScalingQuality.Highest;

    /// <summary>디인터레이싱 사용</summary>
    public bool UseDeinterlacing { get; set; } = true;
}

/// <summary>비디오 렌더러 유형</summary>
public enum VideoRendererType
{
    Auto,
    D3D11,
    D3D9,
    OpenGL
}

/// <summary>비디오 스케일링 품질</summary>
public enum VideoScalingQuality
{
    Fast,       // 빠름 (Bilinear)
    Balanced,   // 균형 (Bicubic)
    High,       // 높음 (Spline)
    Highest     // 최고 (Lanczos)
}

/// <summary>오디오 설정</summary>
public class AudioSettings
{
    /// <summary>기본 볼륨 (0 ~ 100)</summary>
    public int DefaultVolume { get; set; } = 100;

    /// <summary>음소거 상태</summary>
    public bool IsMuted { get; set; } = false;

    /// <summary>볼륨 증폭 허용 (100% 이상)</summary>
    public bool AllowVolumeBoost { get; set; } = false;

    /// <summary>최대 볼륨 (%)</summary>
    public int MaxVolume { get; set; } = 200;

    /// <summary>오디오 정규화</summary>
    public bool EnableNormalization { get; set; } = false;

    /// <summary>선호 오디오 언어</summary>
    public List<string> PreferredLanguages { get; set; } = new() { "ko", "kor", "korean" };

    /// <summary>출력 장치 ID (비어있으면 기본)</summary>
    public string OutputDeviceId { get; set; } = string.Empty;

    /// <summary>WASAPI 배타 모드 사용 (최고 음질, 다른 앱 소리 차단)</summary>
    public bool UseExclusiveMode { get; set; } = false;

    /// <summary>오디오 출력 품질</summary>
    public AudioQuality Quality { get; set; } = AudioQuality.High;

    /// <summary>출력 샘플레이트 (Hz)</summary>
    public int OutputSampleRate { get; set; } = 48000;
}

/// <summary>오디오 품질</summary>
public enum AudioQuality
{
    Standard,   // 표준 (44.1kHz, 16bit)
    High,       // 높음 (48kHz, 32bit float)
    Highest     // 최고 (96kHz, 32bit float)
}

/// <summary>스크린샷 설정</summary>
public class ScreenshotSettings
{
    /// <summary>스크린샷 저장 경로 (빈 경우 기본 경로)</summary>
    public string SavePath { get; set; } = string.Empty;

    /// <summary>스크린샷 포맷</summary>
    public ScreenshotFormat Format { get; set; } = ScreenshotFormat.Png;

    /// <summary>JPEG 품질 (0-100)</summary>
    public int JpegQuality { get; set; } = 95;

    /// <summary>스크린샷 후 알림 표시</summary>
    public bool ShowNotification { get; set; } = true;
}

/// <summary>스크린샷 포맷</summary>
public enum ScreenshotFormat
{
    Png,
    Jpeg,
    Bmp
}

/// <summary>키보드 단축키 설정</summary>
public class KeyboardSettings
{
    /// <summary>단축키 맵핑 (명령 -> 키 조합)</summary>
    public Dictionary<string, string> Bindings { get; set; } = GetDefaultBindings();

    private static Dictionary<string, string> GetDefaultBindings() => new()
    {
        // 재생 컨트롤
        ["PlayPause"] = "Space",
        ["Stop"] = "S",
        ["SeekForward5"] = "Right",
        ["SeekBackward5"] = "Left",
        ["SeekForward30"] = "Ctrl+Right",
        ["SeekBackward30"] = "Ctrl+Left",
        ["NextFrame"] = ".",
        ["PrevFrame"] = ",",

        // 볼륨
        ["VolumeUp"] = "Up",
        ["VolumeDown"] = "Down",
        ["Mute"] = "M",

        // 배속
        ["SpeedUp"] = "]",
        ["SpeedDown"] = "[",
        ["SpeedReset"] = "Backspace",

        // 자막
        ["SubtitleToggle"] = "V",
        ["SubtitleSyncPlus"] = "Ctrl+]",
        ["SubtitleSyncMinus"] = "Ctrl+[",

        // 화면
        ["Fullscreen"] = "F",
        ["Fullscreen2"] = "Enter",
        ["Escape"] = "Escape",

        // 파일
        ["OpenFile"] = "Ctrl+O",
        ["OpenFolder"] = "Ctrl+Shift+O",

        // 기타
        ["ABRepeat"] = "R",
        ["Screenshot"] = "Ctrl+S",
        ["Playlist"] = "P"
    };
}

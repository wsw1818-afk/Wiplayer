using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Wiplayer.FFmpeg;

/// <summary>
/// FFmpeg 라이브러리 초기화 및 설정
/// </summary>
public static class FFmpegSetup
{
    private static bool _initialized = false;
    private static readonly object _lock = new();

    /// <summary>FFmpeg 초기화 여부</summary>
    public static bool IsInitialized => _initialized;

    /// <summary>FFmpeg 버전 정보</summary>
    public static string? Version { get; private set; }

    /// <summary>
    /// FFmpeg 라이브러리 초기화
    /// </summary>
    /// <param name="ffmpegPath">FFmpeg 바이너리 경로 (null이면 기본 경로 사용)</param>
    public static void Initialize(string? ffmpegPath = null)
    {
        lock (_lock)
        {
            if (_initialized)
                return;

            // FFmpeg 바이너리 경로 설정
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                // 기본 경로: 실행 파일 옆의 ffmpeg 폴더
                var exeDir = AppContext.BaseDirectory;
                ffmpegPath = Path.Combine(exeDir, "ffmpeg");

                // 없으면 현재 디렉토리
                if (!Directory.Exists(ffmpegPath))
                    ffmpegPath = exeDir;
            }

            // FFmpeg 라이브러리 경로 설정
            ffmpeg.RootPath = ffmpegPath;

            // 로그 레벨 설정
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_WARNING);

            // 네트워크 초기화 (스트리밍 지원)
            ffmpeg.avformat_network_init();

            // 버전 정보 저장
            var codecVersion = ffmpeg.avcodec_version();
            Version = $"libavcodec {codecVersion >> 16}.{(codecVersion >> 8) & 0xFF}.{codecVersion & 0xFF}";

            _initialized = true;
        }
    }

    /// <summary>
    /// FFmpeg 종료 정리
    /// </summary>
    public static void Cleanup()
    {
        lock (_lock)
        {
            if (!_initialized)
                return;

            ffmpeg.avformat_network_deinit();
            _initialized = false;
        }
    }

    /// <summary>
    /// FFmpeg 에러 코드를 메시지로 변환
    /// </summary>
    public static unsafe string GetErrorMessage(int errorCode)
    {
        var bufferSize = 1024;
        var buffer = stackalloc byte[bufferSize];
        ffmpeg.av_strerror(errorCode, buffer, (ulong)bufferSize);
        return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"Unknown error {errorCode}";
    }

    /// <summary>
    /// 지원되는 하드웨어 가속 목록 가져오기
    /// </summary>
    public static IEnumerable<string> GetSupportedHwAccels()
    {
        var type = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

        while ((type = ffmpeg.av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            yield return ffmpeg.av_hwdevice_get_type_name(type);
        }
    }

    /// <summary>
    /// 코덱이 하드웨어 가속을 지원하는지 확인
    /// </summary>
    public static unsafe bool SupportsHwAccel(string codecName, AVHWDeviceType hwType)
    {
        var codec = ffmpeg.avcodec_find_decoder_by_name(codecName);
        if (codec == null)
            return false;

        for (int i = 0; ; i++)
        {
            var config = ffmpeg.avcodec_get_hw_config(codec, i);
            if (config == null)
                break;

            if (config->device_type == hwType)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 주요 비디오 코덱 지원 상태 확인
    /// </summary>
    public static unsafe Dictionary<string, bool> GetSupportedVideoCodecs()
    {
        var codecs = new Dictionary<string, bool>();
        var codecNames = new[]
        {
            // 일반 비디오 코덱
            "h264", "hevc", "vp8", "vp9", "av1",
            "mpeg4", "mpeg2video", "mpeg1video",
            "wmv3", "vc1", "theora", "mjpeg",
            "prores", "dnxhd", "huffyuv", "ffv1",
            // 애니메이션/특수
            "gif", "webp", "png", "bmp"
        };

        foreach (var name in codecNames)
        {
            var codec = ffmpeg.avcodec_find_decoder_by_name(name);
            codecs[name] = codec != null;
        }

        return codecs;
    }

    /// <summary>
    /// 주요 오디오 코덱 지원 상태 확인
    /// </summary>
    public static unsafe Dictionary<string, bool> GetSupportedAudioCodecs()
    {
        var codecs = new Dictionary<string, bool>();
        var codecNames = new[]
        {
            "aac", "mp3", "ac3", "eac3", "dts",
            "vorbis", "opus", "flac", "alac", "pcm_s16le",
            "wmav2", "amrnb", "amrwb", "truehd"
        };

        foreach (var name in codecNames)
        {
            var codec = ffmpeg.avcodec_find_decoder_by_name(name);
            codecs[name] = codec != null;
        }

        return codecs;
    }

    /// <summary>
    /// 지원 코덱을 로그 메시지로 반환
    /// </summary>
    public static string GetSupportedCodecsLog()
    {
        var videoCodecs = GetSupportedVideoCodecs();
        var audioCodecs = GetSupportedAudioCodecs();

        var supportedVideo = videoCodecs.Where(c => c.Value).Select(c => c.Key);
        var supportedAudio = audioCodecs.Where(c => c.Value).Select(c => c.Key);

        return $"Video: {string.Join(", ", supportedVideo)}\nAudio: {string.Join(", ", supportedAudio)}";
    }
}

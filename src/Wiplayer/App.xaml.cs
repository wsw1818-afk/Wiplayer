using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Wiplayer.Core.Settings;
using Wiplayer.FFmpeg;
using Wiplayer.Services;
using Wiplayer.ViewModels;
using Wiplayer.Views;

namespace Wiplayer;

/// <summary>
/// 애플리케이션 진입점
/// </summary>
public partial class App : Application
{
    private static IServiceProvider? _serviceProvider;

    /// <summary>서비스 프로바이더</summary>
    public static IServiceProvider ServiceProvider => _serviceProvider ?? throw new InvalidOperationException("ServiceProvider not initialized");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 로거 초기화
        InitializeLogging();

        // FFmpeg 초기화
        InitializeFFmpeg();

        // DI 컨테이너 설정
        ConfigureServices();

        // 명령줄 인수 처리
        if (e.Args.Length > 0)
        {
            var playerService = ServiceProvider.GetRequiredService<IPlayerService>();
            _ = OpenAndPlayAsync(playerService, e.Args[0]);
        }
    }

    private static async Task OpenAndPlayAsync(IPlayerService playerService, string path)
    {
        if (await playerService.OpenAsync(path))
        {
            playerService.Play();

            // ViewModel 상태 동기화 (커맨드라인으로 파일 열 때 StateChanged 이벤트 누락 방지)
            await Task.Delay(100); // UI 초기화 대기
            Current.Dispatcher.Invoke(() =>
            {
                var viewModel = ServiceProvider.GetService<MainViewModel>();
                viewModel?.SyncState();
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Wiplayer shutting down");

        // 설정 저장
        var settings = ServiceProvider.GetService<PlayerSettings>();
        settings?.Save();

        // FFmpeg 정리
        FFmpegSetup.Cleanup();

        Log.CloseAndFlush();

        base.OnExit(e);
    }

    private static void InitializeLogging()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Wiplayer", "logs", "wiplayer-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console()
            .CreateLogger();
    }

    private static void InitializeFFmpeg()
    {
        try
        {
            FFmpegSetup.Initialize();
            Log.Information("FFmpeg initialized: {Version}", FFmpegSetup.Version);

            // 지원되는 하드웨어 가속 로그
            var hwAccels = FFmpegSetup.GetSupportedHwAccels().ToList();
            if (hwAccels.Count > 0)
            {
                Log.Information("Supported HW accelerations: {HwAccels}", string.Join(", ", hwAccels));
            }

            // 지원되는 코덱 로그
            var videoCodecs = FFmpegSetup.GetSupportedVideoCodecs();
            var audioCodecs = FFmpegSetup.GetSupportedAudioCodecs();
            var supportedVideo = videoCodecs.Where(c => c.Value).Select(c => c.Key);
            var supportedAudio = audioCodecs.Where(c => c.Value).Select(c => c.Key);
            Log.Information("Supported video codecs: {VideoCodecs}", string.Join(", ", supportedVideo));
            Log.Information("Supported audio codecs: {AudioCodecs}", string.Join(", ", supportedAudio));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize FFmpeg");
            MessageBox.Show(
                $"FFmpeg 초기화 실패: {ex.Message}\n\n" +
                "ffmpeg 폴더에 FFmpeg 바이너리(dll)가 있는지 확인하세요.",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void ConfigureServices()
    {
        var services = new ServiceCollection();

        // 설정
        services.AddSingleton(PlayerSettings.Load());

        // 서비스
        services.AddSingleton<IPlayerService, PlayerService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();  // Singleton으로 변경 (상태 공유 필요)
        services.AddTransient<PlayerControlViewModel>();
        services.AddTransient<MultiPlayerViewModel>();

        _serviceProvider = services.BuildServiceProvider();
    }
}

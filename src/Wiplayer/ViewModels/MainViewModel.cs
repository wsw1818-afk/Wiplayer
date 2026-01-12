using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Wiplayer.Core.Player;
using Wiplayer.Core.Settings;
using Wiplayer.Core.Utils;
using Wiplayer.Services;
using Wiplayer.Subtitle;

namespace Wiplayer.ViewModels;

/// <summary>
/// 메인 윈도우 ViewModel
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IPlayerService _playerService;
    private readonly PlayerSettings _settings;
    private int _frameRenderCount = 0; // 프레임 쓰로틀링용 카운터

    [ObservableProperty]
    private PlayerState _state = PlayerState.Stopped;

    [ObservableProperty]
    private double _position;

    /// <summary>
    /// 사용자가 시크바를 조작 중인지 여부 (이 동안 Position 자동 업데이트 중지)
    /// </summary>
    public bool IsSeeking { get; set; } = false;

    [ObservableProperty]
    private double _duration;

    [ObservableProperty]
    private double _volume = 100;

    partial void OnVolumeChanged(double value)
    {
        _playerService.Volume = value / 100.0;
        // 설정에 저장
        _settings.Audio.DefaultVolume = (int)value;
    }

    [ObservableProperty]
    private bool _isMuted;

    partial void OnIsMutedChanged(bool value)
    {
        _playerService.IsMuted = value;
        // 설정에 저장
        _settings.Audio.IsMuted = value;
    }

    [ObservableProperty]
    private double _playbackSpeed = 1.0;

    [ObservableProperty]
    private string _title = "Wiplayer";

    [ObservableProperty]
    private string _positionText = "00:00";

    [ObservableProperty]
    private string _durationText = "00:00";

    [ObservableProperty]
    private string _speedText = "1.0x";

    [ObservableProperty]
    private string? _subtitleText;

    [ObservableProperty]
    private WriteableBitmap? _videoFrame;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private bool _showControls = true;

    [ObservableProperty]
    private MediaInfo? _mediaInfo;

    // 멀티뷰 관련 속성
    [ObservableProperty]
    private bool _isMultiViewMode;

    [ObservableProperty]
    private int _multiViewCount = 1; // 1, 2, 4

    [ObservableProperty]
    private ObservableCollection<PlayerPanelViewModel> _players = new();

    [ObservableProperty]
    private PlayerPanelViewModel? _selectedPlayer;

    public bool IsPlaying => State == PlayerState.Playing;
    public bool IsPaused => State == PlayerState.Paused;
    // 단일 모드: 메인 플레이어 상태, 멀티뷰 모드: 아무 플레이어라도 미디어가 있으면 true
    public bool HasMedia => IsMultiViewMode
        ? Players.Any(p => p.HasMedia)
        : State != PlayerState.Stopped;

    public MainViewModel(IPlayerService playerService, PlayerSettings settings)
    {
        _playerService = playerService;
        _settings = settings;

        // 이벤트 연결
        _playerService.StateChanged += OnStateChanged;
        _playerService.PositionChanged += OnPositionChanged;
        _playerService.ErrorOccurred += OnErrorOccurred;
        _playerService.FrameRendered += OnFrameRendered;
        _playerService.SubtitleManager.SubtitleChanged += OnSubtitleChanged;

        // 설정에서 볼륨/음소거 상태 로드
        _volume = _settings.Audio.DefaultVolume;
        _isMuted = _settings.Audio.IsMuted;
        _playerService.Volume = _volume / 100.0;
        _playerService.IsMuted = _isMuted;

        // 멀티뷰용 플레이어 패널 생성 (최대 4개)
        InitializeMultiViewPlayers();
    }

    private void InitializeMultiViewPlayers()
    {
        for (int i = 0; i < 4; i++)
        {
            var panel = new PlayerPanelViewModel(_settings, i + 1);
            panel.RequestFocus += OnPlayerRequestFocus;
            panel.PropertyChanged += OnPlayerPropertyChanged;
            Players.Add(panel);
        }
        // 기본 선택
        if (Players.Count > 0)
        {
            SelectedPlayer = Players[0];
            Players[0].IsSelected = true;
        }
    }

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 개별 플레이어의 HasMedia가 변경되면 메인 HasMedia도 업데이트
        if (e.PropertyName == nameof(PlayerPanelViewModel.HasMedia))
        {
            OnPropertyChanged(nameof(HasMedia));
        }
    }

    private void OnPlayerRequestFocus(object? sender, EventArgs e)
    {
        if (sender is PlayerPanelViewModel panel)
        {
            SelectPlayer(panel);
        }
    }

    private void SelectPlayer(PlayerPanelViewModel panel)
    {
        foreach (var p in Players)
        {
            p.IsSelected = false;
        }
        panel.IsSelected = true;
        SelectedPlayer = panel;
    }

    private void OnStateChanged(object? sender, PlayerStateChangedEventArgs e)
    {
        // UI 스레드에서 상태 업데이트 (바인딩 갱신을 위해 필수)
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        Action updateAction = () =>
        {
            State = e.NewState;
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(HasMedia));

            if (e.NewState == PlayerState.Playing || e.NewState == PlayerState.Paused)
            {
                MediaInfo = _playerService.CurrentMedia;
                Duration = _playerService.Duration;
                DurationText = FormatTime(Duration);
                Title = MediaInfo?.FileName ?? "Wiplayer";
                Position = _playerService.Position;
                PositionText = FormatTime(Position);
            }
            else if (e.NewState == PlayerState.Stopped)
            {
                Title = "Wiplayer";
                Position = 0;
                PositionText = "00:00";
                DurationText = "00:00";
            }
        };

        // 이미 UI 스레드면 즉시 실행, 아니면 비동기적으로 실행
        if (dispatcher.CheckAccess())
            updateAction();
        else
            dispatcher.BeginInvoke(updateAction);
    }

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        // 사용자가 시크바를 조작 중일 때는 Position 자동 업데이트 중지
        if (!IsSeeking)
        {
            Position = e.Position;
            PositionText = FormatTime(e.Position);
        }

        // 자막 업데이트
        _playerService.SubtitleManager.GetSubtitleAt(e.Position);
    }

    private void OnErrorOccurred(object? sender, PlayerErrorEventArgs e)
    {
        System.Windows.MessageBox.Show(e.Message, "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }

    private void OnFrameRendered(object? sender, EventArgs e)
    {
        // 프레임 업데이트 쓰로틀링: 5프레임마다 1회만 UI 업데이트 (UI 스레드 부하 80% 감소)
        if (++_frameRenderCount % 5 != 0) return;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            VideoFrame = _playerService.VideoFrame;
        });
    }

    private void OnSubtitleChanged(object? sender, SubtitleEntry? e)
    {
        SubtitleText = e?.Text;
    }

    [RelayCommand]
    private async Task OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = MediaFileHelper.GetOpenFileFilter(),
            Title = "미디어 파일 열기"
        };

        if (dialog.ShowDialog() == true)
        {
            LogDebug($"OpenFile: Before OpenAsync - State={State}, ServiceState={_playerService.State}");
            await _playerService.OpenAsync(dialog.FileName);
            LogDebug($"OpenFile: After OpenAsync - State={State}, ServiceState={_playerService.State}");
            _playerService.Play();
            LogDebug($"OpenFile: After Play - State={State}, ServiceState={_playerService.State}");

            // 상태 수동 동기화 (이벤트가 누락될 수 있으므로)
            SyncStateFromService();
            LogDebug($"OpenFile: After SyncState - State={State}, ServiceState={_playerService.State}");
        }
    }

    /// <summary>
    /// PlayerService 상태를 ViewModel에 동기화 (외부에서 호출 가능)
    /// </summary>
    public void SyncState() => SyncStateFromService();

    /// <summary>
    /// PlayerService 상태를 ViewModel에 동기화
    /// </summary>
    private void SyncStateFromService()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        Action syncAction = () =>
        {
            var serviceState = _playerService.State;
            if (State != serviceState)
            {
                LogDebug($"SyncStateFromService: State mismatch! ViewModel={State}, Service={serviceState}, IsUIThread={dispatcher.CheckAccess()}");
                State = serviceState;
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(HasMedia));
                LogDebug($"SyncStateFromService: After sync - State={State}, IsPlaying={IsPlaying}");
            }

            // Duration도 동기화 (재생 중이면 항상 업데이트)
            if (serviceState == PlayerState.Playing || serviceState == PlayerState.Paused)
            {
                var serviceDuration = _playerService.Duration;
                if (Duration != serviceDuration && serviceDuration > 0)
                {
                    Duration = serviceDuration;
                    DurationText = FormatTime(Duration);
                    MediaInfo = _playerService.CurrentMedia;
                    Title = MediaInfo?.FileName ?? "Wiplayer";
                }
            }
        };

        if (dispatcher.CheckAccess())
        {
            syncAction();
        }
        else
        {
            dispatcher.Invoke(syncAction);
        }
    }

    private void LogDebug(string msg) => System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");

    [RelayCommand]
    private void PlayPause()
    {
        LogDebug($"PlayPause() called - Before: State={State}, IsPlaying={IsPlaying}, ServiceState={_playerService.State}");
        _playerService.TogglePlayPause();
        LogDebug($"PlayPause() after TogglePlayPause - State={State}, IsPlaying={IsPlaying}, ServiceState={_playerService.State}");
    }

    [RelayCommand]
    private void Play()
    {
        _playerService.Play();
    }

    [RelayCommand]
    private void Pause()
    {
        _playerService.Pause();
    }

    [RelayCommand]
    private void Stop()
    {
        _playerService.Stop();
    }

    [RelayCommand]
    private async Task Seek(double position)
    {
        await _playerService.SeekAsync(position);
    }

    [RelayCommand]
    private async Task SeekRelative(object? offset)
    {
        double value = offset switch
        {
            double d => d,
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => 0
        };
        await _playerService.SeekRelativeAsync(value);
    }

    [RelayCommand]
    private void SetVolume(double volume)
    {
        Volume = volume;
        _playerService.Volume = volume / 100.0;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        _playerService.IsMuted = IsMuted;
    }

    [RelayCommand]
    private void SetSpeed(object? speed)
    {
        double value = speed switch
        {
            double d => d,
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => 1.0
        };
        PlaybackSpeed = Math.Clamp(value, 0.2, 4.0);
        _playerService.PlaybackSpeed = PlaybackSpeed;
        SpeedText = $"{PlaybackSpeed:F1}x";
    }

    [RelayCommand]
    private void SpeedUp()
    {
        SetSpeed(PlaybackSpeed + 0.1);
    }

    [RelayCommand]
    private void SpeedDown()
    {
        SetSpeed(PlaybackSpeed - 0.1);
    }

    [RelayCommand]
    private void ResetSpeed()
    {
        SetSpeed(1.0);
    }

    [RelayCommand]
    private void ToggleABRepeat()
    {
        _playerService.ABRepeat.Toggle(Position);
    }

    [RelayCommand]
    private void ToggleSubtitle()
    {
        _playerService.SubtitleManager.IsVisible = !_playerService.SubtitleManager.IsVisible;
        if (!_playerService.SubtitleManager.IsVisible)
        {
            SubtitleText = null;
        }
    }

    [RelayCommand]
    private void SubtitleSyncPlus()
    {
        _playerService.SubtitleManager.AdjustSync(100); // +100ms
    }

    [RelayCommand]
    private void SubtitleSyncMinus()
    {
        _playerService.SubtitleManager.AdjustSync(-100); // -100ms
    }

    [RelayCommand]
    private void LoadSubtitle()
    {
        var dialog = new OpenFileDialog
        {
            Filter = MediaFileHelper.GetSubtitleFileFilter(),
            Title = "자막 파일 열기"
        };

        if (dialog.ShowDialog() == true)
        {
            _playerService.LoadSubtitle(dialog.FileName);
        }
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        IsFullscreen = !IsFullscreen;
    }

    [RelayCommand]
    private void StepForward()
    {
        _playerService.StepFrame(true);
    }

    [RelayCommand]
    private void StepBackward()
    {
        _playerService.StepFrame(false);
    }

    // ========== 스크린샷 기능 ==========
    [RelayCommand]
    private async Task TakeScreenshot()
    {
        LogDebug("TakeScreenshot() 호출됨");
        var path = await _playerService.SaveScreenshotAsync();
        LogDebug($"TakeScreenshot() 결과: {path ?? "null"}");
        if (!string.IsNullOrEmpty(path) && _settings.Screenshot.ShowNotification)
        {
            System.Windows.MessageBox.Show(
                $"스크린샷이 저장되었습니다.\n{path}",
                "스크린샷",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        else if (string.IsNullOrEmpty(path))
        {
            System.Windows.MessageBox.Show(
                "스크린샷 저장에 실패했습니다.\n재생 중인 영상이 있는지 확인하세요.",
                "스크린샷 실패",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    // ========== 화면 회전 기능 ==========
    [ObservableProperty]
    private int _rotationAngle = 0;

    [RelayCommand]
    private void RotateScreen(object? angle)
    {
        LogDebug($"RotateScreen() 호출됨, angle={angle}");
        int value = angle switch
        {
            int i => i,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 90
        };

        RotationAngle = (RotationAngle + value) % 360;
        if (RotationAngle < 0) RotationAngle += 360;
        _playerService.RotationAngle = RotationAngle;
        LogDebug($"RotateScreen() 결과: RotationAngle={RotationAngle}");
    }

    [RelayCommand]
    private void ResetRotation()
    {
        LogDebug("ResetRotation() 호출됨");
        RotationAngle = 0;
        _playerService.RotationAngle = 0;
    }

    // ========== 밝기/대비/채도 조절 ==========
    [ObservableProperty]
    private int _brightness = 0;

    partial void OnBrightnessChanged(int value)
    {
        LogDebug($"Brightness 변경됨: {value}");
        _playerService.Brightness = value;
    }

    [ObservableProperty]
    private int _contrast = 0;

    partial void OnContrastChanged(int value)
    {
        LogDebug($"Contrast 변경됨: {value}");
        _playerService.Contrast = value;
    }

    [ObservableProperty]
    private int _saturation = 0;

    partial void OnSaturationChanged(int value)
    {
        LogDebug($"Saturation 변경됨: {value}");
        _playerService.Saturation = value;
    }

    [RelayCommand]
    private void ResetVideoFilters()
    {
        LogDebug("ResetVideoFilters() 호출됨");
        Brightness = 0;
        Contrast = 0;
        Saturation = 0;
    }

    // ========== 미디어 정보 표시 ==========
    [RelayCommand]
    private void ShowMediaInfo()
    {
        var info = _playerService.CurrentMedia;
        if (info == null)
        {
            System.Windows.MessageBox.Show("재생 중인 미디어가 없습니다.", "미디어 정보");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== 파일 정보 ===");
        sb.AppendLine($"파일명: {info.FileName}");
        sb.AppendLine($"경로: {info.Path}");
        sb.AppendLine($"포맷: {info.Format}");
        sb.AppendLine($"크기: {FormatFileSize(info.FileSize)}");
        sb.AppendLine($"재생 시간: {FormatTime(info.Duration)}");
        sb.AppendLine($"비트레이트: {info.Bitrate / 1000} kbps");
        sb.AppendLine();

        if (info.VideoStreams.Count > 0)
        {
            sb.AppendLine("=== 비디오 스트림 ===");
            foreach (var video in info.VideoStreams)
            {
                sb.AppendLine($"코덱: {video.CodecLongName} ({video.Codec})");
                sb.AppendLine($"해상도: {video.Resolution}");
                sb.AppendLine($"프레임레이트: {video.FrameRate:F2} fps");
                sb.AppendLine($"비트레이트: {video.Bitrate / 1000} kbps");
                sb.AppendLine($"픽셀 포맷: {video.PixelFormat}");
                if (!string.IsNullOrEmpty(video.Language))
                    sb.AppendLine($"언어: {video.Language}");
                sb.AppendLine();
            }
        }

        if (info.AudioStreams.Count > 0)
        {
            sb.AppendLine("=== 오디오 스트림 ===");
            foreach (var audio in info.AudioStreams)
            {
                sb.AppendLine($"코덱: {audio.CodecLongName} ({audio.Codec})");
                sb.AppendLine($"샘플레이트: {audio.SampleRate} Hz");
                sb.AppendLine($"채널: {audio.ChannelDescription}");
                sb.AppendLine($"비트레이트: {audio.Bitrate / 1000} kbps");
                if (!string.IsNullOrEmpty(audio.Language))
                    sb.AppendLine($"언어: {audio.Language}");
                sb.AppendLine();
            }
        }

        if (info.SubtitleStreams.Count > 0)
        {
            sb.AppendLine("=== 자막 스트림 ===");
            foreach (var sub in info.SubtitleStreams)
            {
                sb.AppendLine($"코덱: {sub.Codec}");
                if (!string.IsNullOrEmpty(sub.Language))
                    sb.AppendLine($"언어: {sub.Language}");
                if (!string.IsNullOrEmpty(sub.Title))
                    sb.AppendLine($"제목: {sub.Title}");
                sb.AppendLine();
            }
        }

        if (info.Metadata.Count > 0)
        {
            sb.AppendLine("=== 메타데이터 ===");
            foreach (var meta in info.Metadata)
            {
                sb.AppendLine($"{meta.Key}: {meta.Value}");
            }
        }

        System.Windows.MessageBox.Show(sb.ToString(), "미디어 정보", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    // ========== 최근 재생 목록 ==========
    public IReadOnlyList<Core.Playlist.PlaylistItem> RecentFiles => _playerService.RecentFiles;

    [RelayCommand]
    private void OpenRecentFile(object? path)
    {
        if (path is string filePath)
        {
            _playerService.OpenRecentFile(filePath);
        }
    }

    // ========== 북마크 기능 ==========
    public IReadOnlyList<Core.Playlist.BookmarkItem> Bookmarks => _playerService.Bookmarks.Items;

    [RelayCommand]
    private void AddBookmark()
    {
        _playerService.AddBookmark();
        OnPropertyChanged(nameof(Bookmarks));
        System.Windows.MessageBox.Show(
            $"북마크가 추가되었습니다.\n위치: {PositionText}",
            "북마크",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task GoToBookmark(object? bookmark)
    {
        if (bookmark is Core.Playlist.BookmarkItem item)
        {
            await _playerService.GoToBookmark(item);
            SyncStateFromService();
        }
    }

    [RelayCommand]
    private void RemoveBookmark(object? bookmark)
    {
        if (bookmark is Core.Playlist.BookmarkItem item)
        {
            _playerService.Bookmarks.Remove(item);
            OnPropertyChanged(nameof(Bookmarks));
        }
    }

    // 시간 포맷 캐시 (1시간 = 3600초, 메모리 할당 제거)
    private static readonly string[] _timeFormatCache;
    static MainViewModel()
    {
        _timeFormatCache = new string[3600];
        for (int i = 0; i < 3600; i++)
        {
            int m = i / 60;
            int s = i % 60;
            _timeFormatCache[i] = $"{m:D2}:{s:D2}";
        }
    }

    private static string FormatTime(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            return "00:00";

        int intSec = (int)seconds;

        // 1시간 미만: 캐시 사용
        if (intSec < 3600)
            return _timeFormatCache[intSec];

        // 1시간 이상: 직접 계산
        int h = intSec / 3600;
        int m = (intSec % 3600) / 60;
        int s = intSec % 60;
        return $"{h}:{m:D2}:{s:D2}";
    }

    // 멀티뷰 관련 명령
    [RelayCommand]
    private void ToggleMultiView()
    {
        IsMultiViewMode = !IsMultiViewMode;
        if (!IsMultiViewMode)
        {
            MultiViewCount = 1;
        }
    }

    /// <summary>
    /// 멀티뷰 카운트 설정 (외부에서 await 가능)
    /// </summary>
    public async Task SetMultiViewCountAsync(int count)
    {
        await SetMultiViewCount(count);
    }

    [RelayCommand]
    private async Task SetMultiViewCount(object? count)
    {
        int newCount = count switch
        {
            int i => i,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 1
        };

        // 유효한 값으로 정규화
        if (newCount != 1 && newCount != 2 && newCount != 4)
            newCount = 1;

        // 같은 뷰 모드면 무시
        if (MultiViewCount == newCount)
            return;

        // 단일뷰 → 멀티뷰: 메인 플레이어 영상을 1번 플레이어로 전달
        if (MultiViewCount == 1 && newCount > 1)
        {
            var hasMedia = _playerService.State == PlayerState.Playing || _playerService.State == PlayerState.Paused;
            var currentMedia = _playerService.CurrentMedia;

            if (hasMedia && currentMedia != null && !string.IsNullOrEmpty(currentMedia.Path))
            {
                // 현재 상태 저장
                var mediaPath = currentMedia.Path;
                var position = _playerService.Position;
                var volume = Volume;
                var isMuted = IsMuted;
                var wasPaused = _playerService.State == PlayerState.Paused;

                // 뷰 모드 전환 후 메인 플레이어 정지
                MultiViewCount = newCount;
                IsMultiViewMode = true;
                _playerService.Stop();

                // 1번 플레이어에서 영상 열기
                if (Players.Count > 0)
                {
                    try
                    {
                        await Players[0].OpenFileAsync(mediaPath);
                        await Players[0].SeekToAsync(position);
                        Players[0].Volume = volume;
                        Players[0].IsMuted = isMuted;
                        if (wasPaused) Players[0].Pause();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Single→Multi 영상 전달 실패: {ex.Message}");
                    }
                }
                return;
            }
        }

        // 멀티뷰 → 단일뷰: 1번 플레이어 영상을 메인 플레이어로 전달
        if (MultiViewCount > 1 && newCount == 1 && Players.Count > 0)
        {
            var player1 = Players[0];
            var hasMedia = player1.State == PlayerState.Playing || player1.State == PlayerState.Paused;

            if (hasMedia && player1.CurrentMedia != null && !string.IsNullOrEmpty(player1.CurrentMedia.Path))
            {
                // 현재 상태 저장
                var mediaPath = player1.CurrentMedia.Path;
                var position = player1.Position;
                var volume = player1.Volume;
                var isMuted = player1.IsMuted;
                var wasPaused = player1.State == PlayerState.Paused;

                // 1번 플레이어 정지 후 뷰 모드 전환
                player1.Stop();
                MultiViewCount = newCount;
                IsMultiViewMode = false;

                // 메인 플레이어에서 영상 열기
                try
                {
                    await _playerService.OpenAsync(mediaPath);
                    await _playerService.SeekAsync(position);
                    Volume = volume;
                    IsMuted = isMuted;
                    if (wasPaused) _playerService.Pause();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Multi→Single 영상 전달 실패: {ex.Message}");
                }
                return;
            }
        }

        MultiViewCount = newCount;
        IsMultiViewMode = newCount > 1;
    }

    [RelayCommand]
    private void SelectPlayerByIndex(object? index)
    {
        int idx = index switch
        {
            int i => i,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 0
        };

        if (idx >= 0 && idx < Players.Count)
        {
            SelectPlayer(Players[idx]);
        }
    }

    [RelayCommand]
    private void PlayAllPlayers()
    {
        foreach (var player in Players)
        {
            if (player.HasMedia && !player.IsPlaying)
            {
                player.Play();
            }
        }
    }

    [RelayCommand]
    private void PauseAllPlayers()
    {
        foreach (var player in Players)
        {
            if (player.IsPlaying)
            {
                player.Pause();
            }
        }
    }

    [RelayCommand]
    private void StopAllPlayers()
    {
        foreach (var player in Players)
        {
            player.Stop();
        }
    }

    public void Dispose()
    {
        foreach (var player in Players)
        {
            player.Dispose();
        }
        Players.Clear();
        GC.SuppressFinalize(this);
    }
}

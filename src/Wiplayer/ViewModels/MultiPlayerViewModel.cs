using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Wiplayer.Core.Player;
using Wiplayer.Core.Settings;
using Wiplayer.Core.Utils;
using Wiplayer.Services;

namespace Wiplayer.ViewModels;

/// <summary>
/// 4분할 멀티 플레이어 ViewModel
/// </summary>
public partial class MultiPlayerViewModel : ObservableObject
{
    private readonly PlayerSettings _settings;

    [ObservableProperty]
    private ObservableCollection<PlayerPanelViewModel> _players = new();

    [ObservableProperty]
    private PlayerPanelViewModel? _selectedPlayer;

    [ObservableProperty]
    private bool _showControls = true;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private string _title = "Wiplayer - 멀티뷰";

    public MultiPlayerViewModel(PlayerSettings settings)
    {
        _settings = settings;

        // 4개의 플레이어 패널 생성
        for (int i = 0; i < 4; i++)
        {
            var panel = new PlayerPanelViewModel(_settings, i + 1);
            panel.RequestFocus += OnPlayerRequestFocus;
            Players.Add(panel);
        }

        SelectedPlayer = Players[0];
    }

    private void OnPlayerRequestFocus(object? sender, EventArgs e)
    {
        if (sender is PlayerPanelViewModel panel)
        {
            SelectedPlayer = panel;
        }
    }

    [RelayCommand]
    private async Task OpenFileToPanel(int panelIndex)
    {
        if (panelIndex < 0 || panelIndex >= Players.Count)
            return;

        var dialog = new OpenFileDialog
        {
            Filter = MediaFileHelper.GetOpenFileFilter(),
            Title = $"플레이어 {panelIndex + 1}에 파일 열기"
        };

        if (dialog.ShowDialog() == true)
        {
            await Players[panelIndex].OpenFileAsync(dialog.FileName);
        }
    }

    [RelayCommand]
    private void PlayAll()
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
    private void PauseAll()
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
    private void StopAll()
    {
        foreach (var player in Players)
        {
            player.Stop();
        }
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        IsFullscreen = !IsFullscreen;
    }

    [RelayCommand]
    private void SelectPlayer(int panelIndex)
    {
        if (panelIndex >= 0 && panelIndex < Players.Count)
        {
            SelectedPlayer = Players[panelIndex];
        }
    }

    /// <summary>
    /// 리소스 정리
    /// </summary>
    public void Dispose()
    {
        foreach (var player in Players)
        {
            player.Dispose();
        }
        Players.Clear();
    }
}

/// <summary>
/// 개별 플레이어 패널 ViewModel
/// </summary>
public partial class PlayerPanelViewModel : ObservableObject, IDisposable
{
    private readonly IPlayerService _playerService;
    private readonly int _panelNumber;

    [ObservableProperty]
    private PlayerState _state = PlayerState.Stopped;

    [ObservableProperty]
    private double _position;

    [ObservableProperty]
    private double _duration;

    [ObservableProperty]
    private double _volume = 100;

    partial void OnVolumeChanged(double value)
    {
        _playerService.Volume = value / 100.0;
    }

    [ObservableProperty]
    private bool _isMuted;

    partial void OnIsMutedChanged(bool value)
    {
        _playerService.IsMuted = value;
    }

    [ObservableProperty]
    private string _positionText = "00:00";

    [ObservableProperty]
    private string _durationText = "00:00";

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private WriteableBitmap? _videoFrame;

    [ObservableProperty]
    private bool _isSelected;

    public int PanelNumber => _panelNumber;
    public bool IsPlaying => State == PlayerState.Playing;
    public bool HasMedia => State != PlayerState.Stopped && State != PlayerState.Error;
    public MediaInfo? CurrentMedia => _playerService.CurrentMedia;

    // 시크바용 0~100 범위 위치 (퍼센트)
    private double _lastSeekPosition = -1;
    public double SeekPosition
    {
        get => Duration > 0 ? (Position / Duration) * 100 : 0;
        set
        {
            if (Duration > 0)
            {
                var newPosition = (value / 100) * Duration;
                // 동일한 위치로 반복 Seek 방지 (0.5초 이내 차이는 무시)
                if (Math.Abs(newPosition - _lastSeekPosition) < 0.5)
                    return;
                _lastSeekPosition = newPosition;
                _ = _playerService.SeekAsync(newPosition);
            }
        }
    }

    public event EventHandler? RequestFocus;

    public PlayerPanelViewModel(PlayerSettings settings, int panelNumber)
    {
        _panelNumber = panelNumber;
        _title = $"플레이어 {panelNumber}";
        _playerService = new PlayerService(settings);

        // 이벤트 연결
        _playerService.StateChanged += OnStateChanged;
        _playerService.PositionChanged += OnPositionChanged;
        _playerService.ErrorOccurred += OnErrorOccurred;
        _playerService.FrameRendered += OnFrameRendered;

        Volume = settings.Audio.DefaultVolume;
    }

    private void OnStateChanged(object? sender, PlayerStateChangedEventArgs e)
    {
        // UI 스레드에서 상태 업데이트 (바인딩 갱신을 위해 필수)
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            State = e.NewState;
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(HasMedia));

            if (e.NewState == PlayerState.Playing || e.NewState == PlayerState.Paused)
            {
                Duration = _playerService.Duration;
                DurationText = FormatTime(Duration);
                Title = _playerService.CurrentMedia?.FileName ?? $"플레이어 {_panelNumber}";
            }
            else if (e.NewState == PlayerState.Stopped)
            {
                Title = $"플레이어 {_panelNumber}";
                Position = 0;
                PositionText = "00:00";
                DurationText = "00:00";
            }
        });
    }

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        Position = e.Position;
        PositionText = FormatTime(e.Position);
        OnPropertyChanged(nameof(SeekPosition));
    }

    private void OnErrorOccurred(object? sender, PlayerErrorEventArgs e)
    {
        // 오디오 관련 오류는 무시 (다른 패널과 충돌 시 발생)
        if (e.Message.Contains("0x8889") || e.Message.Contains("WASAPI") || e.Message.Contains("오디오"))
        {
            System.Diagnostics.Debug.WriteLine($"[플레이어 {_panelNumber}] 오디오 오류 무시: {e.Message}");
            return;
        }

        System.Windows.MessageBox.Show(
            $"[플레이어 {_panelNumber}] 파일 열기 실패: {e.Message}",
            "오류",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
    }

    private void OnFrameRendered(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            VideoFrame = _playerService.VideoFrame;
        });
    }

    public async Task OpenFileAsync(string path)
    {
        if (await _playerService.OpenAsync(path))
        {
            _playerService.Play();
        }
    }

    [RelayCommand]
    private async Task OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = MediaFileHelper.GetOpenFileFilter(),
            Title = $"플레이어 {_panelNumber}에 파일 열기"
        };

        if (dialog.ShowDialog() == true)
        {
            await OpenFileAsync(dialog.FileName);
        }
    }

    [RelayCommand]
    private void PlayPause()
    {
        _playerService.TogglePlayPause();
    }

    [RelayCommand]
    public void Play()
    {
        _playerService.Play();
    }

    [RelayCommand]
    public void Pause()
    {
        _playerService.Pause();
    }

    [RelayCommand]
    public void Stop()
    {
        // 멀티뷰에서는 정지 대신 처음으로 되돌리기 + 일시정지
        // (완전 정지하면 영상이 닫히므로)
        var serviceState = _playerService.State;
        if (serviceState == PlayerState.Playing || serviceState == PlayerState.Paused || serviceState == PlayerState.Ended)
        {
            // ResetToStart: 처음으로 이동 + 일시정지 (클럭 리셋 포함)
            _playerService.ResetToStart();
        }
        else
        {
            _playerService.Stop();
        }
    }

    [RelayCommand]
    private async Task Seek(double position)
    {
        await _playerService.SeekAsync(position);
    }

    /// <summary>
    /// 절대 위치로 시크 (초 단위)
    /// </summary>
    public async Task SeekToAsync(double positionInSeconds)
    {
        await _playerService.SeekAsync(positionInSeconds);
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
    private void Focus()
    {
        RequestFocus?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatTime(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            return "00:00";

        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    public void Dispose()
    {
        _playerService.StateChanged -= OnStateChanged;
        _playerService.PositionChanged -= OnPositionChanged;
        _playerService.ErrorOccurred -= OnErrorOccurred;
        _playerService.FrameRendered -= OnFrameRendered;

        if (_playerService is IDisposable disposable)
        {
            disposable.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

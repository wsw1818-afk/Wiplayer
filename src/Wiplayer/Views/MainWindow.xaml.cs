using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Wiplayer.Core.Utils;
using Wiplayer.ViewModels;

namespace Wiplayer.Views;

/// <summary>
/// 메인 윈도우
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private DateTime _lastMouseMove = DateTime.Now;
    private System.Threading.Timer? _hideControlsTimer;
    private bool _isUserInteracting = false;
    private WindowState _previousWindowState;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = App.ServiceProvider.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;

        // 컨트롤 자동 숨김 타이머
        _hideControlsTimer = new System.Threading.Timer(HideControlsCallback, null, 3000, System.Threading.Timeout.Infinite);

        // 시크바 이벤트 핸들러 등록 (handledEventsToo=true로 처리된 이벤트도 수신)
        SeekBar.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
            new System.Windows.Controls.Primitives.DragStartedEventHandler(SeekBar_DragStarted));
        SeekBar.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
            new System.Windows.Controls.Primitives.DragCompletedEventHandler(SeekBar_DragCompleted));

        // PreviewMouseLeftButtonDown도 handledEventsToo=true로 등록 (Track 내부 요소가 처리한 이벤트도 수신)
        SeekBar.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(SeekBar_PreviewMouseLeftButtonDown), true);

        // Loaded 이벤트에서 Duration 동기화
        Loaded += (s, e) =>
        {
            if (_viewModel.Duration > 0)
            {
                SeekBar.Maximum = _viewModel.Duration;
                Log.Debug("[MainWindow] Loaded - SeekBar.Maximum 초기화: {Maximum}초", SeekBar.Maximum);
            }
        };

        // 전체화면 변경 감지 및 Position 업데이트
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsFullscreen))
            {
                UpdateFullscreenState();
            }
            else if (e.PropertyName == nameof(MainViewModel.Duration))
            {
                Log.Debug("[MainWindow] Duration 변경: {Duration}초", _viewModel.Duration);
                Dispatcher.BeginInvoke(() =>
                {
                    SeekBar.Maximum = _viewModel.Duration;
                    Log.Debug("[MainWindow] SeekBar.Maximum 설정됨: {Maximum}초", SeekBar.Maximum);
                });
            }
            else if (e.PropertyName == nameof(MainViewModel.State))
            {
                // State 변경 시 Duration도 업데이트 (미디어 로드 후 PropertyChanged 누락 방지)
                if (_viewModel.Duration > 0 && Math.Abs(SeekBar.Maximum - _viewModel.Duration) > 0.1)
                {
                    Log.Debug("[MainWindow] State 변경 시 Duration 동기화: {Duration}초", _viewModel.Duration);
                    Dispatcher.BeginInvoke(() =>
                    {
                        SeekBar.Maximum = _viewModel.Duration;
                        Log.Debug("[MainWindow] SeekBar.Maximum 동기화됨: {Maximum}초", SeekBar.Maximum);
                    });
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.Position))
            {
                // 사용자가 조작 중이 아닐 때만 슬라이더 값 업데이트
                if (!_isUserInteracting)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        // Duration이 아직 동기화되지 않았으면 동기화
                        if (_viewModel.Duration > 0 && Math.Abs(SeekBar.Maximum - _viewModel.Duration) > 0.1)
                        {
                            SeekBar.Maximum = _viewModel.Duration;
                            Log.Debug("[MainWindow] Position 업데이트 시 Duration 동기화: {Maximum}초", SeekBar.Maximum);
                        }

                        if (!_isUserInteracting)
                        {
                            SeekBar.Value = _viewModel.Position;
                        }
                    });
                }
            }
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ShowControls();

        var handled = true;

        switch (e.Key)
        {
            case Key.Space:
                _viewModel.PlayPauseCommand.Execute(null);
                ShowPlayPauseOverlay();
                break;

            case Key.Left:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                    _viewModel.SeekRelativeCommand.Execute(-30.0);
                else
                    _viewModel.SeekRelativeCommand.Execute(-5.0);
                break;

            case Key.Right:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                    _viewModel.SeekRelativeCommand.Execute(30.0);
                else
                    _viewModel.SeekRelativeCommand.Execute(5.0);
                break;

            case Key.Up:
                _viewModel.SetVolumeCommand.Execute(_viewModel.Volume + 5);
                break;

            case Key.Down:
                _viewModel.SetVolumeCommand.Execute(_viewModel.Volume - 5);
                break;

            case Key.M:
                _viewModel.ToggleMuteCommand.Execute(null);
                break;

            case Key.F:
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Alt:
                _viewModel.ToggleFullscreenCommand.Execute(null);
                break;

            case Key.Escape:
                if (_viewModel.IsFullscreen)
                    _viewModel.IsFullscreen = false;
                break;

            case Key.S:
                if (Keyboard.Modifiers == ModifierKeys.None)
                    _viewModel.StopCommand.Execute(null);
                break;

            case Key.V:
                _viewModel.ToggleSubtitleCommand.Execute(null);
                break;

            case Key.R:
                _viewModel.ToggleABRepeatCommand.Execute(null);
                break;

            case Key.OemOpenBrackets: // [
                if (Keyboard.Modifiers == ModifierKeys.Control)
                    _viewModel.SubtitleSyncMinusCommand.Execute(null);
                else
                    _viewModel.SpeedDownCommand.Execute(null);
                break;

            case Key.OemCloseBrackets: // ]
                if (Keyboard.Modifiers == ModifierKeys.Control)
                    _viewModel.SubtitleSyncPlusCommand.Execute(null);
                else
                    _viewModel.SpeedUpCommand.Execute(null);
                break;

            case Key.Back:
                _viewModel.ResetSpeedCommand.Execute(null);
                break;

            case Key.OemPeriod: // .
                _viewModel.StepForwardCommand.Execute(null);
                break;

            case Key.OemComma: // ,
                _viewModel.StepBackwardCommand.Execute(null);
                break;

            case Key.O:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                    _viewModel.OpenFileCommand.Execute(null);
                else
                    handled = false;
                break;

            case Key.D1:
            case Key.NumPad1:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                    _ = _viewModel.SetMultiViewCountAsync(1);
                else
                    handled = false;
                break;

            case Key.D2:
            case Key.NumPad2:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                    _ = _viewModel.SetMultiViewCountAsync(2);
                else
                    handled = false;
                break;

            case Key.D4:
            case Key.NumPad4:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                    _ = _viewModel.SetMultiViewCountAsync(4);
                else
                    handled = false;
                break;

            default:
                handled = false;
                break;
        }

        if (handled)
            e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        ShowControls();
    }

    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            // 컨트롤 바 영역이 아닌 경우에만 전체화면 토글
            var position = e.GetPosition(this);
            if (position.Y < ActualHeight - 80)
            {
                _viewModel.ToggleFullscreenCommand.Execute(null);
            }
        }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                var file = files[0];

                // 미디어 파일이면 재생
                if (MediaFileHelper.IsMediaFile(file))
                {
                    // PlayerService를 통해 파일 열기
                    var playerService = App.ServiceProvider.GetRequiredService<Wiplayer.Services.IPlayerService>();
                    await playerService.OpenAsync(file);
                    playerService.Play();

                    // ViewModel 상태 동기화
                    _viewModel.SyncState();
                }
                // 자막 파일이면 로드
                else if (MediaFileHelper.IsSubtitleFile(file))
                {
                    var playerService = App.ServiceProvider.GetRequiredService<Wiplayer.Services.IPlayerService>();
                    playerService.LoadSubtitle(file);
                }
            }
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                var file = files[0];
                if (MediaFileHelper.IsMediaFile(file) || MediaFileHelper.IsSubtitleFile(file))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
        }
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private async void SeekBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // PlayerService에서 직접 Duration 가져오기 (ViewModel의 Duration이 동기화되지 않을 수 있음)
        var playerService = App.ServiceProvider.GetRequiredService<Wiplayer.Services.IPlayerService>();
        var duration = playerService.Duration;

        Log.Debug("[SeekBar] PreviewMouseLeftButtonDown 이벤트 발생, Maximum={Maximum}, ViewModel.Duration={VmDuration}, PlayerService.Duration={PsDuration}",
            SeekBar.Maximum, _viewModel.Duration, duration);

        // Duration이 아직 동기화되지 않았으면 PlayerService에서 가져온 값으로 동기화
        if (duration > 0 && Math.Abs(SeekBar.Maximum - duration) > 0.1)
        {
            SeekBar.Maximum = duration;
            Log.Debug("[SeekBar] Maximum 동기화 (from PlayerService): {Maximum}초", SeekBar.Maximum);
        }

        _isUserInteracting = true;
        _viewModel.IsSeeking = true;

        if (sender is Slider slider && duration > 0)
        {
            // Track 컴포넌트를 찾아서 실제 트랙 위치 기준으로 계산 (Thumb 오프셋 보정)
            var track = slider.Template.FindName("PART_Track", slider) as System.Windows.Controls.Primitives.Track;
            double ratio;

            if (track != null && track.ActualWidth > 0)
            {
                // Track 기준으로 위치 계산 (정확함)
                var clickPosition = e.GetPosition(track);
                ratio = Math.Clamp(clickPosition.X / track.ActualWidth, 0, 1);
                Log.Debug("[SeekBar] Track 기준 계산: clickX={ClickX}, trackWidth={TrackWidth}",
                    clickPosition.X, track.ActualWidth);
            }
            else
            {
                // 폴백: Slider 기준으로 계산
                var clickPosition = e.GetPosition(slider);
                ratio = Math.Clamp(clickPosition.X / slider.ActualWidth, 0, 1);
                Log.Debug("[SeekBar] Slider 기준 계산 (폴백): clickX={ClickX}, sliderWidth={SliderWidth}",
                    clickPosition.X, slider.ActualWidth);
            }

            var seekPosition = ratio * duration;  // 0 ~ duration 범위

            Log.Debug("[SeekBar] 시크 위치: ratio={Ratio}, seekTo={SeekPosition}초", ratio, seekPosition);

            // 슬라이더 값도 업데이트
            slider.Value = seekPosition;

            // 즉시 시크 수행
            Log.Debug("[SeekBar] SeekAsync 호출 시작: {Position}초", seekPosition);
            await playerService.SeekAsync(seekPosition);
            Log.Debug("[SeekBar] SeekAsync 완료");
        }
        else
        {
            Log.Warning("[SeekBar] duration <= 0: PlayerService.Duration={Duration}", duration);
        }
    }

    private void SeekBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[SeekBar] PreviewMouseLeftButtonUp");
        _isUserInteracting = false;
        _viewModel.IsSeeking = false;
    }

    private void SeekBar_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        // WPF 슬라이더 기본 드래그 동작 사용 - 별도 처리 불필요
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // 자동 업데이트 시에는 무시
        if (!_isUserInteracting)
            return;

        // 사용자가 조작 중일 때 로그만 출력 (실제 시크는 MouseDown/DragCompleted에서)
        if (sender is Slider slider && slider.Maximum > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[SeekBar] ValueChanged: {e.OldValue:F2} -> {e.NewValue:F2}");
        }
    }

    private void SeekBar_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        _isUserInteracting = true;
        _viewModel.IsSeeking = true;
        System.Diagnostics.Debug.WriteLine($"[SeekBar] DragStarted");
    }

    private async void SeekBar_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        // 마우스 캡처 해제 (드래그 후 마우스가 앱 뒤에 밀리는 문제 방지)
        Mouse.Capture(null);

        // Slider의 Thumb에서도 마우스 캡처 해제
        if (sender is System.Windows.Controls.Slider slider)
        {
            slider.ReleaseMouseCapture();
        }

        // PlayerService에서 직접 Duration 가져오기
        var playerService = App.ServiceProvider.GetRequiredService<Wiplayer.Services.IPlayerService>();
        var duration = playerService.Duration;

        Log.Debug("[SeekBar] DragCompleted: Value={Value}, Maximum={Maximum}, PlayerService.Duration={Duration}",
            SeekBar.Value, SeekBar.Maximum, duration);

        // 드래그 완료 시 해당 위치로 시크
        if (duration > 0)
        {
            // Value를 Duration 기준으로 재계산 (Maximum이 100이었던 경우 대응)
            var seekPosition = SeekBar.Value;

            // Maximum이 100이고 Duration이 다른 경우 (동기화되지 않은 경우)
            if (Math.Abs(SeekBar.Maximum - 100) < 0.1 && duration > 0)
            {
                // 100 기준으로 계산된 값을 Duration 기준으로 변환
                seekPosition = (SeekBar.Value / 100.0) * duration;
                Log.Debug("[SeekBar] DragCompleted - 위치 재계산 (100->Duration): {OldValue} -> {NewValue}", SeekBar.Value, seekPosition);

                // Maximum도 업데이트
                SeekBar.Maximum = duration;
            }
            else if (SeekBar.Value > duration)
            {
                // Value가 duration보다 크면 재계산
                seekPosition = (SeekBar.Value / SeekBar.Maximum) * duration;
                Log.Debug("[SeekBar] DragCompleted - 위치 재계산: {OldValue} -> {NewValue}", SeekBar.Value, seekPosition);
            }

            Log.Debug("[SeekBar] DragCompleted - SeekAsync 호출: {Position}초", seekPosition);
            await playerService.SeekAsync(seekPosition);
            Log.Debug("[SeekBar] DragCompleted - SeekAsync 완료");
        }

        _isUserInteracting = false;
        _viewModel.IsSeeking = false;
    }

    private void ShowControls()
    {
        _lastMouseMove = DateTime.Now;
        _viewModel.ShowControls = true;

        // 타이머 리셋
        _hideControlsTimer?.Change(3000, System.Threading.Timeout.Infinite);
    }

    private void HideControlsCallback(object? state)
    {
        // 재생 중이고 3초 동안 마우스 움직임이 없으면 컨트롤 숨기기
        if (_viewModel.IsPlaying && (DateTime.Now - _lastMouseMove).TotalSeconds >= 3)
        {
            Dispatcher.Invoke(() =>
            {
                _viewModel.ShowControls = false;
                Mouse.OverrideCursor = Cursors.None;
            });
        }
        else
        {
            Dispatcher.Invoke(() =>
            {
                Mouse.OverrideCursor = null;
            });
        }
    }

    private void UpdateFullscreenState()
    {
        if (_viewModel.IsFullscreen)
        {
            _previousWindowState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _previousWindowState;
            ResizeMode = ResizeMode.CanResize;
        }
    }

    private void ShowPlayPauseOverlay()
    {
        // 재생/일시정지 아이콘 표시 후 페이드아웃
        PlayPauseIcon.Text = _viewModel.IsPlaying ? "\uE769" : "\uE768"; // Pause : Play

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(100));
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500))
        {
            BeginTime = TimeSpan.FromMilliseconds(300)
        };

        PlayPauseOverlay.BeginAnimation(OpacityProperty, fadeIn);
        PlayPauseOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }

    protected override void OnClosed(EventArgs e)
    {
        _hideControlsTimer?.Dispose();
        base.OnClosed(e);
    }

    private void PlayerPanel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string tagStr && int.TryParse(tagStr, out int index))
        {
            _viewModel.SelectPlayerByIndexCommand.Execute(index);

            // 더블클릭 감지
            if (e.ClickCount == 2 && index >= 0 && index < _viewModel.Players.Count)
            {
                _viewModel.Players[index].OpenFileCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void PlayerPanel_Drop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string tagStr && int.TryParse(tagStr, out int index))
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && index >= 0 && index < _viewModel.Players.Count)
                {
                    var file = files[0];
                    if (MediaFileHelper.IsMediaFile(file))
                    {
                        _ = _viewModel.Players[index].OpenFileAsync(file);
                        _viewModel.SelectPlayerByIndexCommand.Execute(index);
                    }
                }
            }
        }
        e.Handled = true;
    }

    private void PlayerPanel_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                var file = files[0];
                if (MediaFileHelper.IsMediaFile(file))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
        }
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    // ========== ContextMenu 열기 버튼 클릭 핸들러 ==========

    private void SpeedButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.DataContext = DataContext;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void RotationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.DataContext = DataContext;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.DataContext = DataContext;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void RecentFilesButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.DataContext = DataContext;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void BookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.DataContext = DataContext;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            button.ContextMenu.IsOpen = true;
        }
    }
}

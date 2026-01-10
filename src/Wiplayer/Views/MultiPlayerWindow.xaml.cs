using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Wiplayer.Core.Settings;
using Wiplayer.Core.Utils;
using Wiplayer.ViewModels;

namespace Wiplayer.Views;

/// <summary>
/// 4분할 멀티 플레이어 윈도우
/// </summary>
public partial class MultiPlayerWindow : Window
{
    private MultiPlayerViewModel? _viewModel;
    private readonly DispatcherTimer _hideControlsTimer;
    private WindowState _previousWindowState;

    public MultiPlayerWindow()
    {
        InitializeComponent();

        // ViewModel 생성
        var settings = App.ServiceProvider.GetRequiredService<PlayerSettings>();
        _viewModel = new MultiPlayerViewModel(settings);
        DataContext = _viewModel;

        // 컨트롤 자동 숨김 타이머
        _hideControlsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _hideControlsTimer.Tick += (s, e) =>
        {
            _viewModel.ShowControls = false;
            _hideControlsTimer.Stop();
        };

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hideControlsTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel?.Dispose();
        _hideControlsTimer.Stop();
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        // 윈도우에 드롭된 파일은 첫 번째 빈 플레이어에 할당
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0 && MediaFileHelper.IsMediaFile(files[0]))
            {
                // 빈 플레이어 찾기
                var emptyPlayerIndex = -1;
                for (int i = 0; i < _viewModel!.Players.Count; i++)
                {
                    if (!_viewModel.Players[i].HasMedia)
                    {
                        emptyPlayerIndex = i;
                        break;
                    }
                }

                if (emptyPlayerIndex >= 0)
                {
                    _ = _viewModel.Players[emptyPlayerIndex].OpenFileAsync(files[0]);
                }
                else
                {
                    // 모두 사용 중이면 선택된 플레이어에
                    if (_viewModel.SelectedPlayer != null)
                    {
                        _ = _viewModel.SelectedPlayer.OpenFileAsync(files[0]);
                    }
                }
            }
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0 && MediaFileHelper.IsMediaFile(files[0]))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }
        }
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void PlayerPanel_Drop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string tagStr && int.TryParse(tagStr, out var panelIndex))
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files?.Length > 0 && MediaFileHelper.IsMediaFile(files[0]))
                {
                    _ = _viewModel!.Players[panelIndex].OpenFileAsync(files[0]);
                    e.Handled = true;
                }
            }
        }
    }

    private void PlayerPanel_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0 && MediaFileHelper.IsMediaFile(files[0]))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }
        }
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void PlayerPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string tagStr && int.TryParse(tagStr, out var panelIndex))
        {
            _viewModel!.SelectPlayerCommand.Execute(panelIndex);
            UpdatePlayerSelection(panelIndex);

            // 더블클릭 시 재생/일시정지
            if (e.ClickCount == 2)
            {
                _viewModel.Players[panelIndex].PlayPauseCommand.Execute(null);
            }
        }
    }

    private void UpdatePlayerSelection(int selectedIndex)
    {
        for (int i = 0; i < _viewModel!.Players.Count; i++)
        {
            _viewModel.Players[i].IsSelected = (i == selectedIndex);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel == null) return;

        switch (e.Key)
        {
            case Key.Space:
                _viewModel.SelectedPlayer?.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                if (_viewModel.IsFullscreen)
                {
                    ToggleFullscreen();
                }
                break;

            case Key.F:
            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case Key.D1:
            case Key.NumPad1:
                SelectPlayer(0);
                e.Handled = true;
                break;

            case Key.D2:
            case Key.NumPad2:
                SelectPlayer(1);
                e.Handled = true;
                break;

            case Key.D3:
            case Key.NumPad3:
                SelectPlayer(2);
                e.Handled = true;
                break;

            case Key.D4:
            case Key.NumPad4:
                SelectPlayer(3);
                e.Handled = true;
                break;

            case Key.Left:
                _ = _viewModel.SelectedPlayer?.SeekRelativeCommand.ExecuteAsync(-5.0);
                e.Handled = true;
                break;

            case Key.Right:
                _ = _viewModel.SelectedPlayer?.SeekRelativeCommand.ExecuteAsync(5.0);
                e.Handled = true;
                break;

            case Key.M:
                _viewModel.SelectedPlayer?.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.S:
                _viewModel.StopAllCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.P:
                _viewModel.PlayAllCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void SelectPlayer(int index)
    {
        _viewModel!.SelectPlayerCommand.Execute(index);
        UpdatePlayerSelection(index);
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        ShowControls();
    }

    private void ShowControls()
    {
        if (_viewModel == null) return;

        _viewModel.ShowControls = true;
        _hideControlsTimer.Stop();
        _hideControlsTimer.Start();
    }

    private void ToggleFullscreen()
    {
        if (_viewModel == null) return;

        _viewModel.IsFullscreen = !_viewModel.IsFullscreen;

        if (_viewModel.IsFullscreen)
        {
            _previousWindowState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _previousWindowState;
        }
    }
}

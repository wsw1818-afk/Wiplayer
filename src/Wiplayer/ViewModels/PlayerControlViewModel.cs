using CommunityToolkit.Mvvm.ComponentModel;

namespace Wiplayer.ViewModels;

/// <summary>
/// 플레이어 컨트롤 ViewModel (재생목록, 트랙 선택 등)
/// </summary>
public partial class PlayerControlViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isPlaylistVisible;

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private int _selectedAudioTrack = -1;

    [ObservableProperty]
    private int _selectedSubtitleTrack = -1;
}

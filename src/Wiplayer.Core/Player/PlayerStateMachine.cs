using System.Diagnostics;

namespace Wiplayer.Core.Player;

/// <summary>
/// 플레이어 상태 머신 - 상태 전이 관리
/// </summary>
public class PlayerStateMachine
{
    private PlayerState _currentState = PlayerState.Stopped;
    private readonly object _lock = new();

    /// <summary>현재 상태</summary>
    public PlayerState CurrentState
    {
        get { lock (_lock) return _currentState; }
    }

    /// <summary>상태 변경 이벤트</summary>
    public event EventHandler<PlayerStateChangedEventArgs>? StateChanged;

    /// <summary>유효한 상태 전이 정의</summary>
    private static readonly Dictionary<PlayerState, HashSet<PlayerState>> ValidTransitions = new()
    {
        [PlayerState.Stopped] = new() { PlayerState.Loading },
        [PlayerState.Loading] = new() { PlayerState.Playing, PlayerState.Paused, PlayerState.Error, PlayerState.Stopped },
        [PlayerState.Playing] = new() { PlayerState.Paused, PlayerState.Buffering, PlayerState.Ended, PlayerState.Error, PlayerState.Stopped },
        [PlayerState.Paused] = new() { PlayerState.Playing, PlayerState.Stopped, PlayerState.Error },
        [PlayerState.Buffering] = new() { PlayerState.Playing, PlayerState.Paused, PlayerState.Error, PlayerState.Stopped },
        [PlayerState.Ended] = new() { PlayerState.Playing, PlayerState.Stopped, PlayerState.Loading },
        [PlayerState.Error] = new() { PlayerState.Stopped, PlayerState.Loading }
    };

    /// <summary>
    /// 상태 전이 시도
    /// </summary>
    /// <param name="newState">새 상태</param>
    /// <returns>전이 성공 여부</returns>
    public bool TryTransition(PlayerState newState)
    {
        lock (_lock)
        {
            if (_currentState == newState)
                return true;

            if (!ValidTransitions.TryGetValue(_currentState, out var validStates) || !validStates.Contains(newState))
            {
                Debug.WriteLine($"[StateMachine] Invalid transition: {_currentState} -> {newState}");
                return false;
            }

            var oldState = _currentState;
            _currentState = newState;

            Debug.WriteLine($"[StateMachine] Transition: {oldState} -> {newState}");
            StateChanged?.Invoke(this, new PlayerStateChangedEventArgs(oldState, newState));

            return true;
        }
    }

    /// <summary>
    /// 강제 상태 변경 (오류 복구 등에 사용)
    /// </summary>
    public void ForceState(PlayerState newState)
    {
        lock (_lock)
        {
            var oldState = _currentState;
            _currentState = newState;

            Debug.WriteLine($"[StateMachine] Force transition: {oldState} -> {newState}");
            StateChanged?.Invoke(this, new PlayerStateChangedEventArgs(oldState, newState));
        }
    }

    /// <summary>
    /// 상태 초기화
    /// </summary>
    public void Reset()
    {
        ForceState(PlayerState.Stopped);
    }

    /// <summary>
    /// 특정 상태로 전이 가능한지 확인
    /// </summary>
    public bool CanTransitionTo(PlayerState newState)
    {
        lock (_lock)
        {
            if (_currentState == newState)
                return true;

            return ValidTransitions.TryGetValue(_currentState, out var validStates) && validStates.Contains(newState);
        }
    }

    /// <summary>재생 중인지 확인</summary>
    public bool IsPlaying => CurrentState == PlayerState.Playing;

    /// <summary>일시정지 중인지 확인</summary>
    public bool IsPaused => CurrentState == PlayerState.Paused;

    /// <summary>정지 상태인지 확인</summary>
    public bool IsStopped => CurrentState == PlayerState.Stopped;

    /// <summary>활성 상태인지 확인 (재생 또는 일시정지)</summary>
    public bool IsActive => CurrentState is PlayerState.Playing or PlayerState.Paused or PlayerState.Buffering;
}

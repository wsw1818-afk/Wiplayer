using System.Diagnostics;

namespace Wiplayer.Core.Player;

/// <summary>
/// 재생 시계 - A/V 동기화를 위한 마스터 클럭 (lock 최적화)
/// </summary>
public class PlaybackClock
{
    private readonly Stopwatch _stopwatch = new();
    private double _baseTime;
    private double _playbackSpeed = 1.0;
    private readonly object _lock = new();

    /// <summary>현재 재생 시간 (초) - 읽기 최적화</summary>
    public double CurrentTime
    {
        get
        {
            // Stopwatch.Elapsed는 thread-safe
            if (!_stopwatch.IsRunning)
            {
                lock (_lock) { return _baseTime; }
            }

            double baseTime, speed;
            lock (_lock)
            {
                baseTime = _baseTime;
                speed = _playbackSpeed;
            }
            return baseTime + (_stopwatch.Elapsed.TotalSeconds * speed);
        }
    }

    /// <summary>재생 속도 (0.2x ~ 4.0x)</summary>
    public double PlaybackSpeed
    {
        get { lock (_lock) { return _playbackSpeed; } }
        set
        {
            lock (_lock)
            {
                if (value < 0.2 || value > 4.0)
                    throw new ArgumentOutOfRangeException(nameof(value), "재생 속도는 0.2 ~ 4.0 사이여야 합니다.");

                // 현재 시간 저장 후 속도 변경
                _baseTime = CurrentTime;
                _stopwatch.Restart();
                _playbackSpeed = value;
            }
        }
    }

    /// <summary>재생 중인지 확인</summary>
    public bool IsRunning => _stopwatch.IsRunning;

    /// <summary>시계 시작/재개</summary>
    public void Start()
    {
        lock (_lock)
        {
            _stopwatch.Start();
        }
    }

    /// <summary>시계 일시정지</summary>
    public void Pause()
    {
        lock (_lock)
        {
            _baseTime = CurrentTime;
            _stopwatch.Stop();
        }
    }

    /// <summary>시계 정지 및 초기화</summary>
    public void Stop()
    {
        lock (_lock)
        {
            _stopwatch.Reset();
            _baseTime = 0;
        }
    }

    /// <summary>특정 시간으로 이동</summary>
    public void Seek(double time)
    {
        lock (_lock)
        {
            _baseTime = Math.Max(0, time);
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Restart();
            }
            else
            {
                _stopwatch.Reset();
            }
        }
    }

    /// <summary>상대적 시간 이동</summary>
    public void SeekRelative(double offset)
    {
        Seek(CurrentTime + offset);
    }
}

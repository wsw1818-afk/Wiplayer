namespace Wiplayer.Core.Player;

/// <summary>
/// A-B 구간 반복 기능
/// </summary>
public class ABRepeat
{
    private double? _pointA;
    private double? _pointB;

    /// <summary>A 지점 (시작)</summary>
    public double? PointA => _pointA;

    /// <summary>B 지점 (끝)</summary>
    public double? PointB => _pointB;

    /// <summary>구간 반복이 활성화되었는지</summary>
    public bool IsActive => _pointA.HasValue && _pointB.HasValue;

    /// <summary>구간 반복 상태 변경 이벤트</summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// A 또는 B 지점 설정 (토글 방식)
    /// - A가 없으면 A 설정
    /// - A만 있으면 B 설정
    /// - 둘 다 있으면 초기화
    /// </summary>
    public void Toggle(double currentTime)
    {
        if (!_pointA.HasValue)
        {
            _pointA = currentTime;
        }
        else if (!_pointB.HasValue)
        {
            // B는 A보다 커야 함
            if (currentTime > _pointA.Value)
            {
                _pointB = currentTime;
            }
            else
            {
                // A와 B를 교환
                _pointB = _pointA;
                _pointA = currentTime;
            }
        }
        else
        {
            Clear();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A 지점만 설정</summary>
    public void SetPointA(double time)
    {
        _pointA = time;
        // B가 A보다 작으면 B 초기화
        if (_pointB.HasValue && _pointB.Value <= time)
            _pointB = null;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>B 지점만 설정</summary>
    public void SetPointB(double time)
    {
        if (!_pointA.HasValue)
            return;

        if (time > _pointA.Value)
            _pointB = time;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>구간 반복 초기화</summary>
    public void Clear()
    {
        _pointA = null;
        _pointB = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 현재 시간이 B 지점을 넘었는지 확인하고 A 지점으로 돌아가야 하는지 반환
    /// </summary>
    public bool ShouldLoop(double currentTime)
    {
        if (!IsActive)
            return false;

        return currentTime >= _pointB!.Value;
    }

    /// <summary>구간 길이</summary>
    public double? Duration => IsActive ? _pointB - _pointA : null;

    /// <summary>상태 문자열</summary>
    public string StatusText
    {
        get
        {
            if (!_pointA.HasValue)
                return "구간 반복: 꺼짐";
            if (!_pointB.HasValue)
                return $"구간 반복: A={FormatTime(_pointA.Value)} (B 설정 대기)";
            return $"구간 반복: {FormatTime(_pointA.Value)} - {FormatTime(_pointB.Value)}";
        }
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}

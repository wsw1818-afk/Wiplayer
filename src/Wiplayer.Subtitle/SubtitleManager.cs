using Wiplayer.Subtitle.Parsers;

namespace Wiplayer.Subtitle;

/// <summary>
/// 자막 관리자
/// </summary>
public class SubtitleManager
{
    private readonly List<ISubtitleParser> _parsers = new();
    private List<SubtitleEntry> _entries = new();
    private int _currentIndex = -1;

    /// <summary>현재 로드된 자막 파일 경로</summary>
    public string? CurrentPath { get; private set; }

    /// <summary>자막이 로드되었는지</summary>
    public bool IsLoaded => _entries.Count > 0;

    /// <summary>자막 항목 수</summary>
    public int Count => _entries.Count;

    /// <summary>싱크 오프셋 (밀리초)</summary>
    public int SyncOffset { get; set; } = 0;

    /// <summary>자막 표시 여부</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>현재 표시 중인 자막</summary>
    public SubtitleEntry? CurrentEntry { get; private set; }

    /// <summary>자막 변경 이벤트</summary>
    public event EventHandler<SubtitleEntry?>? SubtitleChanged;

    public SubtitleManager()
    {
        // 기본 파서 등록
        _parsers.Add(new SrtParser());
        _parsers.Add(new AssParser());
    }

    /// <summary>
    /// 자막 파일 로드
    /// </summary>
    public bool Load(string path)
    {
        try
        {
            var parser = _parsers.FirstOrDefault(p => p.CanParse(path));
            if (parser == null)
            {
                return false;
            }

            _entries = parser.Parse(path);
            CurrentPath = path;
            _currentIndex = -1;
            CurrentEntry = null;

            return _entries.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 자막 문자열 로드
    /// </summary>
    public bool LoadFromContent(string content, string format)
    {
        try
        {
            ISubtitleParser? parser = format.ToLowerInvariant() switch
            {
                "srt" => new SrtParser(),
                "ass" or "ssa" => new AssParser(),
                _ => null
            };

            if (parser == null)
                return false;

            _entries = parser.ParseContent(content);
            CurrentPath = null;
            _currentIndex = -1;
            CurrentEntry = null;

            return _entries.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 자막 언로드
    /// </summary>
    public void Unload()
    {
        _entries.Clear();
        CurrentPath = null;
        _currentIndex = -1;
        CurrentEntry = null;
        SubtitleChanged?.Invoke(this, null);
    }

    /// <summary>
    /// 현재 시간에 맞는 자막 가져오기
    /// </summary>
    public SubtitleEntry? GetSubtitleAt(double time)
    {
        if (!IsVisible || _entries.Count == 0)
            return null;

        // 싱크 오프셋 적용
        var adjustedTime = time - (SyncOffset / 1000.0);

        // 현재 인덱스 근처에서 먼저 검색 (성능 최적화)
        if (_currentIndex >= 0 && _currentIndex < _entries.Count)
        {
            var current = _entries[_currentIndex];
            if (current.IsActiveAt(adjustedTime))
            {
                UpdateCurrentEntry(current);
                return current;
            }

            // 다음 자막 확인
            if (_currentIndex + 1 < _entries.Count)
            {
                var next = _entries[_currentIndex + 1];
                if (next.IsActiveAt(adjustedTime))
                {
                    _currentIndex++;
                    UpdateCurrentEntry(next);
                    return next;
                }
            }
        }

        // 이진 검색으로 적절한 자막 찾기
        var index = FindSubtitleIndex(adjustedTime);
        if (index >= 0)
        {
            _currentIndex = index;
            var entry = _entries[index];
            UpdateCurrentEntry(entry);
            return entry;
        }

        // 자막 없음
        UpdateCurrentEntry(null);
        return null;
    }

    /// <summary>
    /// 특정 시간에 활성화된 모든 자막 가져오기 (겹치는 자막 지원)
    /// </summary>
    public IEnumerable<SubtitleEntry> GetAllSubtitlesAt(double time)
    {
        if (!IsVisible || _entries.Count == 0)
            yield break;

        var adjustedTime = time - (SyncOffset / 1000.0);

        foreach (var entry in _entries)
        {
            if (entry.IsActiveAt(adjustedTime))
            {
                yield return entry;
            }
            else if (entry.StartTime > adjustedTime)
            {
                // 정렬되어 있으므로 이후 자막은 확인 불필요
                break;
            }
        }
    }

    private int FindSubtitleIndex(double time)
    {
        int left = 0;
        int right = _entries.Count - 1;
        int result = -1;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;
            var entry = _entries[mid];

            if (entry.IsActiveAt(time))
            {
                result = mid;
                break;
            }
            else if (entry.EndTime < time)
            {
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }

        return result;
    }

    private void UpdateCurrentEntry(SubtitleEntry? entry)
    {
        if (CurrentEntry != entry)
        {
            CurrentEntry = entry;
            SubtitleChanged?.Invoke(this, entry);
        }
    }

    /// <summary>
    /// 싱크 조절 (밀리초 단위)
    /// </summary>
    public void AdjustSync(int offsetMs)
    {
        SyncOffset += offsetMs;
    }

    /// <summary>
    /// 싱크 초기화
    /// </summary>
    public void ResetSync()
    {
        SyncOffset = 0;
    }

    /// <summary>
    /// 다음 자막으로 이동 (시간 반환)
    /// </summary>
    public double? GetNextSubtitleTime(double currentTime)
    {
        var adjustedTime = currentTime - (SyncOffset / 1000.0);

        foreach (var entry in _entries)
        {
            if (entry.StartTime > adjustedTime)
            {
                return entry.StartTime + (SyncOffset / 1000.0);
            }
        }

        return null;
    }

    /// <summary>
    /// 이전 자막으로 이동 (시간 반환)
    /// </summary>
    public double? GetPreviousSubtitleTime(double currentTime)
    {
        var adjustedTime = currentTime - (SyncOffset / 1000.0);

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].StartTime < adjustedTime - 0.5) // 0.5초 이전 자막
            {
                return _entries[i].StartTime + (SyncOffset / 1000.0);
            }
        }

        return null;
    }

    /// <summary>
    /// 자막 텍스트 검색
    /// </summary>
    public IEnumerable<SubtitleEntry> Search(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            yield break;

        foreach (var entry in _entries)
        {
            if (entry.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                yield return entry;
            }
        }
    }
}

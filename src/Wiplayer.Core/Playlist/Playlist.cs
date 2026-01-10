using System.Collections.ObjectModel;

namespace Wiplayer.Core.Playlist;

/// <summary>
/// 재생목록
/// </summary>
public class Playlist
{
    private readonly List<PlaylistItem> _items = new();
    private int _currentIndex = -1;
    private readonly Random _random = new();

    /// <summary>재생목록 이름</summary>
    public string Name { get; set; } = "새 재생목록";

    /// <summary>재생목록 항목들 (읽기 전용)</summary>
    public IReadOnlyList<PlaylistItem> Items => _items.AsReadOnly();

    /// <summary>현재 재생 중인 인덱스</summary>
    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            if (value >= -1 && value < _items.Count)
            {
                _currentIndex = value;
                CurrentItemChanged?.Invoke(this, CurrentItem);
            }
        }
    }

    /// <summary>현재 재생 중인 항목</summary>
    public PlaylistItem? CurrentItem => _currentIndex >= 0 && _currentIndex < _items.Count ? _items[_currentIndex] : null;

    /// <summary>항목 수</summary>
    public int Count => _items.Count;

    /// <summary>비어있는지</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>셔플 모드</summary>
    public bool ShuffleEnabled { get; set; } = false;

    /// <summary>반복 모드</summary>
    public RepeatMode RepeatMode { get; set; } = RepeatMode.None;

    /// <summary>셔플 시 재생 순서</summary>
    private List<int>? _shuffleOrder;
    private int _shuffleIndex = -1;

    /// <summary>항목 변경 이벤트</summary>
    public event EventHandler<PlaylistItem?>? CurrentItemChanged;

    /// <summary>재생목록 변경 이벤트</summary>
    public event EventHandler? PlaylistChanged;

    /// <summary>항목 추가</summary>
    public void Add(PlaylistItem item)
    {
        _items.Add(item);
        InvalidateShuffleOrder();
        PlaylistChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>항목 추가 (경로로)</summary>
    public PlaylistItem Add(string path)
    {
        var item = new PlaylistItem
        {
            Path = path,
            Title = Path.GetFileNameWithoutExtension(path)
        };
        Add(item);
        return item;
    }

    /// <summary>여러 항목 추가</summary>
    public void AddRange(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            _items.Add(new PlaylistItem
            {
                Path = path,
                Title = Path.GetFileNameWithoutExtension(path)
            });
        }
        InvalidateShuffleOrder();
        PlaylistChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>항목 제거</summary>
    public void Remove(PlaylistItem item)
    {
        var index = _items.IndexOf(item);
        if (index < 0) return;

        _items.RemoveAt(index);

        if (index < _currentIndex)
            _currentIndex--;
        else if (index == _currentIndex)
            _currentIndex = Math.Min(_currentIndex, _items.Count - 1);

        InvalidateShuffleOrder();
        PlaylistChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>인덱스로 항목 제거</summary>
    public void RemoveAt(int index)
    {
        if (index >= 0 && index < _items.Count)
            Remove(_items[index]);
    }

    /// <summary>모든 항목 제거</summary>
    public void Clear()
    {
        _items.Clear();
        _currentIndex = -1;
        InvalidateShuffleOrder();
        PlaylistChanged?.Invoke(this, EventArgs.Empty);
        CurrentItemChanged?.Invoke(this, null);
    }

    /// <summary>항목 이동</summary>
    public void Move(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= _items.Count) return;
        if (newIndex < 0 || newIndex >= _items.Count) return;

        var item = _items[oldIndex];
        _items.RemoveAt(oldIndex);
        _items.Insert(newIndex, item);

        // 현재 인덱스 조정
        if (_currentIndex == oldIndex)
            _currentIndex = newIndex;
        else if (oldIndex < _currentIndex && newIndex >= _currentIndex)
            _currentIndex--;
        else if (oldIndex > _currentIndex && newIndex <= _currentIndex)
            _currentIndex++;

        InvalidateShuffleOrder();
        PlaylistChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>다음 항목 가져오기</summary>
    public PlaylistItem? GetNext()
    {
        if (_items.Count == 0) return null;

        if (ShuffleEnabled)
            return GetNextShuffle();

        var nextIndex = _currentIndex + 1;

        if (nextIndex >= _items.Count)
        {
            if (RepeatMode == RepeatMode.All)
                nextIndex = 0;
            else
                return null;
        }

        return _items[nextIndex];
    }

    /// <summary>이전 항목 가져오기</summary>
    public PlaylistItem? GetPrevious()
    {
        if (_items.Count == 0) return null;

        if (ShuffleEnabled)
            return GetPreviousShuffle();

        var prevIndex = _currentIndex - 1;

        if (prevIndex < 0)
        {
            if (RepeatMode == RepeatMode.All)
                prevIndex = _items.Count - 1;
            else
                return null;
        }

        return _items[prevIndex];
    }

    /// <summary>다음 항목으로 이동</summary>
    public bool MoveNext()
    {
        var next = GetNext();
        if (next == null) return false;

        CurrentIndex = _items.IndexOf(next);
        if (ShuffleEnabled)
            _shuffleIndex++;

        return true;
    }

    /// <summary>이전 항목으로 이동</summary>
    public bool MovePrevious()
    {
        var prev = GetPrevious();
        if (prev == null) return false;

        CurrentIndex = _items.IndexOf(prev);
        if (ShuffleEnabled)
            _shuffleIndex--;

        return true;
    }

    /// <summary>특정 인덱스로 이동</summary>
    public bool MoveTo(int index)
    {
        if (index < 0 || index >= _items.Count)
            return false;

        CurrentIndex = index;
        return true;
    }

    /// <summary>특정 항목으로 이동</summary>
    public bool MoveTo(PlaylistItem item)
    {
        var index = _items.IndexOf(item);
        return MoveTo(index);
    }

    private void InvalidateShuffleOrder()
    {
        _shuffleOrder = null;
        _shuffleIndex = -1;
    }

    private void EnsureShuffleOrder()
    {
        if (_shuffleOrder != null && _shuffleOrder.Count == _items.Count)
            return;

        _shuffleOrder = Enumerable.Range(0, _items.Count).OrderBy(_ => _random.Next()).ToList();
        _shuffleIndex = _currentIndex >= 0 ? _shuffleOrder.IndexOf(_currentIndex) : -1;
    }

    private PlaylistItem? GetNextShuffle()
    {
        EnsureShuffleOrder();
        if (_shuffleOrder == null || _shuffleOrder.Count == 0) return null;

        var nextShuffleIndex = _shuffleIndex + 1;
        if (nextShuffleIndex >= _shuffleOrder.Count)
        {
            if (RepeatMode == RepeatMode.All)
            {
                // 다시 섞기
                _shuffleOrder = Enumerable.Range(0, _items.Count).OrderBy(_ => _random.Next()).ToList();
                nextShuffleIndex = 0;
            }
            else
            {
                return null;
            }
        }

        return _items[_shuffleOrder[nextShuffleIndex]];
    }

    private PlaylistItem? GetPreviousShuffle()
    {
        EnsureShuffleOrder();
        if (_shuffleOrder == null || _shuffleOrder.Count == 0) return null;

        var prevShuffleIndex = _shuffleIndex - 1;
        if (prevShuffleIndex < 0)
        {
            if (RepeatMode == RepeatMode.All)
                prevShuffleIndex = _shuffleOrder.Count - 1;
            else
                return null;
        }

        return _items[_shuffleOrder[prevShuffleIndex]];
    }

    /// <summary>경로로 항목 찾기</summary>
    public PlaylistItem? FindByPath(string path)
    {
        return _items.FirstOrDefault(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>ID로 항목 찾기</summary>
    public PlaylistItem? FindById(string id)
    {
        return _items.FirstOrDefault(i => i.Id == id);
    }

    /// <summary>총 재생 시간</summary>
    public TimeSpan TotalDuration => TimeSpan.FromSeconds(_items.Sum(i => i.Duration));
}

/// <summary>반복 모드</summary>
public enum RepeatMode
{
    /// <summary>반복 없음</summary>
    None,

    /// <summary>한 곡 반복</summary>
    One,

    /// <summary>전체 반복</summary>
    All
}

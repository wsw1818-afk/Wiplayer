using System.Text.Json;

namespace Wiplayer.Core.Playlist;

/// <summary>
/// 최근 재생 파일 관리
/// </summary>
public class RecentFiles
{
    private readonly List<PlaylistItem> _items = new();
    private readonly int _maxCount;
    private readonly string _filePath;

    /// <summary>최근 재생 파일 목록 (최신순)</summary>
    public IReadOnlyList<PlaylistItem> Items => _items.AsReadOnly();

    /// <summary>목록 변경 이벤트</summary>
    public event EventHandler? ListChanged;

    public RecentFiles(int maxCount = 20)
    {
        _maxCount = maxCount;
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Wiplayer", "recent.json");

        Load();
    }

    /// <summary>파일 추가/업데이트</summary>
    public void Add(string path, double duration = 0, double lastPosition = 0)
    {
        // 기존 항목 제거
        var existing = _items.FirstOrDefault(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            _items.Remove(existing);
            existing.LastPlayedAt = DateTime.Now;
            existing.PlayCount++;
            if (duration > 0) existing.Duration = duration;
            if (lastPosition > 0) existing.LastPosition = lastPosition;
            _items.Insert(0, existing);
        }
        else
        {
            var item = new PlaylistItem
            {
                Path = path,
                Title = Path.GetFileNameWithoutExtension(path),
                Duration = duration,
                LastPosition = lastPosition,
                LastPlayedAt = DateTime.Now,
                PlayCount = 1
            };
            _items.Insert(0, item);
        }

        // 최대 개수 제한
        while (_items.Count > _maxCount)
        {
            _items.RemoveAt(_items.Count - 1);
        }

        Save();
        ListChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>재생 위치 업데이트</summary>
    public void UpdatePosition(string path, double position)
    {
        var item = _items.FirstOrDefault(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            item.LastPosition = position;
            Save();
        }
    }

    /// <summary>재생 시간 업데이트</summary>
    public void UpdateDuration(string path, double duration)
    {
        var item = _items.FirstOrDefault(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            item.Duration = duration;
            Save();
        }
    }

    /// <summary>항목 제거</summary>
    public void Remove(string path)
    {
        var item = _items.FirstOrDefault(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            _items.Remove(item);
            Save();
            ListChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>모든 항목 제거</summary>
    public void Clear()
    {
        _items.Clear();
        Save();
        ListChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>존재하지 않는 파일 제거</summary>
    public void RemoveInvalidFiles()
    {
        var removed = _items.RemoveAll(i => !i.Exists);
        if (removed > 0)
        {
            Save();
            ListChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>마지막 재생 위치 가져오기</summary>
    public double GetLastPosition(string path)
    {
        var item = _items.FirstOrDefault(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        return item?.LastPosition ?? 0;
    }

    /// <summary>이어보기 가능한 항목 목록</summary>
    public IEnumerable<PlaylistItem> GetResumableItems()
    {
        return _items.Where(i => i.CanResume && i.Exists);
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var items = JsonSerializer.Deserialize<List<PlaylistItem>>(json);
                if (items != null)
                {
                    _items.Clear();
                    _items.AddRange(items.Take(_maxCount));
                }
            }
        }
        catch
        {
            // 파일 손상 시 무시
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_items, options);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // 저장 실패 무시
        }
    }
}

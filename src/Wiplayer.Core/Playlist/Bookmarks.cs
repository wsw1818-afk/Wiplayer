using System.Text.Json;

namespace Wiplayer.Core.Playlist;

/// <summary>
/// 북마크 항목
/// </summary>
public class BookmarkItem
{
    /// <summary>미디어 파일 경로</summary>
    public string MediaPath { get; set; } = string.Empty;

    /// <summary>북마크 위치 (초)</summary>
    public double Position { get; set; }

    /// <summary>북마크 제목</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>북마크 설명</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>생성 시각</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>파일명 (읽기 전용)</summary>
    public string FileName => Path.GetFileName(MediaPath);

    /// <summary>위치 표시 텍스트</summary>
    public string PositionText
    {
        get
        {
            var ts = TimeSpan.FromSeconds(Position);
            return ts.Hours > 0
                ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
    }
}

/// <summary>
/// 북마크 관리자
/// </summary>
public class Bookmarks
{
    private readonly List<BookmarkItem> _items = new();
    private readonly string _filePath;

    /// <summary>북마크 목록</summary>
    public IReadOnlyList<BookmarkItem> Items => _items.AsReadOnly();

    /// <summary>목록 변경 이벤트</summary>
    public event EventHandler? ListChanged;

    public Bookmarks()
    {
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Wiplayer", "bookmarks.json");

        Load();
    }

    /// <summary>북마크 추가</summary>
    public void Add(string mediaPath, double position, string? title = null, string? description = null)
    {
        var item = new BookmarkItem
        {
            MediaPath = mediaPath,
            Position = position,
            Title = title ?? $"{Path.GetFileNameWithoutExtension(mediaPath)} - {TimeSpan.FromSeconds(position):hh\\:mm\\:ss}",
            Description = description ?? string.Empty,
            CreatedAt = DateTime.Now
        };

        _items.Insert(0, item);
        Save();
        ListChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>북마크 제거</summary>
    public void Remove(BookmarkItem item)
    {
        if (_items.Remove(item))
        {
            Save();
            ListChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>북마크 제거 (인덱스)</summary>
    public void RemoveAt(int index)
    {
        if (index >= 0 && index < _items.Count)
        {
            _items.RemoveAt(index);
            Save();
            ListChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>특정 미디어의 북마크 목록</summary>
    public IEnumerable<BookmarkItem> GetBookmarksForMedia(string mediaPath)
    {
        return _items.Where(i => i.MediaPath.Equals(mediaPath, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(i => i.Position);
    }

    /// <summary>모든 북마크 제거</summary>
    public void Clear()
    {
        _items.Clear();
        Save();
        ListChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var items = JsonSerializer.Deserialize<List<BookmarkItem>>(json);
                if (items != null)
                {
                    _items.Clear();
                    _items.AddRange(items);
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

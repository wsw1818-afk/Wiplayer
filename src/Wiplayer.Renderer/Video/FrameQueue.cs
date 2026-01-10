using System.Collections.Concurrent;

namespace Wiplayer.Renderer.Video;

/// <summary>
/// 프레임 큐 (디코더와 렌더러 사이의 버퍼)
/// </summary>
public class FrameQueue<T> : IDisposable where T : class
{
    private readonly ConcurrentQueue<T> _queue = new();
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxSize;
    private volatile bool _disposed = false;

    /// <summary>현재 큐에 있는 프레임 수</summary>
    public int Count => _queue.Count;

    /// <summary>큐가 가득 찼는지</summary>
    public bool IsFull => _queue.Count >= _maxSize;

    /// <summary>큐가 비었는지</summary>
    public bool IsEmpty => _queue.IsEmpty;

    /// <summary>최대 크기</summary>
    public int MaxSize => _maxSize;

    /// <summary>
    /// 프레임 큐 생성 (대용량 미디어 최적화: 기본 120프레임 = 약 4초 버퍼)
    /// </summary>
    public FrameQueue(int maxSize = 120)
    {
        _maxSize = maxSize;
        _semaphore = new SemaphoreSlim(0, maxSize);
    }

    /// <summary>
    /// 프레임 추가 (큐가 가득 차면 false 반환)
    /// </summary>
    public bool TryEnqueue(T frame)
    {
        if (_disposed || _queue.Count >= _maxSize)
            return false;

        _queue.Enqueue(frame);
        _semaphore.Release();
        return true;
    }

    /// <summary>
    /// 프레임 추가 (큐가 가득 차면 대기)
    /// </summary>
    public async Task EnqueueAsync(T frame, CancellationToken cancellationToken = default)
    {
        while (!_disposed && _queue.Count >= _maxSize)
        {
            await Task.Delay(1, cancellationToken);
        }

        if (!_disposed)
        {
            _queue.Enqueue(frame);
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 프레임 가져오기 (없으면 false 반환)
    /// </summary>
    public bool TryDequeue(out T? frame)
    {
        frame = null;
        if (_disposed)
            return false;

        return _queue.TryDequeue(out frame);
    }

    /// <summary>
    /// 프레임 가져오기 (대기)
    /// </summary>
    public async Task<T?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return null;

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            if (_queue.TryDequeue(out var frame))
                return frame;
        }
        catch (OperationCanceledException)
        {
            // 취소됨
        }

        return null;
    }

    /// <summary>
    /// 프레임 가져오기 (타임아웃)
    /// </summary>
    public T? Dequeue(int timeoutMs = 100)
    {
        if (_disposed)
            return null;

        if (_semaphore.Wait(timeoutMs))
        {
            if (_queue.TryDequeue(out var frame))
                return frame;
        }

        return null;
    }

    /// <summary>
    /// 다음 프레임 미리보기 (제거하지 않음)
    /// </summary>
    public bool TryPeek(out T? frame)
    {
        frame = null;
        if (_disposed)
            return false;

        return _queue.TryPeek(out frame);
    }

    /// <summary>
    /// 큐 비우기
    /// </summary>
    public void Clear()
    {
        while (_queue.TryDequeue(out var frame))
        {
            if (frame is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        // 세마포어 초기화
        while (_semaphore.CurrentCount > 0)
        {
            _semaphore.Wait(0);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Clear();
            _semaphore.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

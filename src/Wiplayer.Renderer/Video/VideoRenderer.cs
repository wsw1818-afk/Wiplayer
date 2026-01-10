using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Wiplayer.Renderer.Video;

/// <summary>
/// WPF용 비디오 렌더러 (WriteableBitmap 기반)
/// </summary>
public class VideoRenderer : IDisposable
{
    private WriteableBitmap? _bitmap;
    private bool _disposed = false;
    private readonly object _lock = new();
    private byte[]? _filterBuffer; // 필터용 재사용 버퍼

    /// <summary>현재 비트맵 (UI에 바인딩)</summary>
    public WriteableBitmap? Bitmap => _bitmap;

    /// <summary>비디오 너비</summary>
    public int Width { get; private set; }

    /// <summary>비디오 높이</summary>
    public int Height { get; private set; }

    /// <summary>밝기 (-100 ~ 100)</summary>
    public int Brightness { get; set; } = 0;

    /// <summary>대비 (-100 ~ 100)</summary>
    public int Contrast { get; set; } = 0;

    /// <summary>채도 (-100 ~ 100)</summary>
    public int Saturation { get; set; } = 0;

    /// <summary>프레임 렌더링 완료 이벤트</summary>
    public event EventHandler? FrameRendered;

    /// <summary>
    /// 렌더러 초기화
    /// </summary>
    public void Initialize(int width, int height)
    {
        lock (_lock)
        {
            Width = width;
            Height = height;

            // UI 스레드에서 비트맵 생성
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            });
        }
    }

    /// <summary>
    /// 프레임 렌더링
    /// </summary>
    public void RenderFrame(VideoFrame frame)
    {
        if (_disposed || _bitmap == null)
            return;

        lock (_lock)
        {
            // 크기가 다르면 비트맵 재생성
            if (frame.Width != Width || frame.Height != Height)
            {
                Initialize(frame.Width, frame.Height);
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_bitmap == null) return;

                _bitmap.Lock();
                try
                {
                    var rect = new Int32Rect(0, 0, frame.Width, frame.Height);
                    _bitmap.WritePixels(rect, frame.Data, frame.Stride, 0);
                }
                finally
                {
                    _bitmap.Unlock();
                }
            });

            FrameRendered?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 프레임 렌더링 (바이트 배열 직접 전달)
    /// </summary>
    public void RenderFrame(byte[] bgraData, int width, int height)
    {
        if (_disposed || bgraData == null)
            return;

        lock (_lock)
        {
            if (width != Width || height != Height || _bitmap == null)
            {
                Initialize(width, height);
            }

            // 필터 적용
            byte[] processedData = bgraData;
            if (Brightness != 0 || Contrast != 0 || Saturation != 0)
            {
                processedData = ApplyFilters(bgraData, width, height);
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_bitmap == null) return;

                _bitmap.Lock();
                try
                {
                    var rect = new Int32Rect(0, 0, width, height);
                    var stride = width * 4;
                    _bitmap.WritePixels(rect, processedData, stride, 0);
                }
                finally
                {
                    _bitmap.Unlock();
                }
            });

            FrameRendered?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 밝기/대비/채도 필터 적용
    /// </summary>
    private byte[] ApplyFilters(byte[] bgraData, int width, int height)
    {
        // 버퍼 재사용 (GC 압력 감소)
        if (_filterBuffer == null || _filterBuffer.Length != bgraData.Length)
        {
            _filterBuffer = new byte[bgraData.Length];
        }
        var result = _filterBuffer;

        // 대비 계수 계산 (0.5 ~ 2.0 범위)
        double contrastFactor = (100.0 + Contrast) / 100.0;
        contrastFactor = contrastFactor * contrastFactor; // 비선형 적용

        // 채도 계수 계산 (0.0 ~ 2.0 범위)
        double saturationFactor = (100.0 + Saturation) / 100.0;

        for (int i = 0; i < bgraData.Length; i += 4)
        {
            // BGRA 순서
            double b = bgraData[i];
            double g = bgraData[i + 1];
            double r = bgraData[i + 2];
            byte a = bgraData[i + 3];

            // 1. 밝기 적용
            r += Brightness * 2.55;
            g += Brightness * 2.55;
            b += Brightness * 2.55;

            // 2. 대비 적용
            r = ((r - 128) * contrastFactor) + 128;
            g = ((g - 128) * contrastFactor) + 128;
            b = ((b - 128) * contrastFactor) + 128;

            // 3. 채도 적용 (HSL 변환 없이 간단한 방법)
            if (Saturation != 0)
            {
                double gray = 0.299 * r + 0.587 * g + 0.114 * b;
                r = gray + (r - gray) * saturationFactor;
                g = gray + (g - gray) * saturationFactor;
                b = gray + (b - gray) * saturationFactor;
            }

            // 클램핑
            result[i] = (byte)Math.Clamp(b, 0, 255);
            result[i + 1] = (byte)Math.Clamp(g, 0, 255);
            result[i + 2] = (byte)Math.Clamp(r, 0, 255);
            result[i + 3] = a;
        }

        return result;
    }

    /// <summary>
    /// 화면 지우기 (검은색)
    /// </summary>
    public void Clear()
    {
        if (_bitmap == null) return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            _bitmap.Lock();
            try
            {
                var blackData = new byte[Width * Height * 4];
                var rect = new Int32Rect(0, 0, Width, Height);
                _bitmap.WritePixels(rect, blackData, Width * 4, 0);
            }
            finally
            {
                _bitmap.Unlock();
            }
        });
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _bitmap = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

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
    private byte[]? _clearBuffer;  // Clear용 재사용 버퍼

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

        // 크기 체크 및 초기화
        lock (_lock)
        {
            if (width != Width || height != Height || _bitmap == null)
            {
                Initialize(width, height);
            }
        }

        // 필터 적용 (lock 외부에서 CPU 작업)
        byte[] processedData = bgraData;
        if (Brightness != 0 || Contrast != 0 || Saturation != 0)
        {
            processedData = ApplyFilters(bgraData, width, height);
        }

        // 캡처용 로컬 변수
        var dataToRender = processedData;
        var w = width;
        var h = height;

        // 비동기 렌더링 (블로킹 제거)
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            lock (_lock)
            {
                if (_bitmap == null || _disposed) return;

                _bitmap.Lock();
                try
                {
                    var rect = new Int32Rect(0, 0, w, h);
                    var stride = w * 4;
                    _bitmap.WritePixels(rect, dataToRender, stride, 0);
                }
                finally
                {
                    _bitmap.Unlock();
                }
            }

            FrameRendered?.Invoke(this, EventArgs.Empty);
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    /// <summary>
    /// 밝기/대비/채도 필터 적용 (unsafe 최적화)
    /// </summary>
    private unsafe byte[] ApplyFilters(byte[] bgraData, int width, int height)
    {
        // 버퍼 재사용 (GC 압력 감소)
        if (_filterBuffer == null || _filterBuffer.Length != bgraData.Length)
        {
            _filterBuffer = new byte[bgraData.Length];
        }

        // 정수 연산으로 변환 (256 스케일)
        int brightnessOffset = (Brightness * 255) / 100;
        int contrastFactor256 = (int)(((100.0 + Contrast) / 100.0) * ((100.0 + Contrast) / 100.0) * 256);
        int saturationFactor256 = (int)(((100.0 + Saturation) / 100.0) * 256);
        bool applySaturation = Saturation != 0;

        fixed (byte* srcPtr = bgraData, dstPtr = _filterBuffer)
        {
            byte* src = srcPtr;
            byte* dst = dstPtr;
            byte* end = srcPtr + bgraData.Length;

            while (src < end)
            {
                int b = src[0];
                int g = src[1];
                int r = src[2];
                byte a = src[3];

                // 1. 밝기 적용
                r += brightnessOffset;
                g += brightnessOffset;
                b += brightnessOffset;

                // 2. 대비 적용 (정수 연산)
                r = (((r - 128) * contrastFactor256) >> 8) + 128;
                g = (((g - 128) * contrastFactor256) >> 8) + 128;
                b = (((b - 128) * contrastFactor256) >> 8) + 128;

                // 3. 채도 적용
                if (applySaturation)
                {
                    int gray = (r * 77 + g * 150 + b * 29) >> 8; // 0.299, 0.587, 0.114
                    r = gray + (((r - gray) * saturationFactor256) >> 8);
                    g = gray + (((g - gray) * saturationFactor256) >> 8);
                    b = gray + (((b - gray) * saturationFactor256) >> 8);
                }

                // 클램핑 (분기 없는 버전)
                dst[0] = (byte)(b < 0 ? 0 : (b > 255 ? 255 : b));
                dst[1] = (byte)(g < 0 ? 0 : (g > 255 ? 255 : g));
                dst[2] = (byte)(r < 0 ? 0 : (r > 255 ? 255 : r));
                dst[3] = a;

                src += 4;
                dst += 4;
            }
        }

        return _filterBuffer;
    }

    /// <summary>
    /// 화면 지우기 (검은색)
    /// </summary>
    public void Clear()
    {
        if (_bitmap == null) return;

        // 버퍼 재사용
        int requiredSize = Width * Height * 4;
        if (_clearBuffer == null || _clearBuffer.Length != requiredSize)
        {
            _clearBuffer = new byte[requiredSize]; // 0으로 자동 초기화
        }

        var w = Width;
        var h = Height;
        var buffer = _clearBuffer;

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            lock (_lock)
            {
                if (_bitmap == null) return;

                _bitmap.Lock();
                try
                {
                    var rect = new Int32Rect(0, 0, w, h);
                    _bitmap.WritePixels(rect, buffer, w * 4, 0);
                }
                finally
                {
                    _bitmap.Unlock();
                }
            }
        }, System.Windows.Threading.DispatcherPriority.Normal);
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

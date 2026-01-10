namespace Wiplayer.Renderer.Video;

/// <summary>
/// 비디오 프레임 데이터
/// </summary>
public class VideoFrame : IDisposable
{
    /// <summary>프레임 너비</summary>
    public int Width { get; }

    /// <summary>프레임 높이</summary>
    public int Height { get; }

    /// <summary>BGRA 픽셀 데이터</summary>
    public byte[] Data { get; }

    /// <summary>스트라이드 (행당 바이트 수)</summary>
    public int Stride => Width * 4;

    /// <summary>PTS (초)</summary>
    public double Pts { get; set; }

    /// <summary>프레임 번호</summary>
    public long FrameNumber { get; set; }

    public VideoFrame(int width, int height, byte[] data)
    {
        Width = width;
        Height = height;
        Data = data;
    }

    public void Dispose()
    {
        // 현재는 관리되는 메모리만 사용
        GC.SuppressFinalize(this);
    }
}

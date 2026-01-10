using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Wiplayer.FFmpeg.Decoder;

/// <summary>
/// 비디오 디코더 (FFmpeg 래퍼)
/// </summary>
public unsafe class VideoDecoder : IDisposable
{
    private AVCodecContext* _codecContext = null;
    private AVFrame* _frame = null;
    private AVFrame* _hwFrame = null;
    private SwsContext* _swsContext = null;
    private AVBufferRef* _hwDeviceContext = null;
    private bool _disposed = false;

    /// <summary>비디오 너비</summary>
    public int Width => _codecContext != null ? _codecContext->width : 0;

    /// <summary>비디오 높이</summary>
    public int Height => _codecContext != null ? _codecContext->height : 0;

    /// <summary>픽셀 포맷</summary>
    public AVPixelFormat PixelFormat => _codecContext != null ? _codecContext->pix_fmt : AVPixelFormat.AV_PIX_FMT_NONE;

    /// <summary>하드웨어 가속 사용 여부</summary>
    public bool IsHardwareAccelerated { get; private set; }

    /// <summary>사용 중인 하드웨어 가속 타입</summary>
    public AVHWDeviceType HwDeviceType { get; private set; } = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

    /// <summary>
    /// 디코더 초기화
    /// </summary>
    public bool Initialize(AVStream* stream, bool useHwAccel = true)
    {
        var codecPar = stream->codecpar;
        var codec = ffmpeg.avcodec_find_decoder(codecPar->codec_id);

        if (codec == null)
        {
            throw new FFmpegException($"코덱을 찾을 수 없습니다: {codecPar->codec_id}");
        }

        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecContext == null)
        {
            throw new FFmpegException("코덱 컨텍스트 할당 실패");
        }

        var result = ffmpeg.avcodec_parameters_to_context(_codecContext, codecPar);
        if (result < 0)
        {
            throw new FFmpegException("코덱 파라미터 복사 실패", result);
        }

        // 멀티스레드 디코딩 설정 (대용량 미디어 최적화)
        _codecContext->thread_count = Environment.ProcessorCount;
        _codecContext->thread_type = ffmpeg.FF_THREAD_FRAME | ffmpeg.FF_THREAD_SLICE;

        // 하드웨어 가속 설정 시도
        if (useHwAccel)
        {
            TryInitializeHwAccel(codec);
        }

        // 코덱 열기
        result = ffmpeg.avcodec_open2(_codecContext, codec, null);
        if (result < 0)
        {
            throw new FFmpegException("코덱 열기 실패", result);
        }

        // 프레임 할당
        _frame = ffmpeg.av_frame_alloc();
        if (IsHardwareAccelerated)
        {
            _hwFrame = ffmpeg.av_frame_alloc();
        }

        return true;
    }

    /// <summary>
    /// 하드웨어 가속 초기화 시도
    /// </summary>
    private void TryInitializeHwAccel(AVCodec* codec)
    {
        // 지원되는 하드웨어 가속 방식 확인
        AVHWDeviceType[] hwTypes =
        {
            AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
            AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
            AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
            AVHWDeviceType.AV_HWDEVICE_TYPE_QSV
        };

        foreach (var hwType in hwTypes)
        {
            // 코덱이 이 하드웨어 가속을 지원하는지 확인
            AVPixelFormat hwPixFmt = AVPixelFormat.AV_PIX_FMT_NONE;

            for (int i = 0; ; i++)
            {
                var config = ffmpeg.avcodec_get_hw_config(codec, i);
                if (config == null)
                    break;

                // AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 1
                if (config->device_type == hwType &&
                    (config->methods & 1) != 0)
                {
                    hwPixFmt = config->pix_fmt;
                    break;
                }
            }

            if (hwPixFmt == AVPixelFormat.AV_PIX_FMT_NONE)
                continue;

            // 하드웨어 디바이스 컨텍스트 생성
            fixed (AVBufferRef** pHwDeviceContext = &_hwDeviceContext)
            {
                var result = ffmpeg.av_hwdevice_ctx_create(pHwDeviceContext, hwType, null, null, 0);
                if (result >= 0)
                {
                    _codecContext->hw_device_ctx = ffmpeg.av_buffer_ref(_hwDeviceContext);
                    IsHardwareAccelerated = true;
                    HwDeviceType = hwType;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 패킷 디코딩
    /// </summary>
    public int SendPacket(AVPacket* packet)
    {
        if (_codecContext == null)
            return -1;

        return ffmpeg.avcodec_send_packet(_codecContext, packet);
    }

    /// <summary>
    /// 디코딩된 프레임 받기
    /// </summary>
    public AVFrame* ReceiveFrame()
    {
        if (_codecContext == null || _frame == null)
            return null;

        var targetFrame = IsHardwareAccelerated ? _hwFrame : _frame;
        var result = ffmpeg.avcodec_receive_frame(_codecContext, targetFrame);

        if (result < 0)
            return null;

        // 하드웨어 프레임을 소프트웨어 프레임으로 전송
        if (IsHardwareAccelerated && _hwFrame != null)
        {
            if (_hwFrame->format == (int)GetHwPixelFormat())
            {
                result = ffmpeg.av_hwframe_transfer_data(_frame, _hwFrame, 0);
                if (result < 0)
                {
                    // 전송 실패 시 하드웨어 프레임 그대로 반환
                    return _hwFrame;
                }
                _frame->pts = _hwFrame->pts;
                ffmpeg.av_frame_unref(_hwFrame);
                return _frame;
            }
        }

        return targetFrame;
    }

    private AVPixelFormat GetHwPixelFormat()
    {
        return HwDeviceType switch
        {
            AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA => AVPixelFormat.AV_PIX_FMT_D3D11,
            AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2 => AVPixelFormat.AV_PIX_FMT_DXVA2_VLD,
            AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA => AVPixelFormat.AV_PIX_FMT_CUDA,
            AVHWDeviceType.AV_HWDEVICE_TYPE_QSV => AVPixelFormat.AV_PIX_FMT_QSV,
            _ => AVPixelFormat.AV_PIX_FMT_NONE
        };
    }

    /// <summary>
    /// 프레임을 BGRA로 변환
    /// </summary>
    public byte[]? ConvertFrameToBgra(AVFrame* frame)
    {
        if (frame == null || frame->data[0] == null)
            return null;

        var srcFormat = (AVPixelFormat)frame->format;
        var dstFormat = AVPixelFormat.AV_PIX_FMT_BGRA;
        var width = frame->width;
        var height = frame->height;

        // SwsContext 생성 또는 재사용 (최고 품질: LANCZOS 스케일링)
        _swsContext = ffmpeg.sws_getCachedContext(
            _swsContext,
            width, height, srcFormat,
            width, height, dstFormat,
            ffmpeg.SWS_LANCZOS | ffmpeg.SWS_ACCURATE_RND | ffmpeg.SWS_FULL_CHR_H_INT,
            null, null, null);

        if (_swsContext == null)
            return null;

        // 출력 버퍼 할당
        var dstStride = width * 4;
        var dstData = new byte[dstStride * height];

        fixed (byte* pDstData = dstData)
        {
            var dstDataPtr = new byte*[4] { pDstData, null, null, null };
            var dstLinesize = new int[4] { dstStride, 0, 0, 0 };

            ffmpeg.sws_scale(
                _swsContext,
                frame->data, frame->linesize, 0, height,
                dstDataPtr, dstLinesize);
        }

        return dstData;
    }

    /// <summary>
    /// 디코더 버퍼 플러시
    /// </summary>
    public void Flush()
    {
        if (_codecContext != null)
        {
            ffmpeg.avcodec_flush_buffers(_codecContext);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_swsContext != null)
            {
                ffmpeg.sws_freeContext(_swsContext);
                _swsContext = null;
            }

            if (_frame != null)
            {
                var frame = _frame;
                ffmpeg.av_frame_free(&frame);
                _frame = null;
            }

            if (_hwFrame != null)
            {
                var hwFrame = _hwFrame;
                ffmpeg.av_frame_free(&hwFrame);
                _hwFrame = null;
            }

            if (_hwDeviceContext != null)
            {
                var ctx = _hwDeviceContext;
                ffmpeg.av_buffer_unref(&ctx);
                _hwDeviceContext = null;
            }

            if (_codecContext != null)
            {
                var ctx = _codecContext;
                ffmpeg.avcodec_free_context(&ctx);
                _codecContext = null;
            }

            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~VideoDecoder()
    {
        Dispose();
    }
}

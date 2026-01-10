using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Wiplayer.FFmpeg.Decoder;

/// <summary>
/// 오디오 디코더 (FFmpeg 래퍼)
/// </summary>
public unsafe class AudioDecoder : IDisposable
{
    private AVCodecContext* _codecContext = null;
    private AVFrame* _frame = null;
    private SwrContext* _swrContext = null;
    private bool _disposed = false;

    // 출력 포맷 (WASAPI 호환 - 안정적인 고품질)
    private const AVSampleFormat OutputSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLT;
    private const int OutputSampleRate = 48000;  // 대부분 장치 호환 고품질
    private const int OutputChannels = 2;

    /// <summary>원본 샘플 레이트</summary>
    public int SampleRate => _codecContext != null ? _codecContext->sample_rate : 0;

    /// <summary>원본 채널 수</summary>
    public int Channels => _codecContext != null ? _codecContext->ch_layout.nb_channels : 0;

    /// <summary>출력 샘플 레이트</summary>
    public int OutputSampleRateValue => OutputSampleRate;

    /// <summary>출력 채널 수</summary>
    public int OutputChannelCount => OutputChannels;

    /// <summary>
    /// 디코더 초기화
    /// </summary>
    public bool Initialize(AVStream* stream)
    {
        var codecPar = stream->codecpar;
        var codec = ffmpeg.avcodec_find_decoder(codecPar->codec_id);

        if (codec == null)
        {
            throw new FFmpegException($"오디오 코덱을 찾을 수 없습니다: {codecPar->codec_id}");
        }

        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecContext == null)
        {
            throw new FFmpegException("오디오 코덱 컨텍스트 할당 실패");
        }

        var result = ffmpeg.avcodec_parameters_to_context(_codecContext, codecPar);
        if (result < 0)
        {
            throw new FFmpegException("오디오 코덱 파라미터 복사 실패", result);
        }

        result = ffmpeg.avcodec_open2(_codecContext, codec, null);
        if (result < 0)
        {
            throw new FFmpegException("오디오 코덱 열기 실패", result);
        }

        // 프레임 할당
        _frame = ffmpeg.av_frame_alloc();

        // 리샘플러 초기화
        InitializeResampler();

        return true;
    }

    /// <summary>
    /// 리샘플러 초기화
    /// </summary>
    private void InitializeResampler()
    {
        _swrContext = ffmpeg.swr_alloc();
        if (_swrContext == null)
        {
            throw new FFmpegException("리샘플러 할당 실패");
        }

        // 입력 채널 레이아웃
        var srcChLayout = _codecContext->ch_layout;

        // 출력 채널 레이아웃 (스테레오)
        AVChannelLayout dstChLayout;
        ffmpeg.av_channel_layout_default(&dstChLayout, OutputChannels);

        // 옵션 설정
        ffmpeg.av_opt_set_chlayout(_swrContext, "in_chlayout", &srcChLayout, 0);
        ffmpeg.av_opt_set_chlayout(_swrContext, "out_chlayout", &dstChLayout, 0);
        ffmpeg.av_opt_set_int(_swrContext, "in_sample_rate", _codecContext->sample_rate, 0);
        ffmpeg.av_opt_set_int(_swrContext, "out_sample_rate", OutputSampleRate, 0);
        ffmpeg.av_opt_set_sample_fmt(_swrContext, "in_sample_fmt", _codecContext->sample_fmt, 0);
        ffmpeg.av_opt_set_sample_fmt(_swrContext, "out_sample_fmt", OutputSampleFormat, 0);

        // 고품질 리샘플링 설정
        ffmpeg.av_opt_set_int(_swrContext, "filter_size", 32, 0);  // 필터 크기 증가 (기본 16)
        ffmpeg.av_opt_set_int(_swrContext, "phase_shift", 10, 0);  // 위상 시프트 (더 부드러운 보간)
        ffmpeg.av_opt_set_int(_swrContext, "linear_interp", 0, 0); // 선형 보간 비활성화 (더 높은 품질)
        ffmpeg.av_opt_set_double(_swrContext, "cutoff", 0.97, 0);  // 컷오프 주파수 (품질/지연 균형)

        var result = ffmpeg.swr_init(_swrContext);
        if (result < 0)
        {
            throw new FFmpegException("리샘플러 초기화 실패", result);
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
    /// 디코딩된 오디오 데이터 받기 (float[] 형태, interleaved stereo)
    /// </summary>
    public float[]? ReceiveAudio()
    {
        if (_codecContext == null || _frame == null)
            return null;

        var result = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
        if (result < 0)
            return null;

        // 리샘플링
        return ResampleFrame(_frame);
    }

    /// <summary>
    /// 프레임 리샘플링
    /// </summary>
    private float[]? ResampleFrame(AVFrame* frame)
    {
        if (_swrContext == null)
            return null;

        // 출력 샘플 수 계산
        var dstNbSamples = (int)ffmpeg.av_rescale_rnd(
            ffmpeg.swr_get_delay(_swrContext, frame->sample_rate) + frame->nb_samples,
            OutputSampleRate,
            frame->sample_rate,
            AVRounding.AV_ROUND_UP);

        // 출력 버퍼 할당
        var outputData = new float[dstNbSamples * OutputChannels];

        fixed (float* pOutputData = outputData)
        {
            var outputPtr = (byte*)pOutputData;
            var result = ffmpeg.swr_convert(
                _swrContext,
                &outputPtr, dstNbSamples,
                frame->extended_data, frame->nb_samples);

            if (result < 0)
                return null;

            // 실제 변환된 샘플 수에 맞게 배열 조정
            if (result < dstNbSamples)
            {
                Array.Resize(ref outputData, result * OutputChannels);
            }
        }

        return outputData;
    }

    /// <summary>
    /// PTS를 초 단위로 변환
    /// </summary>
    public double GetFramePts(AVFrame* frame, AVRational timeBase)
    {
        if (frame == null || frame->pts == ffmpeg.AV_NOPTS_VALUE)
            return 0;

        return frame->pts * ffmpeg.av_q2d(timeBase);
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
            if (_swrContext != null)
            {
                fixed (SwrContext** pSwrContext = &_swrContext)
                {
                    ffmpeg.swr_free(pSwrContext);
                }
                _swrContext = null;
            }

            if (_frame != null)
            {
                var frame = _frame;
                ffmpeg.av_frame_free(&frame);
                _frame = null;
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

    ~AudioDecoder()
    {
        Dispose();
    }
}

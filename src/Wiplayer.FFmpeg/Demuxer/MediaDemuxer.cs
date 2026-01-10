using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Wiplayer.Core.Player;

namespace Wiplayer.FFmpeg.Demuxer;

/// <summary>
/// 미디어 파일 디멀티플렉서 (FFmpeg 래퍼)
/// </summary>
public unsafe class MediaDemuxer : IDisposable
{
    private AVFormatContext* _formatContext = null;
    private bool _disposed = false;

    /// <summary>미디어 정보</summary>
    public MediaInfo? MediaInfo { get; private set; }

    /// <summary>비디오 스트림 인덱스</summary>
    public int VideoStreamIndex { get; private set; } = -1;

    /// <summary>오디오 스트림 인덱스</summary>
    public int AudioStreamIndex { get; private set; } = -1;

    /// <summary>현재 선택된 비디오 스트림</summary>
    public AVStream* VideoStream => VideoStreamIndex >= 0 ? _formatContext->streams[VideoStreamIndex] : null;

    /// <summary>현재 선택된 오디오 스트림</summary>
    public AVStream* AudioStream => AudioStreamIndex >= 0 ? _formatContext->streams[AudioStreamIndex] : null;

    /// <summary>포맷 컨텍스트</summary>
    public AVFormatContext* FormatContext => _formatContext;

    /// <summary>열려 있는지 확인</summary>
    public bool IsOpen => _formatContext != null;

    /// <summary>
    /// 미디어 파일 열기 (대용량 미디어 최적화)
    /// </summary>
    public bool Open(string path)
    {
        if (_formatContext != null)
            Close();

        // 대용량 미디어 최적화를 위한 옵션 설정
        AVDictionary* options = null;

        // I/O 버퍼 크기 증가 (32MB - 대용량 4K/8K 미디어 최적화)
        ffmpeg.av_dict_set(&options, "buffer_size", "33554432", 0);

        // 프로빙 크기 증가 (10MB - 더 정확한 스트림 분석)
        ffmpeg.av_dict_set(&options, "probesize", "10485760", 0);

        // 분석 지속 시간 증가 (10초)
        ffmpeg.av_dict_set(&options, "analyzeduration", "10000000", 0);

        // 멀티스레드 디코딩 최적화
        ffmpeg.av_dict_set(&options, "threads", "auto", 0);

        fixed (AVFormatContext** pFormatContext = &_formatContext)
        {
            var result = ffmpeg.avformat_open_input(pFormatContext, path, null, &options);
            ffmpeg.av_dict_free(&options);

            if (result < 0)
            {
                throw new FFmpegException($"파일을 열 수 없습니다: {path}", result);
            }
        }

        // 스트림 정보 읽기
        var findResult = ffmpeg.avformat_find_stream_info(_formatContext, null);
        if (findResult < 0)
        {
            Close();
            throw new FFmpegException("스트림 정보를 찾을 수 없습니다.", findResult);
        }

        // 스트림 인덱스 찾기
        FindBestStreams();

        // 미디어 정보 구성
        MediaInfo = BuildMediaInfo(path);

        return true;
    }

    /// <summary>
    /// 최적의 스트림 찾기
    /// </summary>
    private void FindBestStreams()
    {
        VideoStreamIndex = ffmpeg.av_find_best_stream(_formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
        AudioStreamIndex = ffmpeg.av_find_best_stream(_formatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
    }

    /// <summary>
    /// 미디어 정보 구성
    /// </summary>
    private MediaInfo BuildMediaInfo(string path)
    {
        var info = new MediaInfo
        {
            Path = path,
            Duration = _formatContext->duration > 0 ? _formatContext->duration / (double)ffmpeg.AV_TIME_BASE : 0,
            Bitrate = _formatContext->bit_rate,
            Format = Marshal.PtrToStringAnsi((IntPtr)_formatContext->iformat->name) ?? "unknown"
        };

        // 파일 크기
        try
        {
            if (File.Exists(path))
                info.FileSize = new FileInfo(path).Length;
        }
        catch { }

        // 스트림 정보 수집
        for (int i = 0; i < _formatContext->nb_streams; i++)
        {
            var stream = _formatContext->streams[i];
            var codecPar = stream->codecpar;

            switch (codecPar->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    info.VideoStreams.Add(BuildVideoStreamInfo(stream, i));
                    break;

                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    info.AudioStreams.Add(BuildAudioStreamInfo(stream, i));
                    break;

                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    info.SubtitleStreams.Add(BuildSubtitleStreamInfo(stream, i));
                    break;
            }
        }

        // 메타데이터 수집
        CollectMetadata(info, _formatContext->metadata);

        // 챕터 정보 수집
        for (int i = 0; i < _formatContext->nb_chapters; i++)
        {
            var chapter = _formatContext->chapters[i];
            var chapterInfo = new ChapterInfo
            {
                Index = i,
                StartTime = chapter->start * ffmpeg.av_q2d(chapter->time_base),
                EndTime = chapter->end * ffmpeg.av_q2d(chapter->time_base)
            };

            // 챕터 제목
            var titleEntry = ffmpeg.av_dict_get(chapter->metadata, "title", null, 0);
            if (titleEntry != null)
            {
                chapterInfo.Title = Marshal.PtrToStringUTF8((IntPtr)titleEntry->value) ?? $"Chapter {i + 1}";
            }
            else
            {
                chapterInfo.Title = $"Chapter {i + 1}";
            }

            info.Chapters.Add(chapterInfo);
        }

        return info;
    }

    private VideoStreamInfo BuildVideoStreamInfo(AVStream* stream, int index)
    {
        var codecPar = stream->codecpar;
        var codec = ffmpeg.avcodec_find_decoder(codecPar->codec_id);

        var info = new VideoStreamInfo
        {
            Index = index,
            Width = codecPar->width,
            Height = codecPar->height,
            Bitrate = codecPar->bit_rate,
            Codec = codec != null ? Marshal.PtrToStringAnsi((IntPtr)codec->name) ?? "unknown" : "unknown",
            CodecLongName = codec != null ? Marshal.PtrToStringAnsi((IntPtr)codec->long_name) ?? "" : "",
            PixelFormat = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)codecPar->format) ?? "unknown"
        };

        // 프레임 레이트
        if (stream->avg_frame_rate.den > 0)
        {
            info.FrameRate = ffmpeg.av_q2d(stream->avg_frame_rate);
        }
        else if (stream->r_frame_rate.den > 0)
        {
            info.FrameRate = ffmpeg.av_q2d(stream->r_frame_rate);
        }

        // 메타데이터
        var langEntry = ffmpeg.av_dict_get(stream->metadata, "language", null, 0);
        if (langEntry != null)
            info.Language = Marshal.PtrToStringAnsi((IntPtr)langEntry->value) ?? "";

        var titleEntry = ffmpeg.av_dict_get(stream->metadata, "title", null, 0);
        if (titleEntry != null)
            info.Title = Marshal.PtrToStringUTF8((IntPtr)titleEntry->value) ?? "";

        info.IsDefault = (stream->disposition & ffmpeg.AV_DISPOSITION_DEFAULT) != 0;

        return info;
    }

    private AudioStreamInfo BuildAudioStreamInfo(AVStream* stream, int index)
    {
        var codecPar = stream->codecpar;
        var codec = ffmpeg.avcodec_find_decoder(codecPar->codec_id);

        var info = new AudioStreamInfo
        {
            Index = index,
            SampleRate = codecPar->sample_rate,
            Channels = codecPar->ch_layout.nb_channels,
            Bitrate = codecPar->bit_rate,
            Codec = codec != null ? Marshal.PtrToStringAnsi((IntPtr)codec->name) ?? "unknown" : "unknown",
            CodecLongName = codec != null ? Marshal.PtrToStringAnsi((IntPtr)codec->long_name) ?? "" : ""
        };

        // 채널 레이아웃
        var channelLayoutBuf = new byte[256];
        fixed (byte* buf = channelLayoutBuf)
        {
            ffmpeg.av_channel_layout_describe(&codecPar->ch_layout, buf, 256);
            info.ChannelLayout = Marshal.PtrToStringAnsi((IntPtr)buf) ?? "";
        }

        // 메타데이터
        var langEntry = ffmpeg.av_dict_get(stream->metadata, "language", null, 0);
        if (langEntry != null)
            info.Language = Marshal.PtrToStringAnsi((IntPtr)langEntry->value) ?? "";

        var titleEntry = ffmpeg.av_dict_get(stream->metadata, "title", null, 0);
        if (titleEntry != null)
            info.Title = Marshal.PtrToStringUTF8((IntPtr)titleEntry->value) ?? "";

        info.IsDefault = (stream->disposition & ffmpeg.AV_DISPOSITION_DEFAULT) != 0;

        return info;
    }

    private SubtitleStreamInfo BuildSubtitleStreamInfo(AVStream* stream, int index)
    {
        var codecPar = stream->codecpar;
        var codec = ffmpeg.avcodec_find_decoder(codecPar->codec_id);

        var info = new SubtitleStreamInfo
        {
            Index = index,
            Codec = codec != null ? Marshal.PtrToStringAnsi((IntPtr)codec->name) ?? "unknown" : "unknown"
        };

        var langEntry = ffmpeg.av_dict_get(stream->metadata, "language", null, 0);
        if (langEntry != null)
            info.Language = Marshal.PtrToStringAnsi((IntPtr)langEntry->value) ?? "";

        var titleEntry = ffmpeg.av_dict_get(stream->metadata, "title", null, 0);
        if (titleEntry != null)
            info.Title = Marshal.PtrToStringUTF8((IntPtr)titleEntry->value) ?? "";

        info.IsDefault = (stream->disposition & ffmpeg.AV_DISPOSITION_DEFAULT) != 0;
        info.IsForced = (stream->disposition & ffmpeg.AV_DISPOSITION_FORCED) != 0;

        return info;
    }

    private void CollectMetadata(MediaInfo info, AVDictionary* metadata)
    {
        if (metadata == null) return;

        AVDictionaryEntry* entry = null;
        while ((entry = ffmpeg.av_dict_get(metadata, "", entry, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
        {
            var key = Marshal.PtrToStringAnsi((IntPtr)entry->key);
            var value = Marshal.PtrToStringUTF8((IntPtr)entry->value);

            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
            {
                info.Metadata[key] = value;
            }
        }
    }

    /// <summary>
    /// 다음 패킷 읽기
    /// </summary>
    public bool ReadPacket(AVPacket* packet)
    {
        if (_formatContext == null)
            return false;

        var result = ffmpeg.av_read_frame(_formatContext, packet);
        return result >= 0;
    }

    /// <summary>
    /// 특정 시간으로 시크
    /// </summary>
    public bool Seek(double timeSeconds, bool backward = true)
    {
        if (_formatContext == null)
            return false;

        // 비디오 스트림 기준으로 시크 (더 정확함)
        int streamIndex = VideoStreamIndex >= 0 ? VideoStreamIndex : -1;
        long timestamp;

        if (streamIndex >= 0)
        {
            // 비디오 스트림의 time_base로 변환
            var timeBase = _formatContext->streams[streamIndex]->time_base;
            timestamp = (long)(timeSeconds / ffmpeg.av_q2d(timeBase));
        }
        else
        {
            // 스트림 인덱스가 없으면 AV_TIME_BASE 사용
            timestamp = (long)(timeSeconds * ffmpeg.AV_TIME_BASE);
        }

        // AVSEEK_FLAG_BACKWARD: 요청 위치 이전의 키프레임으로 이동
        var flags = backward ? ffmpeg.AVSEEK_FLAG_BACKWARD : 0;

        var result = ffmpeg.av_seek_frame(_formatContext, streamIndex, timestamp, flags);

        // 시크 실패 시 다른 방법 시도
        if (result < 0)
        {
            // avformat_seek_file로 재시도 (더 정확한 범위 시크)
            var minTs = backward ? long.MinValue : timestamp;
            var maxTs = backward ? timestamp : long.MaxValue;
            result = ffmpeg.avformat_seek_file(_formatContext, streamIndex, minTs, timestamp, maxTs, 0);
        }

        return result >= 0;
    }

    /// <summary>
    /// 파일 닫기
    /// </summary>
    public void Close()
    {
        if (_formatContext != null)
        {
            fixed (AVFormatContext** pFormatContext = &_formatContext)
            {
                ffmpeg.avformat_close_input(pFormatContext);
            }
            _formatContext = null;
        }

        VideoStreamIndex = -1;
        AudioStreamIndex = -1;
        MediaInfo = null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Close();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~MediaDemuxer()
    {
        Dispose();
    }
}

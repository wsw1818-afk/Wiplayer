using System.Buffers;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Wiplayer.Renderer.Audio;

/// <summary>
/// 오디오 렌더러 (WASAPI 기반)
/// </summary>
public class AudioRenderer : IDisposable
{
    private WasapiOut? _wasapiOut;
    private BufferedWaveProvider? _waveProvider;
    private VolumeSampleProvider? _volumeProvider;
    private bool _disposed = false;
    private byte[]? _sampleBuffer; // 샘플 변환용 재사용 버퍼

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly bool _useExclusiveMode;

    /// <summary>볼륨 (0.0 ~ 1.0) - 앱 자체 볼륨 (시스템 볼륨과 독립)</summary>
    public float Volume
    {
        get => _volumeProvider?.Volume ?? 1.0f;
        set
        {
            if (_volumeProvider != null)
                _volumeProvider.Volume = Math.Clamp(value, 0f, 1f);
        }
    }

    /// <summary>음소거 여부</summary>
    public bool IsMuted { get; set; } = false;

    /// <summary>재생 중인지</summary>
    public bool IsPlaying => _wasapiOut?.PlaybackState == PlaybackState.Playing;

    /// <summary>버퍼링된 시간 (초)</summary>
    public double BufferedDuration => _waveProvider != null
        ? _waveProvider.BufferedDuration.TotalSeconds
        : 0;

    /// <summary>배타 모드 사용 중인지</summary>
    public bool IsExclusiveMode => _useExclusiveMode;

    private int _actualSampleRate;

    public AudioRenderer(int sampleRate = 48000, int channels = 2, bool useExclusiveMode = false)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _useExclusiveMode = useExclusiveMode;
    }

    /// <summary>
    /// 오디오 렌더러 초기화 (자동 폴백 포함)
    /// </summary>
    public void Initialize()
    {
        // 공유 모드 우선 (호환성 최우선)
        _wasapiOut = new WasapiOut(
            NAudio.CoreAudioApi.AudioClientShareMode.Shared,
            latency: 100);

        // 샘플레이트 폴백: 48kHz -> 44.1kHz
        _actualSampleRate = _sampleRate;
        WaveFormat? waveFormat = null;

        int[] sampleRates = { _sampleRate, 48000, 44100 };
        foreach (var rate in sampleRates)
        {
            try
            {
                waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, _channels);
                _waveProvider = new BufferedWaveProvider(waveFormat)
                {
                    BufferLength = rate * _channels * sizeof(float) * 5, // 5초 버퍼
                    DiscardOnBufferOverflow = true
                };

                // VolumeSampleProvider로 감싸서 볼륨 조절 즉시 적용
                _volumeProvider = new VolumeSampleProvider(_waveProvider.ToSampleProvider());
                _wasapiOut.Init(_volumeProvider.ToWaveProvider());
                _actualSampleRate = rate;
                break;
            }
            catch
            {
                // 다음 샘플레이트 시도
                _wasapiOut?.Dispose();
                _wasapiOut = new WasapiOut(
                    NAudio.CoreAudioApi.AudioClientShareMode.Shared,
                    latency: 100);
            }
        }

        if (_waveProvider == null)
        {
            throw new InvalidOperationException("오디오 장치 초기화 실패");
        }
    }

    /// <summary>
    /// 오디오 데이터 추가 (float[] interleaved)
    /// </summary>
    public void AddSamples(float[] samples)
    {
        if (_waveProvider == null || IsMuted)
            return;

        // 버퍼 재사용 (GC 압력 감소)
        var requiredSize = samples.Length * sizeof(float);
        if (_sampleBuffer == null || _sampleBuffer.Length < requiredSize)
        {
            _sampleBuffer = new byte[requiredSize];
        }
        Buffer.BlockCopy(samples, 0, _sampleBuffer, 0, requiredSize);
        _waveProvider.AddSamples(_sampleBuffer, 0, requiredSize);
    }

    /// <summary>
    /// 오디오 데이터 추가 (byte[])
    /// </summary>
    public void AddSamples(byte[] buffer, int offset, int count)
    {
        if (_waveProvider == null || IsMuted)
            return;

        _waveProvider.AddSamples(buffer, offset, count);
    }

    /// <summary>
    /// 재생 시작
    /// </summary>
    public void Play()
    {
        if (_wasapiOut?.PlaybackState != PlaybackState.Playing)
        {
            _wasapiOut?.Play();
        }
    }

    /// <summary>
    /// 재생 일시정지
    /// </summary>
    public void Pause()
    {
        _wasapiOut?.Pause();
    }

    /// <summary>
    /// 재생 정지
    /// </summary>
    public void Stop()
    {
        _wasapiOut?.Stop();
        _waveProvider?.ClearBuffer();
    }

    /// <summary>
    /// 버퍼 비우기
    /// </summary>
    public void ClearBuffer()
    {
        _waveProvider?.ClearBuffer();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _wasapiOut?.Stop();
            _wasapiOut?.Dispose();
            _wasapiOut = null;
            _waveProvider = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~AudioRenderer()
    {
        Dispose();
    }
}

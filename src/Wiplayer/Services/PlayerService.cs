using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using FFmpeg.AutoGen;
using Serilog;
using Wiplayer.Core.Player;
using Wiplayer.Core.Playlist;
using Wiplayer.Core.Settings;
using Wiplayer.Core.Utils;
using Wiplayer.FFmpeg.Decoder;
using Wiplayer.FFmpeg.Demuxer;
using Wiplayer.Renderer.Audio;
using Wiplayer.Renderer.Video;
using Wiplayer.Subtitle;

namespace Wiplayer.Services;

/// <summary>
/// 미디어 플레이어 서비스 구현
/// </summary>
public class PlayerService : IPlayerService, IDisposable
{
    private readonly PlayerSettings _settings;
    private readonly PlayerStateMachine _stateMachine = new();
    private readonly PlaybackClock _clock = new();
    private readonly RecentFiles _recentFiles = new();
    private readonly Core.Playlist.Bookmarks _bookmarks = new();

    private MediaDemuxer? _demuxer;
    private VideoDecoder? _videoDecoder;
    private AudioDecoder? _audioDecoder;
    private VideoRenderer? _videoRenderer;
    private AudioRenderer? _audioRenderer;

    private CancellationTokenSource? _playbackCts;
    private Task? _playbackTask;

    private double _volume = 1.0;
    private bool _isMuted = false;
    private double _playbackSpeed = 1.0;
    private double _seekTargetPosition = -1; // 시크 후 동기화를 위한 목표 위치
    private bool _isSeekPending = false; // 시크 후 첫 프레임 동기화 대기 플래그
    private double _lastSeekPosition = -1; // 반복 Seek 방지용 마지막 시크 위치
    private DateTime _lastSeekTime = DateTime.MinValue; // 연속 Seek 쓰로틀링용 마지막 시크 시간
    private string? _lastMediaPath; // Stop 후 재생을 위한 마지막 미디어 경로 저장

    // 영상 필터 속성
    private int _rotationAngle = 0;
    private int _brightness = 0;
    private int _contrast = 0;
    private int _saturation = 0;

    public PlayerState State => _stateMachine.CurrentState;

    public int RotationAngle
    {
        get => _rotationAngle;
        set => _rotationAngle = value % 360;
    }

    public int Brightness
    {
        get => _brightness;
        set
        {
            _brightness = Math.Clamp(value, -100, 100);
            if (_videoRenderer != null)
                _videoRenderer.Brightness = _brightness;
            Log.Debug("Brightness 설정: {Value}", _brightness);
        }
    }

    public int Contrast
    {
        get => _contrast;
        set
        {
            _contrast = Math.Clamp(value, -100, 100);
            if (_videoRenderer != null)
                _videoRenderer.Contrast = _contrast;
            Log.Debug("Contrast 설정: {Value}", _contrast);
        }
    }

    public int Saturation
    {
        get => _saturation;
        set
        {
            _saturation = Math.Clamp(value, -100, 100);
            if (_videoRenderer != null)
                _videoRenderer.Saturation = _saturation;
            Log.Debug("Saturation 설정: {Value}", _saturation);
        }
    }
    public double Position => _clock.CurrentTime;
    public double Duration => CurrentMedia?.Duration ?? 0;
    public MediaInfo? CurrentMedia => _demuxer?.MediaInfo;
    public WriteableBitmap? VideoFrame => _videoRenderer?.Bitmap;
    public SubtitleManager SubtitleManager { get; } = new();
    public ABRepeat ABRepeat { get; } = new();
    public IReadOnlyList<Core.Playlist.PlaylistItem> RecentFiles => _recentFiles.Items;
    public Core.Playlist.Bookmarks Bookmarks => _bookmarks;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, _settings.Audio.AllowVolumeBoost ? _settings.Audio.MaxVolume / 100.0 : 1.0);
            if (_audioRenderer != null)
                _audioRenderer.Volume = (float)(_isMuted ? 0 : _volume);
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            if (_audioRenderer != null)
                _audioRenderer.Volume = (float)(value ? 0 : _volume);
        }
    }

    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            _playbackSpeed = Math.Clamp(value, 0.2, 4.0);
            _clock.PlaybackSpeed = _playbackSpeed;
        }
    }

    public event EventHandler<PlayerStateChangedEventArgs>? StateChanged;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
    public event EventHandler<PlayerErrorEventArgs>? ErrorOccurred;
    public event EventHandler? FrameRendered;

    public PlayerService(PlayerSettings settings)
    {
        _settings = settings;
        _volume = settings.Audio.DefaultVolume / 100.0;
        _playbackSpeed = settings.Playback.DefaultSpeed;

        _stateMachine.StateChanged += (s, e) => StateChanged?.Invoke(this, e);
    }

    public async Task<bool> OpenAsync(string path)
    {
        try
        {
            // 마지막 미디어 경로 저장 (Stop 후 재생을 위해)
            _lastMediaPath = path;

            // 반복 Seek 방지 변수 리셋
            _lastSeekPosition = -1;
            _lastSeekTime = DateTime.MinValue;

            // 기존 재생 정지
            Stop();

            _stateMachine.TryTransition(PlayerState.Loading);

            // Demuxer 초기화
            _demuxer = new MediaDemuxer();
            _demuxer.Open(path);

            // 비디오 디코더 초기화
            if (_demuxer.VideoStreamIndex >= 0)
            {
                unsafe
                {
                    _videoDecoder = new VideoDecoder();
                    _videoDecoder.Initialize(_demuxer.VideoStream, _settings.Playback.UseHardwareAcceleration);
                }

                _videoRenderer = new VideoRenderer();
                _videoRenderer.Initialize(_videoDecoder.Width, _videoDecoder.Height);
                _videoRenderer.FrameRendered += (s, e) => FrameRendered?.Invoke(this, e);
            }

            // 오디오 디코더 초기화 (실패해도 영상은 재생)
            if (_demuxer.AudioStreamIndex >= 0)
            {
                try
                {
                    unsafe
                    {
                        _audioDecoder = new AudioDecoder();
                        _audioDecoder.Initialize(_demuxer.AudioStream);
                    }

                    _audioRenderer = new AudioRenderer(_audioDecoder.OutputSampleRateValue, _audioDecoder.OutputChannelCount);
                    _audioRenderer.Initialize();
                    _audioRenderer.Volume = (float)(_isMuted ? 0 : _volume);
                }
                catch (Exception ex)
                {
                    Log.Warning("오디오 초기화 실패 (영상만 재생): {Error}", ex.Message);
                    _audioDecoder?.Dispose();
                    _audioDecoder = null;
                    _audioRenderer?.Dispose();
                    _audioRenderer = null;
                }
            }

            // 자막 자동 로드
            if (_settings.Subtitle.AutoLoad)
            {
                var subtitleFiles = MediaFileHelper.FindSubtitleFiles(path);
                var preferredSub = subtitleFiles.FirstOrDefault();
                if (preferredSub != null)
                {
                    SubtitleManager.Load(preferredSub);
                }
            }

            // 최근 파일에 추가
            _recentFiles.Add(path, Duration);

            // 이어보기
            if (_settings.Playback.ResumePlayback)
            {
                var lastPosition = _recentFiles.GetLastPosition(path);
                if (lastPosition > 10 && lastPosition < Duration - 30)
                {
                    await SeekAsync(lastPosition);
                }
            }

            _stateMachine.TryTransition(PlayerState.Paused);
            return true;
        }
        catch (Exception ex)
        {
            _stateMachine.TryTransition(PlayerState.Error);
            ErrorOccurred?.Invoke(this, new PlayerErrorEventArgs(
                $"파일 열기 실패: {ex.Message}",
                PlayerErrorType.InvalidFormat,
                ex));
            return false;
        }
    }

    public void Play()
    {
        Log.Debug("Play() called, Current State={State}, CanTransition={CanTransition}",
            State, _stateMachine.CanTransitionTo(PlayerState.Playing));

        // Stopped 상태에서 Play 시 마지막 미디어 다시 열기
        if (State == PlayerState.Stopped && !string.IsNullOrEmpty(_lastMediaPath))
        {
            var pathToOpen = _lastMediaPath;
            Log.Debug("Play() from Stopped state, reopening: {Path}", pathToOpen);

            // UI 스레드에서 OpenAsync 호출 (상태 변경이 UI 업데이트를 트리거하기 때문)
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    var success = await OpenAsync(pathToOpen);
                    Log.Debug("OpenAsync result: {Success}", success);

                    if (success && State == PlayerState.Paused)
                    {
                        Log.Debug("Starting playback after reopen");
                        _stateMachine.TryTransition(PlayerState.Playing);
                        _clock.Start();
                        _audioRenderer?.Play();
                        StartPlaybackLoop();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error reopening file: {Path}", pathToOpen);
                }
            });
            return;
        }

        // Ended 상태에서 Play 시 처음부터 다시 재생
        if (State == PlayerState.Ended)
        {
            Log.Debug("Play() from Ended state, restarting playback");

            // 상태를 먼저 Playing으로 전환 (Seek 전에!)
            if (!_stateMachine.TryTransition(PlayerState.Playing))
            {
                Log.Warning("Play() from Ended: Failed to transition to Playing");
                return;
            }

            Log.Debug("Play() from Ended: State transitioned to Playing");

            // 디먹서와 디코더 초기화 (처음으로 시크)
            _demuxer?.Seek(0, backward: true);
            _videoDecoder?.Flush();
            _audioDecoder?.Flush();
            _audioRenderer?.ClearBuffer();
            _clock.Seek(0);

            // Seek 관련 플래그 설정
            _seekTargetPosition = 0;
            _isSeekPending = true;
            _lastSeekPosition = 0;
            _lastSeekTime = DateTime.UtcNow;

            // 재생 시작
            _clock.Start();
            _audioRenderer?.Play();
            StartPlaybackLoop();

            Log.Debug("Play() from Ended: Playback restarted successfully");
            return;
        }

        if (!_stateMachine.CanTransitionTo(PlayerState.Playing))
            return;

        _stateMachine.TryTransition(PlayerState.Playing);
        _clock.Start();
        _audioRenderer?.Play();

        Log.Debug("State after transition: {State}", State);

        // 재생 루프 시작
        StartPlaybackLoop();
    }

    public void Pause()
    {
        if (!_stateMachine.CanTransitionTo(PlayerState.Paused))
            return;

        _stateMachine.TryTransition(PlayerState.Paused);
        _clock.Pause();
        _audioRenderer?.Pause();

        // 현재 위치 저장
        if (CurrentMedia != null)
        {
            _recentFiles.UpdatePosition(CurrentMedia.Path, Position);
        }
    }

    /// <summary>
    /// Ended 상태에서 처음으로 이동 후 일시정지 (멀티뷰 Stop용)
    /// </summary>
    public void ResetToStart()
    {
        Log.Debug("ResetToStart() called, State={State}", State);

        if (State != PlayerState.Ended && State != PlayerState.Playing && State != PlayerState.Paused)
        {
            Log.Debug("ResetToStart: Invalid state, ignoring");
            return;
        }

        // 재생 루프 중지
        StopPlaybackLoop();

        // 디먹서와 디코더 초기화 (처음으로 시크)
        _demuxer?.Seek(0, backward: true);
        _videoDecoder?.Flush();
        _audioDecoder?.Flush();
        _audioRenderer?.ClearBuffer();

        // 클럭을 0으로 리셋하고 정지
        _clock.Seek(0);
        _clock.Pause();

        // Seek 관련 플래그 리셋
        _seekTargetPosition = 0;
        _isSeekPending = false;
        _lastSeekPosition = 0;
        _lastSeekTime = DateTime.UtcNow;

        // 상태를 Paused로 전환
        _stateMachine.ForceState(PlayerState.Paused);

        // 위치 업데이트 이벤트 발생
        PositionChanged?.Invoke(this, new PositionChangedEventArgs(0, Duration));

        // 첫 프레임 렌더링
        _ = Task.Run(() => RenderFirstFrameAfterSeekAsync());

        Log.Debug("ResetToStart: Completed, State={State}", State);
    }

    public void TogglePlayPause()
    {
        if (State == PlayerState.Playing)
            Pause();
        else if (State == PlayerState.Paused || State == PlayerState.Ended)
            Play();
        else if (State == PlayerState.Stopped && !string.IsNullOrEmpty(_lastMediaPath))
            Play(); // Stopped 상태에서 마지막 파일 다시 열기
    }

    public void Stop()
    {
        // 재생 루프 중지
        StopPlaybackLoop();

        // 위치 저장
        if (CurrentMedia != null)
        {
            _recentFiles.UpdatePosition(CurrentMedia.Path, Position);
        }

        _stateMachine.TryTransition(PlayerState.Stopped);
        _clock.Stop();

        _audioRenderer?.Stop();
        _audioRenderer?.Dispose();
        _audioRenderer = null;

        _videoRenderer?.Dispose();
        _videoRenderer = null;

        _videoDecoder?.Dispose();
        _videoDecoder = null;

        _audioDecoder?.Dispose();
        _audioDecoder = null;

        _demuxer?.Dispose();
        _demuxer = null;

        SubtitleManager.Unload();
        ABRepeat.Clear();
    }

    public async Task SeekAsync(double position)
    {
        // 디버깅: 모든 Seek 요청 기록 (쓰로틀링 전)
        var now = DateTime.UtcNow;
        var timeSinceLastSeek = (now - _lastSeekTime).TotalMilliseconds;
        Log.Debug("[Seek] 요청 수신: position={Position:F2}, timeSinceLastSeek={Time:F0}ms, lastPos={LastPos:F2}",
            position, timeSinceLastSeek, _lastSeekPosition);

        if (_demuxer == null)
        {
            Log.Debug("[Seek] _demuxer is null, returning");
            System.Diagnostics.Debug.WriteLine("[Seek] _demuxer is null, returning");
            return;
        }

        // Seek 작업 중이면 새 요청 무시 (RenderFirstFrameAfterSeekAsync 동시 실행 방지)
        if (_isSeekPending)
        {
            Log.Debug("[Seek] 이전 Seek 작업 중, 새 요청 무시");
            return;
        }

        position = Math.Clamp(position, 0, Duration);

        // 연속 Seek 쓰로틀링: 100ms 이내 연속 호출은 무시 (드래그 중 과도한 Seek 방지)
        if (timeSinceLastSeek < 100 && timeSinceLastSeek > 0)
        {
            Log.Debug("[Seek] 쓰로틀링: {TimeSinceLastSeek:F0}ms 이내 연속 Seek 무시", timeSinceLastSeek);
            return;
        }

        // 동일한 위치로 반복 Seek 방지 (0.3초 이내 차이는 무시)
        if (Math.Abs(position - _lastSeekPosition) < 0.3 && _lastSeekPosition >= 0)
        {
            Log.Debug("[Seek] 반복 Seek 무시: position={Position:F2}, lastSeek={LastSeek:F2}", position, _lastSeekPosition);
            return;
        }
        _lastSeekPosition = position;
        _lastSeekTime = now;

        Log.Debug("[Seek] SeekAsync 실행: position={Position:F2}, Duration={Duration:F2}, State={State}",
            position, Duration, State);
        System.Diagnostics.Debug.WriteLine($"[Seek] SeekAsync 실행: position={position:F2}, Duration={Duration:F2}, State={State}");

        var wasPlaying = State == PlayerState.Playing;

        // 재생 중이면 루프 중지
        if (wasPlaying)
        {
            Log.Debug("[Seek] Stopping playback loop...");
            StopPlaybackLoop();
        }

        // 시크 수행 - 항상 BACKWARD 플래그 사용 (키프레임 기준 시크)
        var seekResult = _demuxer.Seek(position, backward: true);
        Log.Debug("[Seek] Demuxer.Seek({Position:F2}) result: {Result}",
            position, seekResult);
        System.Diagnostics.Debug.WriteLine($"[Seek] Demuxer.Seek({position:F2}) result: {seekResult}");

        _clock.Seek(position);

        // 디코더 플러시 - 버퍼에 남아있는 이전 프레임 제거
        _videoDecoder?.Flush();
        _audioDecoder?.Flush();
        _audioRenderer?.ClearBuffer();

        // 시크 후 첫 프레임에서 클럭을 동기화하기 위한 플래그 설정
        _seekTargetPosition = position;
        _isSeekPending = true;

        Log.Debug("[Seek] Decoders flushed, clock position: {ClockPos:F2}, State={State}, seekTarget={SeekTarget:F2}",
            _clock.CurrentTime, State, _seekTargetPosition);


        PositionChanged?.Invoke(this, new PositionChangedEventArgs(position, Duration));

        // 시크 직후 즉시 첫 프레임 렌더링 (응답성 개선)
        RenderFirstFrameSync();

        // 재생 중이었으면 재생 재개
        if (wasPlaying)
        {
            Log.Debug("[Seek] Restarting playback loop...");
            if (State != PlayerState.Playing)
            {
                Log.Debug("[Seek] State changed to {State}, transitioning back to Playing", State);
                _stateMachine.TryTransition(PlayerState.Playing);
            }
            _clock.Start();
            StartPlaybackLoop();
            Log.Debug("[Seek] Playback loop restarted");
        }
    }

    /// <summary>
    /// 시크 후 첫 프레임을 동기적으로 렌더링 (응답성 개선)
    /// </summary>
    private unsafe void RenderFirstFrameSync()
    {
        var targetPos = _seekTargetPosition;
        Log.Debug("[RenderFirstFrameSync] Started, target={Target:F2}", targetPos);

        if (_demuxer == null || _videoDecoder == null || _videoRenderer == null)
        {
            Log.Debug("[RenderFirstFrameSync] Null check failed, skipping");
            _isSeekPending = false;
            _seekTargetPosition = -1;
            return;
        }

        var packet = ffmpeg.av_packet_alloc();
        double renderedPts = -1;

        try
        {
            for (int i = 0; i < 15; i++) // 30 → 15로 감소 (더 빠른 응답)
            {
                if (!_demuxer.ReadPacket(packet))
                {
                    Log.Debug("[RenderFirstFrameSync] No more packets at iteration {i}", i);
                    break;
                }

                if (packet->stream_index == _demuxer.VideoStreamIndex)
                {
                    _videoDecoder.SendPacket(packet);
                    AVFrame* frame;
                    while ((frame = _videoDecoder.ReceiveFrame()) != null)
                    {
                        var pts = frame->pts * ffmpeg.av_q2d(_demuxer.VideoStream->time_base);

                        // 타겟 위치에 가까운 프레임 찾기 (0.2초 이내로 조건 강화)
                        if (pts >= targetPos - 0.2 || i > 5)
                        {
                            var bgraData = _videoDecoder.ConvertFrameToBgra(frame);
                            if (bgraData != null)
                            {
                                _videoRenderer.RenderFrame(bgraData, frame->width, frame->height);
                                renderedPts = pts;
                                Log.Debug("[RenderFirstFrameSync] Rendered frame at pts={Pts:F2}, target={Target:F2}", pts, targetPos);
                            }
                            ffmpeg.av_packet_free(&packet);

                            // Clock만 업데이트 - demuxer는 이미 적절한 위치에 있음
                            // Seek/Flush 생략으로 ~100ms 절약
                            _clock.Seek(renderedPts);
                            Log.Debug("[RenderFirstFrameSync] Updated clock to {Pts:F2}", renderedPts);

                            _isSeekPending = false;
                            _seekTargetPosition = -1;
                            return;
                        }
                    }
                }
                ffmpeg.av_packet_unref(packet);
            }

            Log.Debug("[RenderFirstFrameSync] Loop completed without finding target frame");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[RenderFirstFrameSync] Error occurred");
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            _isSeekPending = false;
            _seekTargetPosition = -1;
        }
    }

    /// <summary>
    /// 시크 후 첫 프레임을 렌더링 (ResetToStart에서 사용)
    /// </summary>
    private unsafe Task RenderFirstFrameAfterSeekAsync()
    {
        Log.Debug("[Seek] RenderFirstFrameAfterSeekAsync started, target={Target:F2}", _seekTargetPosition);
        if (_demuxer == null || _videoDecoder == null || _videoRenderer == null)
        {
            Log.Debug("[Seek] RenderFirstFrameAfterSeekAsync: null check failed");
            _isSeekPending = false;
            _seekTargetPosition = -1;
            return Task.CompletedTask;
        }

        var packet = ffmpeg.av_packet_alloc();
        bool frameRendered = false;
        AVFrame* lastFrame = null;
        double lastPts = 0;
        int videoPacketCount = 0;
        bool shouldRender = false;

        try
        {
            // 최대 100개 패킷을 읽어서 첫 프레임 찾기
            for (int i = 0; i < 100 && !shouldRender; i++)
            {
                if (!_demuxer.ReadPacket(packet))
                {
                    Log.Debug("[Seek] RenderFirstFrame: EOF after {Count} packets", i);
                    break;
                }

                if (packet->stream_index == _demuxer.VideoStreamIndex)
                {
                    videoPacketCount++;
                    _videoDecoder.SendPacket(packet);

                    // 디코더에서 프레임 받기 시도 (여러 번)
                    AVFrame* frame;
                    while ((frame = _videoDecoder.ReceiveFrame()) != null)
                    {
                        var pts = frame->pts * ffmpeg.av_q2d(_demuxer.VideoStream->time_base);
                        lastFrame = frame;
                        lastPts = pts;
                        Log.Debug("[Seek] RenderFirstFrame: Frame decoded, pts={Pts:F2}", pts);

                        // 목표 위치 근처이면 렌더링 진행
                        if (pts >= _seekTargetPosition - 1.0)
                        {
                            Log.Debug("[Seek] RenderFirstFrame: Target condition met, breaking loop");
                            shouldRender = true;
                            break;
                        }
                    }
                }

                ffmpeg.av_packet_unref(packet);
            }

            Log.Debug("[Seek] RenderFirstFrame: Loop ended, lastFrame={HasFrame}, lastPts={Pts:F2}", lastFrame != null, lastPts);

            // 찾은 프레임이 있으면 렌더링
            if (lastFrame != null)
            {
                Log.Debug("[Seek] RenderFirstFrame: Converting to BGRA...");
                var bgraData = _videoDecoder.ConvertFrameToBgra(lastFrame);
                if (bgraData != null)
                {
                    Log.Debug("[Seek] RenderFirstFrame: Calling RenderFrame...");
                    _videoRenderer.RenderFrame(bgraData, lastFrame->width, lastFrame->height);
                    _clock.Seek(lastPts);
                    frameRendered = true;
                    Log.Debug("[Seek] RenderFirstFrame: Rendered at pts={Pts:F2}, target={Target:F2}", lastPts, _seekTargetPosition);
                }
                else
                {
                    Log.Warning("[Seek] RenderFirstFrame: BGRA conversion failed");
                }
            }

            if (!frameRendered)
            {
                Log.Warning("[Seek] RenderFirstFrame: No frame rendered (videoPackets={Count})", videoPacketCount);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Seek] Error rendering first frame");
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            _isSeekPending = false;
            _seekTargetPosition = -1;
            Log.Debug("[Seek] RenderFirstFrameAfterSeekAsync completed");
        }

        return Task.CompletedTask;
    }

    public async Task SeekRelativeAsync(double offset)
    {
        await SeekAsync(Position + offset);
    }

    public void StepFrame(bool forward)
    {
        // 프레임 단위 이동 (구현 예정)
        var frameTime = 1.0 / 30.0; // 기본 30fps 가정
        if (CurrentMedia?.VideoStreams.Count > 0)
        {
            var fps = CurrentMedia.VideoStreams[0].FrameRate;
            if (fps > 0)
                frameTime = 1.0 / fps;
        }

        _ = SeekRelativeAsync(forward ? frameTime : -frameTime);
    }

    public bool LoadSubtitle(string path)
    {
        return SubtitleManager.Load(path);
    }

    public void SetAudioTrack(int index)
    {
        // 오디오 트랙 변경 (구현 예정)
    }

    public void SetSubtitleTrack(int index)
    {
        // 자막 트랙 변경 (구현 예정)
    }

    private void StartPlaybackLoop()
    {
        StopPlaybackLoop();
        Log.Debug("Starting playback loop...");

        _playbackCts = new CancellationTokenSource();
        _playbackTask = Task.Run(() => PlaybackLoop(_playbackCts.Token));
    }

    private void StopPlaybackLoop()
    {
        _playbackCts?.Cancel();
        // 대기 없이 즉시 취소 - PlaybackLoop는 ct.IsCancellationRequested로 종료됨
        _playbackCts?.Dispose();
        _playbackCts = null;
        _playbackTask = null;
    }

    private unsafe void PlaybackLoop(CancellationToken ct)
    {
        Log.Debug("Playback loop started, State={State}", State);
        Log.Debug("VideoStreamIndex={VideoIdx}, AudioStreamIndex={AudioIdx}",
            _demuxer?.VideoStreamIndex ?? -1, _demuxer?.AudioStreamIndex ?? -1);

        var packet = ffmpeg.av_packet_alloc();
        var lastPositionUpdate = Stopwatch.StartNew();

        try
        {
            int frameCount = 0;
            int videoFrameCount = 0;
            int audioFrameCount = 0;

            while (!ct.IsCancellationRequested && State == PlayerState.Playing)
            {
                // A-B 구간 반복 체크
                if (ABRepeat.ShouldLoop(Position))
                {
                    _ = SeekAsync(ABRepeat.PointA!.Value);
                    continue;
                }

                // 패킷 읽기
                if (!_demuxer!.ReadPacket(packet))
                {
                    Log.Debug("ReadPacket returned false (EOF or error) after {FrameCount} packets", frameCount);

                    // EOF 도달 시 Seek 플래그 해제 (데드락 방지)
                    if (_isSeekPending)
                    {
                        Log.Debug("[Seek] EOF 도달로 _isSeekPending 해제");
                        _isSeekPending = false;
                        _seekTargetPosition = -1;
                    }

                    // EOF
                    if (State == PlayerState.Playing)
                    {
                        _stateMachine.TryTransition(PlayerState.Ended);
                    }
                    break;
                }

                frameCount++;
                if (frameCount <= 5 || frameCount % 100 == 0)
                {
                    Log.Debug("Packet {Count}: stream_index={StreamIndex}, size={Size}",
                        frameCount, packet->stream_index, packet->size);
                }

                // 비디오 패킷 처리
                if (packet->stream_index == _demuxer.VideoStreamIndex && _videoDecoder != null)
                {
                    videoFrameCount++;
                    if (videoFrameCount == 1)
                        Log.Debug("Processing first video packet");
                    ProcessVideoPacket(packet);
                }
                // 오디오 패킷 처리
                else if (packet->stream_index == _demuxer.AudioStreamIndex && _audioDecoder != null)
                {
                    audioFrameCount++;
                    if (audioFrameCount == 1)
                        Log.Debug("Processing first audio packet");
                    ProcessAudioPacket(packet);
                }

                ffmpeg.av_packet_unref(packet);

                // 위치 업데이트 (100ms마다)
                if (lastPositionUpdate.ElapsedMilliseconds >= 100)
                {
                    PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position, Duration));
                    lastPositionUpdate.Restart();
                }
            }

            Log.Debug("Playback loop ended: {VideoFrames} video frames, {AudioFrames} audio frames processed",
                videoFrameCount, audioFrameCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in playback loop");
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
        }
    }

    private static int _videoFrameRenderedCount = 0;

    private unsafe void ProcessVideoPacket(AVPacket* packet)
    {
        if (_videoDecoder == null || _videoRenderer == null)
            return;

        try
        {
            _videoDecoder.SendPacket(packet);

            var frame = _videoDecoder.ReceiveFrame();
            if (frame != null)
            {
                // 프레임 PTS 계산
                var pts = frame->pts * ffmpeg.av_q2d(_demuxer!.VideoStream->time_base);

                // 시크 후 첫 프레임 처리: 목표 위치 이전의 프레임은 스킵
                if (_isSeekPending)
                {
                    // 목표 위치보다 0.5초 이상 이전 프레임은 스킵 (키프레임 간격 고려)
                    if (pts < _seekTargetPosition - 0.5)
                    {
                        Log.Debug("[Seek] Skipping frame: pts={Pts:F2} < target={Target:F2}", pts, _seekTargetPosition);
                        return; // 프레임 스킵
                    }

                    // 목표 위치 근처의 프레임을 찾았으면 클럭을 동기화
                    Log.Debug("[Seek] First frame after seek: pts={Pts:F2}, syncing clock from {Clock:F2}",
                        pts, _clock.CurrentTime);
                    _clock.Seek(pts); // 클럭을 실제 프레임 PTS로 재설정
                    _isSeekPending = false;
                    _seekTargetPosition = -1;
                }

                // A/V 동기화: 프레임 표시 시점까지 대기 (SpinWait 기반 정밀 대기)
                var delay = pts - _clock.CurrentTime;
                if (delay > 0 && delay < 1.0)
                {
                    var targetDelayMs = delay * 1000 / _playbackSpeed;
                    if (targetDelayMs > 5)
                    {
                        // 5ms 이상은 Thread.Sleep (CPU 부하 감소)
                        Thread.Sleep((int)(targetDelayMs - 2));
                    }
                    // 남은 시간은 SpinWait으로 정밀 대기
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var targetTicks = (long)(targetDelayMs * System.Diagnostics.Stopwatch.Frequency / 1000);
                    while (sw.ElapsedTicks < targetTicks)
                    {
                        Thread.SpinWait(10);
                    }
                }
                else if (delay < -0.1)
                {
                    // 프레임이 너무 늦음 - 스킵하여 동기화 복구
                    Log.Debug("[Sync] Frame too late, skipping: pts={Pts:F2}, clock={Clock:F2}, delay={Delay:F2}",
                        pts, _clock.CurrentTime, delay);
                    return;
                }

                // 프레임 렌더링
                var bgraData = _videoDecoder.ConvertFrameToBgra(frame);
                if (bgraData != null)
                {
                    _videoRenderer.RenderFrame(bgraData, frame->width, frame->height);
                    _videoFrameRenderedCount++;
                    if (_videoFrameRenderedCount == 1 || _videoFrameRenderedCount % 30 == 0)
                    {
                        Log.Debug("Video frame rendered: count={Count}, size={Width}x{Height}, pts={Pts:F2}s",
                            _videoFrameRenderedCount, frame->width, frame->height, pts);
                    }
                }
                else
                {
                    Log.Warning("ConvertFrameToBgra returned null for frame {Width}x{Height}, format={Format}",
                        frame->width, frame->height, frame->format);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing video packet");
        }
    }

    private unsafe void ProcessAudioPacket(AVPacket* packet)
    {
        if (_audioDecoder == null || _audioRenderer == null)
            return;

        _audioDecoder.SendPacket(packet);

        var audioData = _audioDecoder.ReceiveAudio();
        if (audioData != null)
        {
            _audioRenderer.AddSamples(audioData);
        }
    }

    public async Task<string?> SaveScreenshotAsync(string? folderPath = null)
    {
        if (_videoRenderer?.Bitmap == null)
        {
            Log.Warning("스크린샷 실패: 비디오 프레임이 없습니다.");
            return null;
        }

        try
        {
            // 저장 폴더 결정
            var folder = folderPath ?? _settings.Screenshot.SavePath;
            if (string.IsNullOrEmpty(folder))
            {
                folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Wiplayer Screenshots");
            }

            // 폴더 생성
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // 파일명 생성
            var fileName = CurrentMedia?.FileName ?? "screenshot";
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var position = TimeSpan.FromSeconds(Position).ToString(@"hh\-mm\-ss");
            var fullPath = Path.Combine(folder, $"{fileName}_{position}_{timestamp}.png");

            // UI 스레드에서 비트맵 저장
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                using var fileStream = new FileStream(fullPath, FileMode.Create);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(_videoRenderer.Bitmap));
                encoder.Save(fileStream);
            });

            Log.Information("스크린샷 저장 완료: {Path}", fullPath);
            return fullPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "스크린샷 저장 실패");
            return null;
        }
    }

    public void OpenRecentFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Log.Warning("최근 파일 열기 실패: 파일이 존재하지 않음 - {Path}", path);
            return;
        }

        // UI 스레드에서 OpenAsync 호출
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var success = await OpenAsync(path);
                if (success)
                {
                    Play();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "최근 파일 열기 오류: {Path}", path);
            }
        });
    }

    public void AddBookmark(string? title = null)
    {
        var media = CurrentMedia;
        if (media == null)
        {
            Log.Warning("북마크 추가 실패: 재생 중인 미디어가 없음");
            return;
        }

        _bookmarks.Add(media.Path, Position, title);
        Log.Information("북마크 추가: {Path} at {Position:F2}s", media.Path, Position);
    }

    public async Task GoToBookmark(Core.Playlist.BookmarkItem bookmark)
    {
        if (bookmark == null)
        {
            Log.Warning("북마크 이동 실패: 북마크가 null");
            return;
        }

        // 현재 재생 중인 파일과 다르면 열기
        if (CurrentMedia == null || !CurrentMedia.Path.Equals(bookmark.MediaPath, StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(bookmark.MediaPath))
            {
                Log.Warning("북마크 이동 실패: 파일이 존재하지 않음 - {Path}", bookmark.MediaPath);
                return;
            }

            var success = await OpenAsync(bookmark.MediaPath);
            if (!success) return;
        }

        // 해당 위치로 이동
        await SeekAsync(bookmark.Position);
        Play();
        Log.Information("북마크로 이동: {Path} at {Position:F2}s", bookmark.MediaPath, bookmark.Position);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

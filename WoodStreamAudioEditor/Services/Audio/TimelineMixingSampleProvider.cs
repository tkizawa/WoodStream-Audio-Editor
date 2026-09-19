using System;
using NAudio.Wave;

namespace WoodStreamAudioEditor.Services.Audio;

/// <summary>
/// BGM および エンディング曲のトラック定義
/// </summary>
public class MixingTrack
{
    public ISampleProvider Source { get; }
    public float Volume { get; set; } = 1.0f;
    public long StartFrame { get; set; } = 0;
    public long? StopFrame { get; set; } = null;
    public bool Loop { get; set; } = false;
    public Action? OnLoopReset { get; set; }

    // フェード設定 (トラックのローカルフレームまたは全体フレーム)
    public long? FadeOutStartFrame { get; set; } = null;
    public long FadeOutDurationFrames { get; set; } = 0;

    public long? FadeInStartFrame { get; set; } = null;
    public long FadeInDurationFrames { get; set; } = 0;

    public MixingTrack(ISampleProvider source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }
}

/// <summary>
/// ポッドキャスト本編音声、BGM、エンディング曲を精密なタイムラインとフェードで合成するマルチトラックミキサー
/// </summary>
public class TimelineMixingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _mainVoice;
    private readonly MixingTrack? _bgmTrack;
    private readonly MixingTrack? _endingTrack;
    private readonly WaveFormat _waveFormat;
    private readonly int _channels;
    private readonly int _sampleRate;

    private readonly long _mainVoiceTotalFrames;
    private readonly long _totalMixFrames;

    private long _currentFrame = 0;
    private float[] _voiceBuffer = new float[2048];
    private float[] _bgmBuffer = new float[2048];
    private float[] _endingBuffer = new float[2048];

    public WaveFormat WaveFormat => _waveFormat;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="mainVoice">本編音声 (48kHz, 2ch)</param>
    /// <param name="mainVoiceTotalFrames">本編音声の総フレーム数</param>
    /// <param name="bgmTrack">BGMトラック（任意）</param>
    /// <param name="endingTrack">エンディング曲トラック（任意）</param>
    /// <param name="totalMixFrames">ミキシング全体の想定総フレーム数</param>
    public TimelineMixingSampleProvider(
        ISampleProvider mainVoice,
        long mainVoiceTotalFrames,
        MixingTrack? bgmTrack = null,
        MixingTrack? endingTrack = null,
        long? totalMixFrames = null)
    {
        _mainVoice = mainVoice ?? throw new ArgumentNullException(nameof(mainVoice));
        _waveFormat = mainVoice.WaveFormat;
        _channels = _waveFormat.Channels;
        _sampleRate = _waveFormat.SampleRate;

        _mainVoiceTotalFrames = mainVoiceTotalFrames;
        _bgmTrack = bgmTrack;
        _endingTrack = endingTrack;

        // 全体の終了フレーム数
        long calculatedTotal = mainVoiceTotalFrames;
        if (endingTrack?.StopFrame.HasValue == true)
        {
            calculatedTotal = Math.Max(calculatedTotal, endingTrack.StopFrame.Value);
        }
        _totalMixFrames = totalMixFrames ?? calculatedTotal;
    }

    public int Read(Span<float> buffer)
    {
        int requestedSamples = buffer.Length;
        int requestedFrames = requestedSamples / _channels;

        if (_currentFrame >= _totalMixFrames && _totalMixFrames > 0)
        {
            return 0; // 全トラック完了 (EOF)
        }

        long framesRemaining = _totalMixFrames > 0 ? (_totalMixFrames - _currentFrame) : requestedFrames;
        int framesToProcess = (int)Math.Min(requestedFrames, framesRemaining);
        if (framesToProcess <= 0) return 0;

        int samplesToProcess = framesToProcess * _channels;
        buffer.Slice(0, samplesToProcess).Clear();

        // 1. 本編音声の読み出しとミキシング
        if (_currentFrame < _mainVoiceTotalFrames)
        {
            int voiceFramesToRead = (int)Math.Min(framesToProcess, _mainVoiceTotalFrames - _currentFrame);
            int voiceSamplesToRead = voiceFramesToRead * _channels;

            EnsureBufferSize(ref _voiceBuffer, voiceSamplesToRead);
            int voiceSamplesRead = _mainVoice.Read(_voiceBuffer.AsSpan(0, voiceSamplesToRead));

            for (int i = 0; i < voiceSamplesRead; i++)
            {
                buffer[i] += _voiceBuffer[i];
            }
        }

        // 2. BGM トラックのミキシング
        if (_bgmTrack != null)
        {
            MixTrackIntoBuffer(_bgmTrack, buffer, framesToProcess, ref _bgmBuffer);
        }

        // 3. エンディング曲トラックのミキシング
        if (_endingTrack != null)
        {
            MixTrackIntoBuffer(_endingTrack, buffer, framesToProcess, ref _endingBuffer);
        }

        // 4. ピークリミッター / ソフトクリッピング (1.0 を超えた場合の歪み防止)
        for (int i = 0; i < samplesToProcess; i++)
        {
            float val = buffer[i];
            if (val > 1.0f) buffer[i] = 1.0f;
            else if (val < -1.0f) buffer[i] = -1.0f;
        }

        _currentFrame += framesToProcess;
        return samplesToProcess;
    }

    private void MixTrackIntoBuffer(MixingTrack track, Span<float> outputBuffer, int framesToProcess, ref float[] tempBuffer)
    {
        // 再生区間の重複範囲を計算
        long blockStart = _currentFrame;
        long blockEnd = _currentFrame + framesToProcess;

        long trackStart = track.StartFrame;
        long trackEnd = track.StopFrame ?? long.MaxValue;

        if (blockEnd <= trackStart || blockStart >= trackEnd)
        {
            return; // このブロックはトラックの再生区間外
        }

        int activeStartOffset = (int)Math.Max(0, trackStart - blockStart);
        int activeEndOffset = (int)Math.Min(framesToProcess, trackEnd - blockStart);
        int activeFrames = activeEndOffset - activeStartOffset;

        if (activeFrames <= 0) return;

        int activeSamples = activeFrames * _channels;
        EnsureBufferSize(ref tempBuffer, activeSamples);
        Span<float> trackSpan = tempBuffer.AsSpan(0, activeSamples);

        // トラックからサンプルを一括読み出し（ループ対応）
        int totalRead = 0;
        while (totalRead < activeSamples)
        {
            int read = track.Source.Read(trackSpan.Slice(totalRead));
            if (read == 0)
            {
                if (track.Loop)
                {
                    track.OnLoopReset?.Invoke();
                    int loopRead = track.Source.Read(trackSpan.Slice(totalRead));
                    if (loopRead == 0)
                    {
                        trackSpan.Slice(totalRead).Clear();
                        break;
                    }
                    totalRead += loopRead;
                }
                else
                {
                    trackSpan.Slice(totalRead).Clear();
                    break;
                }
            }
            else
            {
                totalRead += read;
            }
        }

        // ゲインとフェードを適用しながら出力バッファへ加算
        for (int f = 0; f < activeFrames; f++)
        {
            int blockFrameIndex = activeStartOffset + f;
            long absoluteFrame = blockStart + blockFrameIndex;

            float gain = track.Volume;

            // フェードイン
            if (track.FadeInStartFrame.HasValue && track.FadeInDurationFrames > 0)
            {
                long progress = absoluteFrame - track.FadeInStartFrame.Value;
                if (progress < 0)
                {
                    gain = 0.0f;
                }
                else if (progress < track.FadeInDurationFrames)
                {
                    gain *= (float)progress / track.FadeInDurationFrames;
                }
            }

            // フェードアウト
            if (track.FadeOutStartFrame.HasValue && track.FadeOutDurationFrames > 0)
            {
                long progress = absoluteFrame - track.FadeOutStartFrame.Value;
                if (progress >= track.FadeOutDurationFrames)
                {
                    gain = 0.0f;
                }
                else if (progress >= 0)
                {
                    gain *= (1.0f - ((float)progress / track.FadeOutDurationFrames));
                }
            }

            for (int c = 0; c < _channels; c++)
            {
                outputBuffer[blockFrameIndex * _channels + c] += trackSpan[f * _channels + c] * gain;
            }
        }
    }

    private static void EnsureBufferSize(ref float[] buffer, int requiredSize)
    {
        if (buffer.Length < requiredSize)
        {
            buffer = new float[Math.Max(buffer.Length * 2, requiredSize)];
        }
    }
}

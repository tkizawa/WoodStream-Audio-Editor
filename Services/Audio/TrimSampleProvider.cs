using System;
using NAudio.Wave;

namespace WoodStreamAudioEditor.Services.Audio;

/// <summary>
/// 指定されたフレーム数（再生時間）だけサンプルを提供し、完了時にストリームを終了（EOF）する SampleProvider
/// カット開始時のフェードイン（5ms）およびカット終了時のフェードアウト（5ms）を自動適用し、
/// トリミング境界でのクリップノイズ・クリック音を完全に防止します。
/// </summary>
public class TrimSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly long _maxFrames;
    private readonly int _channels;
    private readonly int _fadeFrames;

    private long _framesRead = 0;

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="source">入力オーディオソース</param>
    /// <param name="duration">切り出す再生時間</param>
    public TrimSampleProvider(ISampleProvider source, TimeSpan duration)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _channels = source.WaveFormat.Channels;

        // 最大フレーム数を算出
        _maxFrames = Math.Max(0, (long)(duration.TotalSeconds * source.WaveFormat.SampleRate));

        // 5ms 分のフェードフレーム数（48kHzなら240フレーム）
        _fadeFrames = Math.Max(1, (int)(source.WaveFormat.SampleRate * 0.005));
    }

    /// <summary>
    /// サンプルデータを読み出します
    /// </summary>
    public int Read(Span<float> buffer)
    {
        if (_framesRead >= _maxFrames)
        {
            return 0; // 切り出し終了 (EOF)
        }

        long framesRemaining = _maxFrames - _framesRead;
        int framesRequested = buffer.Length / _channels;
        int framesToRead = (int)Math.Min(framesRequested, framesRemaining);

        if (framesToRead <= 0)
        {
            return 0;
        }

        int samplesNeeded = framesToRead * _channels;
        int samplesRead = _source.Read(buffer.Slice(0, samplesNeeded));
        if (samplesRead == 0)
        {
            return 0;
        }

        int actualFramesRead = samplesRead / _channels;

        // 1. 開始フェードイン処理（先頭 5ms）
        if (_framesRead < _fadeFrames)
        {
            int fadeOffset = (int)_framesRead;
            int fadeCount = Math.Min(actualFramesRead, _fadeFrames - fadeOffset);

            for (int f = 0; f < fadeCount; f++)
            {
                float gain = (float)(fadeOffset + f) / _fadeFrames;
                for (int c = 0; c < _channels; c++)
                {
                    buffer[f * _channels + c] *= gain;
                }
            }
        }

        // 2. 終了フェードアウト処理（末尾 5ms）
        long framesAfterThisRead = _framesRead + actualFramesRead;
        long fadeOutStartFrame = _maxFrames - _fadeFrames;

        if (framesAfterThisRead > fadeOutStartFrame)
        {
            for (int f = 0; f < actualFramesRead; f++)
            {
                long currentFrame = _framesRead + f;
                if (currentFrame >= fadeOutStartFrame)
                {
                    long fadeIndex = _maxFrames - currentFrame;
                    float gain = Math.Clamp((float)fadeIndex / _fadeFrames, 0.0f, 1.0f);
                    for (int c = 0; c < _channels; c++)
                    {
                        buffer[f * _channels + c] *= gain;
                    }
                }
            }
        }

        _framesRead += actualFramesRead;
        return samplesRead;
    }
}

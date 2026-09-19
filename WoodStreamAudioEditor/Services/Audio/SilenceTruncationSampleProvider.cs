using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace WoodStreamAudioEditor.Services.Audio;

/// <summary>
/// 音量（dB）が指定した閾値以下の無音・環境音部分を自動的に検知・短縮する SampleProvider
/// 指定した「保持する最小無音時間」を超える不要なポーズ部分をカットし、
/// クリックノイズ防止のクロスフェードを適用して自然なポッドキャスト音声を生成します。
/// </summary>
public class SilenceTruncationSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly double _thresholdDb;
    private readonly double _thresholdLinear;
    private readonly int _minSilenceFrames;
    private readonly int _windowSizeFrames;
    private readonly int _fadeFrames;
    private readonly int _channels;

    // 内部バッファと状態
    private readonly float[] _windowBuffer;
    private readonly Queue<float> _outputQueue = new();
    private bool _inSilence = false;
    private int _consecutiveSilenceFrames = 0;
    private long _totalInputFrames = 0;
    private long _totalOutputFrames = 0;

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>カットされたフレーム総数</summary>
    public long TruncatedFrames => Math.Max(0, _totalInputFrames - _totalOutputFrames);

    /// <summary>カットされた総時間（秒）</summary>
    public double TruncatedDurationSeconds => (double)TruncatedFrames / WaveFormat.SampleRate;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="source">入力オーディオプロバイダ</param>
    /// <param name="thresholdDb">無音判定閾値 (dB, 例: -45.0)</param>
    /// <param name="minSilenceDurationMs">保持する最小無音時間 (ミリ秒, 例: 400ms)</param>
    public SilenceTruncationSampleProvider(ISampleProvider source, double thresholdDb = -45.0, int minSilenceDurationMs = 400)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _thresholdDb = thresholdDb;
        _thresholdLinear = Math.Pow(10.0, thresholdDb / 20.0);
        _channels = source.WaveFormat.Channels;

        // 20ミリ秒を1評価ウィンドウとする
        _windowSizeFrames = Math.Max(1, (int)(source.WaveFormat.SampleRate * 0.020));
        _minSilenceFrames = Math.Max(1, (int)(source.WaveFormat.SampleRate * (minSilenceDurationMs / 1000.0)));
        // 5ミリ秒のフェード時間（クリックノイズ抑制）
        _fadeFrames = Math.Max(1, (int)(source.WaveFormat.SampleRate * 0.005));

        _windowBuffer = new float[_windowSizeFrames * _channels];
    }

    public int Read(Span<float> buffer)
    {
        int written = 0;
        int count = buffer.Length;

        while (written < count)
        {
            // キューにたまっている処理済みサンプルがあれば先に吐き出す
            while (_outputQueue.Count > 0 && written < count)
            {
                buffer[written++] = _outputQueue.Dequeue();
                if ((written % _channels) == 0)
                {
                    _totalOutputFrames++;
                }
            }

            if (written >= count) break;

            // ソースから1ウィンドウ分のフレームを読み出す
            int samplesRead = _source.Read(_windowBuffer.AsSpan(0, _windowSizeFrames * _channels));
            if (samplesRead == 0)
            {
                // ソース終了
                break;
            }

            int framesRead = samplesRead / _channels;
            _totalInputFrames += framesRead;

            // ウィンドウ内の RMS（二乗平均平方根）を計算
            double sumSquares = 0;
            for (int i = 0; i < samplesRead; i++)
            {
                sumSquares += _windowBuffer[i] * _windowBuffer[i];
            }
            double rms = Math.Sqrt(sumSquares / samplesRead);
            bool isSilentWindow = rms < _thresholdLinear;

            if (isSilentWindow)
            {
                _consecutiveSilenceFrames += framesRead;

                if (_consecutiveSilenceFrames <= _minSilenceFrames)
                {
                    // 保持する最小無音時間以内の場合はそのまま出力
                    for (int i = 0; i < samplesRead; i++)
                    {
                        _outputQueue.Enqueue(_windowBuffer[i]);
                    }
                }
                else
                {
                    // 最小無音時間を超えた余剰な無音部分はスキップ（出力キューに入れない）
                    if (!_inSilence && _outputQueue.Count >= _fadeFrames * _channels)
                    {
                        _inSilence = true;
                    }
                }
            }
            else
            {
                // 音声検出
                if (_inSilence)
                {
                    // 無音スキップからの復帰時：クリック音防止のため先頭をスムーズにフェードイン
                    _inSilence = false;
                    int fadeSampleCount = Math.Min(samplesRead, _fadeFrames * _channels);
                    int fadeFrameCount = fadeSampleCount / _channels;

                    for (int f = 0; f < fadeFrameCount; f++)
                    {
                        float gain = (float)f / fadeFrameCount;
                        for (int c = 0; c < _channels; c++)
                        {
                            _windowBuffer[f * _channels + c] *= gain;
                        }
                    }
                }

                _consecutiveSilenceFrames = 0;

                // サンプルを出力キューに追加
                for (int i = 0; i < samplesRead; i++)
                {
                    _outputQueue.Enqueue(_windowBuffer[i]);
                }
            }
        }

        return written;
    }
}

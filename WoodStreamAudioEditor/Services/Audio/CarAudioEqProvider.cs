using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace WoodStreamAudioEditor.Services.Audio;

/// <summary>
/// 車内向け音声チューニング（ローカット＆明瞭度アップ）用イコライザー Provider
/// 車内の走行音（ロードノイズ・低周波振動）を抑え、走行中でも声の輪郭と芯がクリアに聞き取れるよう、
/// 専門家チューニングによる固定プリセット（31バンドEQ相当）を複数のBiQuadFilter直列接続で適用します。
/// </summary>
public class CarAudioEqProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly BiQuadFilter[][] _filters;

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="source">入力オーディオソース</param>
    public CarAudioEqProvider(ISampleProvider source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _channels = source.WaveFormat.Channels;
        float sampleRate = source.WaveFormat.SampleRate;

        // 各チャンネルごとに独立した BiQuadFilter チェーンを構築（位相ズレ・チャンネル間干渉を防止）
        _filters = new BiQuadFilter[_channels][];

        for (int ch = 0; ch < _channels; ch++)
        {
            _filters[ch] = new[]
            {
                // 1. 20Hz 〜 63Hz: -12.0 dB (超低域ロードノイズ・車体振動カット)
                BiQuadFilter.LowShelf(sampleRate, 63.0f, 1.0f, -12.0f),

                // 2. 80Hz: -4.0 dB (低域の篭もり・ブーミー感抑制)
                // 3. 100Hz 〜 125Hz: -4.0 dB (車内定在波・共鳴の抑制)
                // 80Hz〜125Hz帯域をスムーズに -4.0dB 抑制
                BiQuadFilter.PeakingEQ(sampleRate, 80.0f, 2.0f, -3.0f),
                BiQuadFilter.PeakingEQ(sampleRate, 115.0f, 2.0f, -3.0f),

                // 4. 1kHz 〜 2kHz: +4.0 dB (声の存在感・明瞭度・子音プレゼンスのブースト)
                // 中心周波数 1414Hz (1kHzと2kHzの相乗平均)、Q=1.0 (1オクターブ幅) で +4.0dB ブースト
                BiQuadFilter.PeakingEQ(sampleRate, 1414.0f, 1.0f, +4.0f),

                // 5. 12.5kHz 〜 20kHz: -4.0 dB (車内ツイーターでの耳障りなヒスノイズ・金属音カット)
                BiQuadFilter.HighShelf(sampleRate, 12500.0f, 1.0f, -4.0f)
            };
        }
    }

    /// <summary>
    /// フィルターを適用してサンプルを読み出します
    /// </summary>
    public int Read(Span<float> buffer)
    {
        int samplesRead = _source.Read(buffer);
        if (samplesRead == 0) return 0;

        int framesRead = samplesRead / _channels;

        for (int f = 0; f < framesRead; f++)
        {
            for (int ch = 0; ch < _channels; ch++)
            {
                int sampleIndex = f * _channels + ch;
                float sample = buffer[sampleIndex];

                // チャンネルごとの BiQuadFilter チェーンを順次通過
                var chain = _filters[ch];
                for (int i = 0; i < chain.Length; i++)
                {
                    sample = chain[i].Transform(sample);
                }

                // ソフトリミッター / クランプ (-1.0f 〜 1.0f)
                if (sample > 1.0f) sample = 1.0f;
                else if (sample < -1.0f) sample = -1.0f;

                buffer[sampleIndex] = sample;
            }
        }

        return samplesRead;
    }
}

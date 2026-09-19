using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Lame;
using NAudio.Wave;
using WoodStreamAudioEditor.Models;

namespace WoodStreamAudioEditor.Services.Audio;

/// <summary>
/// 音声処理パイプラインの実行情報
/// </summary>
public record PipelineProgress(double Percentage, string Message);

/// <summary>
/// 音声処理パイプライン実行サービス
/// WAV/MP3の読み込み、VSTプラグイン直列適用、無音部分自動カット、およびMP3書き出しを行います。
/// </summary>
public class AudioPipelineService
{
    /// <summary>
    /// 音声処理パイプラインを非同期で実行します。
    /// </summary>
    public async Task<string> ProcessAudioAsync(
        string inputFilePath,
        string outputDirectory,
        AppSettings settings,
        IProgress<PipelineProgress> progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(inputFilePath))
            {
                throw new FileNotFoundException($"入力ファイルが見つかりません: {inputFilePath}");
            }

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string inputFileNameWithoutExt = Path.GetFileNameWithoutExtension(inputFilePath);
            string outputFilePath = Path.Combine(outputDirectory, $"{inputFileNameWithoutExt}_edited.mp3");

            progress.Report(new PipelineProgress(0, $"入力音声ファイルを読み込み中: {Path.GetFileName(inputFilePath)}"));

            // 1. 入力オーディオリーダー（WAV / MP3を自動判別し IEEE Float 32bit で読み込み）
            using var reader = new AudioFileReader(inputFilePath);
            var originalTotalTime = reader.TotalTime;
            progress.Report(new PipelineProgress(5, $"音声フォーマット: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}ch, 元の長さ: {originalTotalTime:mm\\:ss}"));

            ISampleProvider currentProvider = reader;
            VstSampleProvider? deClickVst = null;
            VstSampleProvider? deNoiseVst = null;

            try
            {
                // 2. VST プラグイン 1: De-click
                if (settings.EnableDeClick && !string.IsNullOrWhiteSpace(settings.DeClickPluginPath))
                {
                    progress.Report(new PipelineProgress(10, $"VST [De-click] を適用中..."));
                    deClickVst = new VstSampleProvider(
                        currentProvider,
                        settings.DeClickPluginPath,
                        1024,
                        msg => progress.Report(new PipelineProgress(10, msg)));
                    currentProvider = deClickVst;
                }

                // 3. VST プラグイン 2: Voice De-noise
                if (settings.EnableVoiceDeNoise && !string.IsNullOrWhiteSpace(settings.VoiceDeNoisePluginPath))
                {
                    progress.Report(new PipelineProgress(15, $"VST [Voice De-noise] を適用中..."));
                    deNoiseVst = new VstSampleProvider(
                        currentProvider,
                        settings.VoiceDeNoisePluginPath,
                        1024,
                        msg => progress.Report(new PipelineProgress(15, msg)));
                    currentProvider = deNoiseVst;
                }

                // 4. 無音自動削除 (Silence Truncation)
                SilenceTruncationSampleProvider? silenceProvider = null;
                if (settings.EnableSilenceTruncation)
                {
                    progress.Report(new PipelineProgress(20, $"無音自動カット設定: 閾値={settings.SilenceThresholdDb:F1}dB, 保持時間={settings.MinSilenceDurationMs}ms"));
                    silenceProvider = new SilenceTruncationSampleProvider(
                        currentProvider,
                        settings.SilenceThresholdDb,
                        settings.MinSilenceDurationMs);
                    currentProvider = silenceProvider;
                }

                // 5. MP3エンコード書き出し
                progress.Report(new PipelineProgress(25, $"MP3エンコード開始: {settings.Mp3Bitrate} kbps -> {Path.GetFileName(outputFilePath)}"));

                // IEEE Float 32-bit SampleProvider を 16-bit PCM WaveProvider に変換して Lame に渡す
                var pcmProvider = currentProvider.ToWaveProvider16();

                // LameMP3FileWriter によるエンコード
                using (var writer = new LameMP3FileWriter(outputFilePath, pcmProvider.WaveFormat, settings.Mp3Bitrate))
                {
                    byte[] buffer = new byte[16384]; // 16KB バッファ
                    long totalBytesRead = 0;
                    long estimatedTotalBytes = reader.Length * (pcmProvider.WaveFormat.BitsPerSample / reader.WaveFormat.BitsPerSample);
                    DateTime lastReportTime = DateTime.UtcNow;

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int bytesRead = pcmProvider.Read(buffer.AsSpan());
                        if (bytesRead == 0) break;

                        writer.Write(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;

                        // 進捗率計算 (25% 〜 95%)
                        if ((DateTime.UtcNow - lastReportTime).TotalMilliseconds >= 250)
                        {
                            double ratio = estimatedTotalBytes > 0 
                                ? Math.Min(1.0, (double)reader.Position / reader.Length) 
                                : 0.5;
                            double percent = 25.0 + (ratio * 70.0);
                            progress.Report(new PipelineProgress(percent, $"エンコード中... ({percent:F1}%)"));
                            lastReportTime = DateTime.UtcNow;
                        }
                    }
                }

                // 統計情報
                if (silenceProvider != null)
                {
                    double cutSeconds = silenceProvider.TruncatedDurationSeconds;
                    double percentSaved = originalTotalTime.TotalSeconds > 0 
                        ? (cutSeconds / originalTotalTime.TotalSeconds) * 100.0 
                        : 0;
                    progress.Report(new PipelineProgress(95, $"無音カット統計: {cutSeconds:F1} 秒カット ({percentSaved:F1}% 短縮)"));
                }

                progress.Report(new PipelineProgress(98, $"MP3エクスポート完了: {Path.GetFileName(outputFilePath)}"));
                return outputFilePath;
            }
            finally
            {
                deClickVst?.Dispose();
                deNoiseVst?.Dispose();
            }
        }, cancellationToken);
    }
}

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
public record PipelineProgress(double Percentage, string Message, bool LogToConsole = true);

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

            progress.Report(new PipelineProgress(0, $"入力音声ファイルを読み込み中: {Path.GetFileName(inputFilePath)}", true));

            // 1. 入力オーディオリーダー（WAV / MP3を自動判別し IEEE Float 32bit で読み込み）
            using var reader = new AudioFileReader(inputFilePath);
            var originalTotalTime = reader.TotalTime;
            progress.Report(new PipelineProgress(5, $"音声フォーマット: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}ch, 元の長さ: {originalTotalTime:mm\\:ss}", true));

            ISampleProvider currentProvider = reader;

            // ハイレゾ音源 (96kHz / 192kHz 等) の場合、VST 処理および無音カットに先立ち、
            // ポッドキャスト・音楽制作標準の 48kHz (または 44.1kHz) に高品質リサンプリングします。
            // これにより、VST プラグインが最も得意とする周波数特性で安定動作し、ノイズや歪みを完全に防止します。
            int originalSampleRate = currentProvider.WaveFormat.SampleRate;
            if (originalSampleRate > 48000)
            {
                int targetSampleRate = (originalSampleRate % 44100 == 0) ? 44100 : 48000;
                progress.Report(new PipelineProgress(8, $"ハイレゾ音源 ({originalSampleRate}Hz) を標準サンプリングレート ({targetSampleRate}Hz) に高品質リサンプリング中...", true));
                currentProvider = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(currentProvider, targetSampleRate);
            }

            // 1.5. トリミング（先頭・末尾カット）の適用
            if (settings.EnableTrim && (settings.TrimStartSeconds > 0 || settings.TrimEndSeconds > 0))
            {
                double startSec = Math.Max(0, settings.TrimStartSeconds);
                double endSec = Math.Max(0, settings.TrimEndSeconds);
                double totalSec = originalTotalTime.TotalSeconds;

                if (startSec + endSec >= totalSec)
                {
                    progress.Report(new PipelineProgress(6, $"[警告] カット秒数の合計 ({startSec + endSec:F1}秒) が音声の長さ ({totalSec:F1}秒) を超えているため、トリミングをスキップします。", true));
                }
                else
                {
                    double remainingSec = totalSec - startSec - endSec;
                    progress.Report(new PipelineProgress(6, $"トリミング適用: 先頭 {startSec:F1}秒カット, 末尾 {endSec:F1}秒カット (有効時間: {TimeSpan.FromSeconds(remainingSec):mm\\:ss})", true));

                    if (startSec > 0)
                    {
                        reader.CurrentTime = TimeSpan.FromSeconds(startSec);
                    }

                    currentProvider = new TrimSampleProvider(currentProvider, TimeSpan.FromSeconds(remainingSec));
                }
            }

            VstSampleProvider? deClickVst = null;
            VstSampleProvider? deNoiseVst = null;

            try
            {
                // 2. VST プラグイン 1: De-click
                if (settings.EnableDeClick && !string.IsNullOrWhiteSpace(settings.DeClickPluginPath))
                {
                    progress.Report(new PipelineProgress(10, $"VST [De-click] を適用中... ({Path.GetFileName(settings.DeClickPluginPath)})", true));
                    deClickVst = new VstSampleProvider(
                        currentProvider,
                        settings.DeClickPluginPath,
                        1024,
                        msg => progress.Report(new PipelineProgress(10, msg, true)));
                    currentProvider = deClickVst;
                }

                // 3. VST プラグイン 2: Voice De-noise
                if (settings.EnableVoiceDeNoise && !string.IsNullOrWhiteSpace(settings.VoiceDeNoisePluginPath))
                {
                    progress.Report(new PipelineProgress(15, $"VST [Voice De-noise] を適用中... ({Path.GetFileName(settings.VoiceDeNoisePluginPath)})", true));
                    deNoiseVst = new VstSampleProvider(
                        currentProvider,
                        settings.VoiceDeNoisePluginPath,
                        1024,
                        msg => progress.Report(new PipelineProgress(15, msg, true)));
                    currentProvider = deNoiseVst;
                }

                // 4. 無音自動削除 (Silence Truncation)
                SilenceTruncationSampleProvider? silenceProvider = null;
                if (settings.EnableSilenceTruncation)
                {
                    progress.Report(new PipelineProgress(20, $"無音自動カット設定: 閾値={settings.SilenceThresholdDb:F1}dB, 保持時間={settings.MinSilenceDurationMs}ms", true));
                    silenceProvider = new SilenceTruncationSampleProvider(
                        currentProvider,
                        settings.SilenceThresholdDb,
                        settings.MinSilenceDurationMs);
                    currentProvider = silenceProvider;
                }

                // 5. MP3エンコード書き出し
                progress.Report(new PipelineProgress(25, $"MP3エンコード準備: {settings.Mp3Bitrate} kbps -> {Path.GetFileName(outputFilePath)}", true));

                // IEEE Float 32-bit SampleProvider を 16-bit PCM WaveProvider に変換して Lame に渡す
                var pcmProvider = currentProvider.ToWaveProvider16();

                // LameMP3FileWriter によるエンコード
                using (var writer = new LameMP3FileWriter(outputFilePath, pcmProvider.WaveFormat, settings.Mp3Bitrate))
                {
                    byte[] buffer = new byte[32768]; // 32KB バッファ
                    long totalBytesWritten = 0;
                    DateTime lastReportTime = DateTime.UtcNow;
                    int lastReportedMilestone = 25;

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int bytesRead = pcmProvider.Read(buffer.AsSpan());
                        if (bytesRead == 0) break;

                        writer.Write(buffer, 0, bytesRead);
                        totalBytesWritten += bytesRead;

                        // reader.Position / reader.Length に基づいて正確に進捗率 (25% 〜 95%) を計算
                        double ratio = reader.Length > 0 
                            ? Math.Clamp((double)reader.Position / reader.Length, 0.0, 1.0) 
                            : 0.0;
                        double percent = 25.0 + (ratio * 70.0);

                        // プログレスバーの更新 (LogToConsole = false)
                        if ((DateTime.UtcNow - lastReportTime).TotalMilliseconds >= 250)
                        {
                            progress.Report(new PipelineProgress(percent, $"エンコード中... ({percent:F0}%)", false));
                            lastReportTime = DateTime.UtcNow;
                        }

                        // 25%ごとの主要マイルストーン時のみログに出力
                        int milestone = ((int)percent / 25) * 25;
                        if (milestone > lastReportedMilestone && milestone < 95)
                        {
                            progress.Report(new PipelineProgress(percent, $"音声処理・エンコード進行中: {milestone}% 完了", true));
                            lastReportedMilestone = milestone;
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

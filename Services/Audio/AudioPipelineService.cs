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

                // 3.5. 車内向け音声チューニング（ローカット＆明瞭度アップ イコライザー）
                if (settings.EnableCarAudioEq)
                {
                    progress.Report(new PipelineProgress(18, "車内向け音声チューニング（ローカット＆明瞭度アップEQ）を適用中...", true));
                    currentProvider = new CarAudioEqProvider(currentProvider);
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

                // 5. BGM / エンディング曲のミキシング判定
                bool hasBgm = settings.EnableBgm && !string.IsNullOrWhiteSpace(settings.BgmFilePath) && File.Exists(settings.BgmFilePath);
                bool hasEnding = settings.EnableEnding && !string.IsNullOrWhiteSpace(settings.EndingFilePath) && File.Exists(settings.EndingFilePath);
                bool requiresMixing = hasBgm || hasEnding;

                if (requiresMixing)
                {
                    // 【2パス方式】
                    // パス1: 音声処理（トリミング、VST、無音カット）済みの本編音声を一時WAVファイルへ出力して正確な長さを確定
                    string tempVoiceWav = Path.Combine(outputDirectory, $"{inputFileNameWithoutExt}_temp_voice_{Guid.NewGuid():N}.wav");
                    try
                    {
                        progress.Report(new PipelineProgress(25, "本編音声の処理中（VST・無音カット確定）...", true));

                        // 48kHz ステレオの PCM 16-bit で一時書き出し
                        ISampleProvider stereoVoiceProvider = currentProvider.WaveFormat.Channels == 1
                            ? new NAudio.Wave.SampleProviders.MonoToStereoSampleProvider(currentProvider)
                            : currentProvider;

                        if (stereoVoiceProvider.WaveFormat.SampleRate != 48000)
                        {
                            stereoVoiceProvider = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(stereoVoiceProvider, 48000);
                        }

                        var tempPcmProvider = stereoVoiceProvider.ToWaveProvider16();
                        using (var wavWriter = new WaveFileWriter(tempVoiceWav, tempPcmProvider.WaveFormat))
                        {
                            byte[] tempBuf = new byte[32768];
                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                int read = tempPcmProvider.Read(tempBuf.AsSpan());
                                if (read == 0) break;
                                wavWriter.Write(tempBuf, 0, read);

                                double ratio = reader.Length > 0 ? Math.Clamp((double)reader.Position / reader.Length, 0.0, 1.0) : 0.0;
                                double pct = 25.0 + (ratio * 30.0); // 25% 〜 55%
                                progress.Report(new PipelineProgress(pct, $"本編音声処理中... ({pct:F0}%)", false));
                            }
                        }

                        // パス2: BGM / エンディング曲をタイムライン合成
                        progress.Report(new PipelineProgress(55, "BGM・エンディング曲をミキシング中...", true));

                        using var voiceReader = new AudioFileReader(tempVoiceWav);
                        long mainVoiceFrames = voiceReader.Length / voiceReader.WaveFormat.BlockAlign;
                        double mainVoiceDuration = (double)mainVoiceFrames / voiceReader.WaveFormat.SampleRate;

                        progress.Report(new PipelineProgress(58, $"確定した本編再生時間: {TimeSpan.FromSeconds(mainVoiceDuration):mm\\:ss}", true));

                        MixingTrack? bgmTrack = null;
                        AudioFileReader? bgmReader = null;
                        if (hasBgm)
                        {
                            progress.Report(new PipelineProgress(60, $"BGM適用: {Path.GetFileName(settings.BgmFilePath)} (音量: {settings.BgmVolume * 100:F0}%)", true));
                            bgmReader = new AudioFileReader(settings.BgmFilePath!);
                            ISampleProvider bgmProvider = bgmReader;
                            if (bgmProvider.WaveFormat.Channels == 1)
                                bgmProvider = new NAudio.Wave.SampleProviders.MonoToStereoSampleProvider(bgmProvider);
                            if (bgmProvider.WaveFormat.SampleRate != 48000)
                                bgmProvider = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(bgmProvider, 48000);

                            // BGMフェードアウト設定: 本編終了の10秒前から5秒間でフェードアウト（5秒前でゲイン0）
                            long fadeOutStart = Math.Max(0, mainVoiceFrames - (48000 * 10));
                            long fadeOutDuration = 48000 * 5; // 5秒間

                            bgmTrack = new MixingTrack(bgmProvider)
                            {
                                Volume = (float)settings.BgmVolume,
                                StartFrame = 0,
                                StopFrame = fadeOutStart + fadeOutDuration,
                                FadeOutStartFrame = fadeOutStart,
                                FadeOutDurationFrames = fadeOutDuration,
                                Loop = true,
                                OnLoopReset = () => { bgmReader.Position = 0; }
                            };
                        }

                        MixingTrack? endingTrack = null;
                        AudioFileReader? endingReader = null;
                        long totalMixFrames = mainVoiceFrames;

                        if (hasEnding)
                        {
                            progress.Report(new PipelineProgress(62, $"エンディング曲適用: {Path.GetFileName(settings.EndingFilePath)} (音量: {settings.EndingVolume * 100:F0}%)", true));
                            endingReader = new AudioFileReader(settings.EndingFilePath!);
                            ISampleProvider edProvider = endingReader;
                            if (edProvider.WaveFormat.Channels == 1)
                                edProvider = new NAudio.Wave.SampleProviders.MonoToStereoSampleProvider(edProvider);
                            if (edProvider.WaveFormat.SampleRate != 48000)
                                edProvider = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(edProvider, 48000);

                            // エンディング曲設定: 本編終了の5秒前から5秒間フェードイン（本編終了地点で100%に到達）
                            long fadeInStart = Math.Max(0, mainVoiceFrames - (48000 * 5));
                            long fadeInDuration = 48000 * 5; // 5秒間

                            // エンディング曲は最後まで完全再生（フェードアウト不要）
                            long endingTotalFrames = (long)(endingReader.TotalTime.TotalSeconds * 48000);
                            long stopFrame = fadeInStart + endingTotalFrames;
                            totalMixFrames = Math.Max(totalMixFrames, stopFrame);

                            progress.Report(new PipelineProgress(62, $"エンディング曲適用: {Path.GetFileName(settings.EndingFilePath)} (長さ: {endingReader.TotalTime:mm\\:ss}, 音量: {settings.EndingVolume * 100:F0}%, 最後まで再生)", true));

                            endingTrack = new MixingTrack(edProvider)
                            {
                                Volume = (float)settings.EndingVolume,
                                StartFrame = fadeInStart,
                                StopFrame = stopFrame,
                                FadeInStartFrame = fadeInStart,
                                FadeInDurationFrames = fadeInDuration,
                                FadeOutStartFrame = null, // フェードアウト不要
                                FadeOutDurationFrames = 0
                            };
                        }

                        // タイムラインミキサー生成
                        var mixer = new TimelineMixingSampleProvider(
                            voiceReader,
                            mainVoiceFrames,
                            bgmTrack,
                            endingTrack,
                            totalMixFrames);

                        // MP3エンコード
                        progress.Report(new PipelineProgress(65, $"最終MP3書き出し: {settings.Mp3Bitrate} kbps -> {Path.GetFileName(outputFilePath)}", true));
                        var mixedPcm = mixer.ToWaveProvider16();

                        using (var mp3Writer = new LameMP3FileWriter(outputFilePath, mixedPcm.WaveFormat, settings.Mp3Bitrate))
                        {
                            byte[] buffer = new byte[32768];
                            long framesWritten = 0;
                            DateTime lastReportTime = DateTime.UtcNow;

                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                int bytesRead = mixedPcm.Read(buffer.AsSpan());
                                if (bytesRead == 0) break;

                                mp3Writer.Write(buffer, 0, bytesRead);
                                framesWritten += bytesRead / mixedPcm.WaveFormat.BlockAlign;

                                double ratio = totalMixFrames > 0 ? Math.Clamp((double)framesWritten / totalMixFrames, 0.0, 1.0) : 0.0;
                                double pct = 65.0 + (ratio * 30.0); // 65% 〜 95%

                                if ((DateTime.UtcNow - lastReportTime).TotalMilliseconds >= 250)
                                {
                                    progress.Report(new PipelineProgress(pct, $"ミックス・MP3エンコード中... ({pct:F0}%)", false));
                                    lastReportTime = DateTime.UtcNow;
                                }
                            }
                        }

                        bgmReader?.Dispose();
                        endingReader?.Dispose();
                    }
                    finally
                    {
                        if (File.Exists(tempVoiceWav))
                        {
                            try { File.Delete(tempVoiceWav); } catch { }
                        }
                    }
                }
                else
                {
                    // 【1パス直接エンコード方式（BGM・EDなしの標準処理）】
                    progress.Report(new PipelineProgress(25, $"MP3エンコード準備: {settings.Mp3Bitrate} kbps -> {Path.GetFileName(outputFilePath)}", true));

                    var pcmProvider = currentProvider.ToWaveProvider16();
                    using (var writer = new LameMP3FileWriter(outputFilePath, pcmProvider.WaveFormat, settings.Mp3Bitrate))
                    {
                        byte[] buffer = new byte[32768];
                        DateTime lastReportTime = DateTime.UtcNow;
                        int lastReportedMilestone = 25;

                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            int bytesRead = pcmProvider.Read(buffer.AsSpan());
                            if (bytesRead == 0) break;

                            writer.Write(buffer, 0, bytesRead);

                            double ratio = reader.Length > 0 ? Math.Clamp((double)reader.Position / reader.Length, 0.0, 1.0) : 0.0;
                            double percent = 25.0 + (ratio * 70.0);

                            if ((DateTime.UtcNow - lastReportTime).TotalMilliseconds >= 250)
                            {
                                progress.Report(new PipelineProgress(percent, $"エンコード中... ({percent:F0}%)", false));
                                lastReportTime = DateTime.UtcNow;
                            }

                            int milestone = ((int)percent / 25) * 25;
                            if (milestone > lastReportedMilestone && milestone < 95)
                            {
                                progress.Report(new PipelineProgress(percent, $"音声処理・エンコード進行中: {milestone}% 完了", true));
                                lastReportedMilestone = milestone;
                            }
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

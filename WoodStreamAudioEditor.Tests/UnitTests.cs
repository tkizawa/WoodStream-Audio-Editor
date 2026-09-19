using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Lame;
using NAudio.Wave;
using WoodStreamAudioEditor.Models;
using WoodStreamAudioEditor.Services;
using WoodStreamAudioEditor.Services.Audio;
using WoodStreamAudioEditor.Services.Metadata;

namespace WoodStreamAudioEditor.Tests;

[TestClass]
public class UnitTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WoodStreamTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// 設定の保存と読み込み、および日本語UTF-8がエスケープされずに可視テキストとして保存されるかの検証
    /// </summary>
    [TestMethod]
    public void TestSettingsSaveAndLoad_UnescapedUtf8()
    {
        var service = new SettingsService();
        var testSettings = new AppSettings
        {
            DefaultArtist = "木澤 朋和（テスト）",
            DefaultAlbum = "WoodStreamのデジタル生活（テスト）",
            DefaultTitle = "第999回 マイクロソフトの最新技術",
            Mp3Bitrate = 256,
            SilenceThresholdDb = -42.5,
            MinSilenceDurationMs = 350
        };

        service.Save(testSettings);

        var loaded = service.Load();
        Assert.AreEqual(testSettings.DefaultArtist, loaded.DefaultArtist);
        Assert.AreEqual(testSettings.DefaultAlbum, loaded.DefaultAlbum);
        Assert.AreEqual(testSettings.DefaultTitle, loaded.DefaultTitle);
        Assert.AreEqual(256, loaded.Mp3Bitrate);
        Assert.AreEqual(-42.5, loaded.SilenceThresholdDb);
        Assert.AreEqual(350, loaded.MinSilenceDurationMs);

        // ファイル内容を直接読み取って Unicode エスケープ (\uXXXX) されていないことを検証
        string appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WoodStream Audio Editor", "settings.json");

        Assert.IsTrue(File.Exists(appDataPath));
        string jsonText = File.ReadAllText(appDataPath, Encoding.UTF8);

        Assert.IsTrue(jsonText.Contains("木澤 朋和"), "日本語テキストが直接可視テキストとして含まれていること");
        Assert.IsTrue(jsonText.Contains("WoodStreamのデジタル生活"), "番組名が直接可視テキストとして含まれていること");
        Assert.IsFalse(jsonText.Contains(@"\u6728"), @"\uXXXX 形式のエスケープ文字が含まれていないこと");
    }

    /// <summary>
    /// 無音自動削除 (SilenceTruncationSampleProvider) の検証
    /// 1秒の音 + 3秒の無音 + 1秒の音 を入力し、400msを超える無音部分がカットされることを検証
    /// </summary>
    [TestMethod]
    public void TestSilenceTruncationSampleProvider()
    {
        int sampleRate = 44100;
        int channels = 1;
        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

        // 1秒の有音 (440Hz, 振幅 0.5) + 3秒の完全無音 (0.0) + 1秒の有音 (440Hz, 振幅 0.5)
        // 合計 5秒 = 220,500 サンプル
        float[] sourceSamples = new float[sampleRate * 5];

        for (int i = 0; i < sampleRate * 1; i++) // 0〜1秒: 有音
        {
            sourceSamples[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / sampleRate));
        }
        // 1〜4秒: 無音 (0.0)
        for (int i = sampleRate * 4; i < sampleRate * 5; i++) // 4〜5秒: 有音
        {
            sourceSamples[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / sampleRate));
        }

        var sourceProvider = new TestArraySampleProvider(waveFormat, sourceSamples);

        // 閾値 -40dB, 最小無音時間 400ms (0.4秒)
        // 期待される出力時間: 約 1秒 + 0.4秒 + 1秒 = 約 2.4秒 (元の5秒から大幅に短縮)
        var truncator = new SilenceTruncationSampleProvider(sourceProvider, -40.0, 400);

        var outputBuffer = new float[sourceSamples.Length];
        int totalRead = 0;
        float[] chunk = new float[1024];

        while (true)
        {
            int read = truncator.Read(chunk.AsSpan());
            if (read == 0) break;

            Array.Copy(chunk, 0, outputBuffer, totalRead, read);
            totalRead += read;
        }

        double originalSeconds = (double)sourceSamples.Length / sampleRate;
        double outputSeconds = (double)totalRead / sampleRate;

        Assert.IsTrue(outputSeconds < 3.0, $"カット後の長さ({outputSeconds:F2}秒)が3秒未満に短縮されていること (元: {originalSeconds:F2}秒)");
        Assert.IsTrue(outputSeconds >= 2.0, $"有音部分(計2秒)が保持されていること (実際: {outputSeconds:F2}秒)");
        Assert.IsTrue(truncator.TruncatedDurationSeconds > 1.5, $"カットされた時間が1.5秒以上であること (実際: {truncator.TruncatedDurationSeconds:F2}秒)");
    }

    /// <summary>
    /// ID3タグメタデータ (MetadataService) の埋め込み検証
    /// </summary>
    [TestMethod]
    public async Task TestMetadataServiceTagging()
    {
        // テスト用ダミーMP3ファイルの作成
        string testMp3Path = Path.Combine(_tempDir, "test_podcast.mp3");
        var format = new WaveFormat(44100, 16, 2);
        using (var writer = new LameMP3FileWriter(testMp3Path, format, 128))
        {
            // 0.5秒分の無音PCMデータを書き込んで有効なMP3を作成
            byte[] silence = new byte[44100 * 2 * 2 / 2];
            writer.Write(silence, 0, silence.Length);
        }

        // テスト用アートワーク画像の作成 (100x100 PNG)
        string testArtworkPath = Path.Combine(_tempDir, "cover.png");
        byte[] minimalPng = CreateMinimalPng();
        await File.WriteAllBytesAsync(testArtworkPath, minimalPng);

        // メタデータ適用
        var service = new MetadataService();
        var metadata = new AudioMetadata(
            Title: "第100回 記念特番",
            Artist: "木澤 朋和",
            Album: "WoodStreamのデジタル生活",
            TrackNumber: "100",
            ArtworkPath: testArtworkPath);

        await service.ApplyMetadataAsync(testMp3Path, metadata);

        // TagLib# で読み戻して検証
        using var taggedFile = TagLib.File.Create(testMp3Path);
        Assert.AreEqual("第100回 記念特番", taggedFile.Tag.Title);
        Assert.AreEqual("木澤 朋和", taggedFile.Tag.FirstPerformer);
        Assert.AreEqual("WoodStreamのデジタル生活", taggedFile.Tag.Album);
        Assert.AreEqual(100u, taggedFile.Tag.Track);
        Assert.IsTrue(taggedFile.Tag.Pictures.Length > 0, "アートワーク画像が埋め込まれていること");
    }

    /// <summary>
    /// パイプライン全体 (AudioPipelineService + MetadataService) のエンドツーエンド統合テスト
    /// WAVファイル入力から無音カット処理、MP3エンコード、ID3タグ埋め込みまで一連の流れを検証
    /// </summary>
    [TestMethod]
    public async Task TestEndToEndAudioPipeline()
    {
        int sampleRate = 44100;
        int channels = 2;
        string inputWav = Path.Combine(_tempDir, "podcast_input.wav");

        // テスト用WAVファイルを生成 (2秒音声 + 2秒無音 + 2秒音声 = 計6秒)
        var format = new WaveFormat(sampleRate, 16, channels);
        using (var writer = new WaveFileWriter(inputWav, format))
        {
            byte[] buffer = new byte[format.AverageBytesPerSecond * 6];
            // 0〜2秒: 440Hz音
            int tone1Samples = sampleRate * 2;
            for (int i = 0; i < tone1Samples; i++)
            {
                short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 16000);
                byte b1 = (byte)(sample & 0xFF);
                byte b2 = (byte)((sample >> 8) & 0xFF);
                int idx = i * 4;
                buffer[idx] = b1;
                buffer[idx + 1] = b2;
                buffer[idx + 2] = b1;
                buffer[idx + 3] = b2;
            }
            // 4〜6秒: 880Hz音
            int tone2Samples = sampleRate * 2;
            for (int i = 0; i < tone2Samples; i++)
            {
                short sample = (short)(Math.Sin(2 * Math.PI * 880 * i / sampleRate) * 16000);
                byte b1 = (byte)(sample & 0xFF);
                byte b2 = (byte)((sample >> 8) & 0xFF);
                int idx = (sampleRate * 4 + i) * 4;
                buffer[idx] = b1;
                buffer[idx + 1] = b2;
                buffer[idx + 2] = b1;
                buffer[idx + 3] = b2;
            }
            writer.Write(buffer, 0, buffer.Length);
        }

        var pipelineService = new AudioPipelineService();
        var metadataService = new MetadataService();

        var settings = new AppSettings
        {
            EnableDeClick = false, // テスト環境ではVST dll未指定のためOFF
            EnableVoiceDeNoise = false,
            EnableSilenceTruncation = true,
            SilenceThresholdDb = -45.0,
            MinSilenceDurationMs = 300,
            Mp3Bitrate = 192
        };

        var progressLogs = new System.Collections.Generic.List<string>();
        var progress = new Progress<PipelineProgress>(p => progressLogs.Add(p.Message));

        // 1. パイプライン実行
        string outputMp3 = await pipelineService.ProcessAudioAsync(
            inputWav,
            _tempDir,
            settings,
            progress,
            System.Threading.CancellationToken.None);

        Assert.IsTrue(File.Exists(outputMp3), "出力MP3ファイルが生成されていること");

        // 2. メタデータ埋め込み
        var metadata = new AudioMetadata(
            Title: "統合テスト エピソード",
            Artist: "木澤 朋和",
            Album: "WoodStreamのデジタル生活",
            TrackNumber: "42",
            ArtworkPath: string.Empty);

        await metadataService.ApplyMetadataAsync(outputMp3, metadata);

        // 3. 出力ファイル検証
        using var mp3File = TagLib.File.Create(outputMp3);
        Assert.AreEqual("統合テスト エピソード", mp3File.Tag.Title);
        Assert.AreEqual("木澤 朋和", mp3File.Tag.FirstPerformer);
        Assert.AreEqual("WoodStreamのデジタル生活", mp3File.Tag.Album);
        Assert.AreEqual(42u, mp3File.Tag.Track);

        // 再生時間の検証 (元6秒から2秒無音が約0.3秒に短縮され、約4.3秒になっていること)
        Assert.IsTrue(mp3File.Properties.Duration.TotalSeconds < 5.5, 
            $"再生時間({mp3File.Properties.Duration.TotalSeconds:F2}秒)が無音カットにより短縮されていること");
    }

    /// <summary>
    /// 96kHz (96,000Hz) ハイレゾ音声ファイルが自動的に 48kHz にリサンプリングされ、
    /// LAME MP3エンコードで Unsupported Sample Rate エラーにならず正常に書き出せるかの検証
    /// </summary>
    [TestMethod]
    public async Task Test96kHzAudioPipelineResampling()
    {
        int sampleRate = 96000; // 96kHz ハイレゾ音源
        int channels = 2;
        string inputWav = Path.Combine(_tempDir, "highres_96khz_input.wav");

        var format = new WaveFormat(sampleRate, 16, channels);
        using (var writer = new WaveFileWriter(inputWav, format))
        {
            byte[] buffer = new byte[format.AverageBytesPerSecond * 2]; // 2秒
            for (int i = 0; i < sampleRate * 2; i++)
            {
                short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 16000);
                byte b1 = (byte)(sample & 0xFF);
                byte b2 = (byte)((sample >> 8) & 0xFF);
                int idx = i * 4;
                buffer[idx] = b1;
                buffer[idx + 1] = b2;
                buffer[idx + 2] = b1;
                buffer[idx + 3] = b2;
            }
            writer.Write(buffer, 0, buffer.Length);
        }

        var pipelineService = new AudioPipelineService();
        var settings = new AppSettings
        {
            EnableDeClick = false,
            EnableVoiceDeNoise = false,
            EnableSilenceTruncation = false,
            Mp3Bitrate = 192
        };

        var logs = new System.Collections.Generic.List<string>();
        var progress = new Progress<PipelineProgress>(p => logs.Add(p.Message));

        // 96kHz 音源の処理実行（エラーが発生せず成功すること）
        string outputMp3 = await pipelineService.ProcessAudioAsync(
            inputWav,
            _tempDir,
            settings,
            progress,
            System.Threading.CancellationToken.None);

        Assert.IsTrue(File.Exists(outputMp3), "96kHz音源からMP3が正常に出力されていること");

        // MP3のサンプリングレートが 48kHz になっていることを検証
        using var mp3 = new Mp3FileReader(outputMp3);
        Assert.AreEqual(48000, mp3.WaveFormat.SampleRate, "MP3のサンプリングレートが48kHzにリサンプリングされていること");
    }

    private static byte[] CreateMinimalPng()
    {
        // 1x1 RGBA PNG バイナリ
        return new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
            0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
            0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
            0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
        };
    }

    /// <summary>
    /// メモリ配列からサンプルを提供するヘルパークラス
    /// </summary>
    private class TestArraySampleProvider : ISampleProvider
    {
        private readonly float[] _samples;
        private int _position = 0;

        public WaveFormat WaveFormat { get; }

        public TestArraySampleProvider(WaveFormat waveFormat, float[] samples)
        {
            WaveFormat = waveFormat;
            _samples = samples;
        }

        public int Read(Span<float> buffer)
        {
            int available = _samples.Length - _position;
            int toCopy = Math.Min(buffer.Length, available);
            if (toCopy <= 0) return 0;

            _samples.AsSpan(_position, toCopy).CopyTo(buffer);
            _position += toCopy;
            return toCopy;
        }
    }
}

using System.Text.Json.Serialization;

namespace WoodStreamAudioEditor.Models;

/// <summary>
/// アプリケーションの設定情報モデル
/// ウィンドウの位置・サイズ、音声処理パラメータ、VSTプラグインパス、ID3タグ情報、言語設定を保持します。
/// </summary>
public class AppSettings
{
    // ウィンドウ状態
    public double? WindowLeft { get; set; } = null;
    public double? WindowTop { get; set; } = null;
    public double WindowWidth { get; set; } = 960;
    public double WindowHeight { get; set; } = 780;
    public bool IsMaximized { get; set; } = false;

    // 言語設定 ("" または "Auto", "ja-JP", "en-US")
    public string Language { get; set; } = "Auto";

    // ファイル入出力履歴
    public string LastInputFilePath { get; set; } = string.Empty;
    public string OutputDirectoryPath { get; set; } = string.Empty;

    // VSTプラグイン設定
    public string DeClickPluginPath { get; set; } = string.Empty;
    public bool EnableDeClick { get; set; } = true;
    public string VoiceDeNoisePluginPath { get; set; } = string.Empty;
    public bool EnableVoiceDeNoise { get; set; } = true;

    // トリミング（先頭・末尾カット）設定
    public bool EnableTrim { get; set; } = false;
    public double TrimStartSeconds { get; set; } = 0.0; // 秒
    public double TrimEndSeconds { get; set; } = 0.0;   // 秒

    // 車内向け音声チューニング（イコライザー）設定
    public bool EnableCarAudioEq { get; set; } = true;

    // 無音削除設定
    public bool EnableSilenceTruncation { get; set; } = true;
    public double SilenceThresholdDb { get; set; } = -45.0; // dB
    public int MinSilenceDurationMs { get; set; } = 400;    // ms

    // MP3エンコード設定
    public int Mp3Bitrate { get; set; } = 192; // 128, 192, 256, 320 kbps

    // BGM設定
    public string BgmFilePath { get; set; } = string.Empty;
    public bool EnableBgm { get; set; } = false;
    public double BgmVolume { get; set; } = 0.15; // 0.0 〜 1.0 (デフォルト 15%)

    // エンディング曲設定
    public string EndingFilePath { get; set; } = string.Empty;
    public bool EnableEnding { get; set; } = false;
    public double EndingVolume { get; set; } = 0.80; // 0.0 〜 1.0 (デフォルト 80%)
    public double EndingExtraSeconds { get; set; } = 15.0; // 本編終了後の余韻秒数 (デフォルト 15秒)

    // ID3タグ設定 (固定値保存対応)
    public string DefaultTitle { get; set; } = string.Empty;
    public string DefaultArtist { get; set; } = "木澤 朋和";
    public string DefaultAlbum { get; set; } = "WoodStreamのデジタル生活";
    public string DefaultTrackNumber { get; set; } = "1";
    public string DefaultArtworkPath { get; set; } = string.Empty;
}

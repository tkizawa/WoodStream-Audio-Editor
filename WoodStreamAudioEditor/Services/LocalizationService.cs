using System.ComponentModel;
using System.Globalization;

namespace WoodStreamAudioEditor.Services;

/// <summary>
/// 多言語（日本語・英語）対応サービス
/// Windowsの表示言語設定（CurrentUICulture）に応じて自動初期化し、動的な切り替えもサポートします。
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    public event PropertyChangedEventHandler? PropertyChanged;

    private string _currentCulture = "ja-JP";

    public LocalizationService()
    {
        // システムの現在の表示言語を確認
        var systemLang = CultureInfo.CurrentUICulture.Name;
        if (systemLang.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            _currentCulture = "ja-JP";
        }
        else
        {
            _currentCulture = "en-US";
        }
    }

    public void SetLanguage(string langCode)
    {
        if (string.IsNullOrWhiteSpace(langCode) || langCode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            var systemLang = CultureInfo.CurrentUICulture.Name;
            _currentCulture = systemLang.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "ja-JP" : "en-US";
        }
        else if (langCode.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            _currentCulture = "ja-JP";
        }
        else
        {
            _currentCulture = "en-US";
        }

        var culture = new CultureInfo(_currentCulture);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // 全プロパティの変更通知を発行してUIを更新
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public bool IsJapanese => _currentCulture.StartsWith("ja", StringComparison.OrdinalIgnoreCase);

    // 文言プロパティ一覧
    public string AppTitle => IsJapanese ? "WoodStream Audio Editor - ポッドキャスト音声編集ツール" : "WoodStream Audio Editor - Podcast Audio Processor";
    
    // パネルヘッダー
    public string SectionInputOutput => IsJapanese ? "1. ファイル入出力" : "1. File Input & Output";
    public string SectionVst => IsJapanese ? "2. VSTプラグイン処理 (iZotope RX 8)" : "2. VST Plugin Processing (iZotope RX 8)";
    public string SectionAudioProcessing => IsJapanese ? "3. 音声処理・無音自動削除設定" : "3. Audio Processing & Silence Truncation";
    public string SectionMetadata => IsJapanese ? "4. 番組情報・ID3タグ設定" : "4. Podcast Metadata & ID3 Tags";
    public string SectionActionLog => IsJapanese ? "5. 処理実行・進捗ログ" : "5. Processing & Progress Log";

    // 入出力
    public string InputFile => IsJapanese ? "入力音声ファイル (WAV / MP3):" : "Input Audio File (WAV / MP3):";
    public string OutputDirectory => IsJapanese ? "出力先フォルダ:" : "Output Folder:";
    public string Browse => IsJapanese ? "参照..." : "Browse...";
    public string DragDropHint => IsJapanese ? "ここに音声ファイルをドラッグ＆ドロップできます" : "Drag and drop audio files here";

    // VST
    public string DeClickLabel => IsJapanese ? "iZotope RX 8 De-click プラグイン (.dll):" : "iZotope RX 8 De-click Plugin (.dll):";
    public string VoiceDeNoiseLabel => IsJapanese ? "iZotope RX 8 Voice De-noise プラグイン (.dll):" : "iZotope RX 8 Voice De-noise Plugin (.dll):";
    public string EnableDeClick => IsJapanese ? "De-click を適用する" : "Enable De-click";
    public string EnableVoiceDeNoise => IsJapanese ? "Voice De-noise を適用する" : "Enable Voice De-noise";
    public string AutoDetect => IsJapanese ? "既定パスを自動検出" : "Auto-detect Default Paths";
    public string VstNotice => IsJapanese 
        ? "※ 64bit(x64)の VST 2.4 プラグイン (.dll) を指定してください。" 
        : "* Please specify 64-bit (x64) VST 2.4 plugin (.dll) files.";

    // 音声処理・トリミング
    public string SectionTrim => IsJapanese ? "トリミング設定 (最初・最後のカット)" : "Trimming (Start & End Cut)";
    public string EnableTrim => IsJapanese ? "最初と最後の不要部分をカットする" : "Enable Start & End Cut";
    public string TrimStart => IsJapanese ? "先頭カット時間 (秒):" : "Trim Start (sec):";
    public string TrimStartHint => IsJapanese ? "（録音開始時の準備音や間をカット）" : "(Cut seconds from start)";
    public string TrimEnd => IsJapanese ? "末尾カット時間 (秒):" : "Trim End (sec):";
    public string TrimEndHint => IsJapanese ? "（録音終了後の不要な間やノイズをカット）" : "(Cut seconds from end)";
    public string AudioDuration => IsJapanese ? "元の音声の長さ:" : "Audio Duration:";
    public string EstimatedDuration => IsJapanese ? "カット後の予想の長さ:" : "Est. Duration:";

    public string EnableSilenceTruncation => IsJapanese ? "無音部分の自動カット (Silence Truncation) を有効にする" : "Enable Silence Truncation";
    public string SilenceThreshold => IsJapanese ? "無音判定音量閾値 (dB):" : "Silence Volume Threshold (dB):";
    public string SilenceThresholdHint => IsJapanese ? "（推奨: -45dB 〜 -40dB。環境音と音声の境目）" : "(Recommended: -45dB to -40dB)";
    public string MinSilenceDuration => IsJapanese ? "保持する最小無音時間 (ミリ秒):" : "Retained Minimum Silence (ms):";
    public string MinSilenceDurationHint => IsJapanese ? "（推奨: 300ms 〜 500ms。カット後の自然な間合いを確保）" : "(Recommended: 300ms to 500ms for natural pauses)";
    public string Mp3Bitrate => IsJapanese ? "出力MP3ビットレート:" : "Output MP3 Bitrate:";

    // メタデータ
    public string TagTitle => IsJapanese ? "タイトル (エピソード名):" : "Episode Title:";
    public string TagArtist => IsJapanese ? "アーティスト名:" : "Artist / Host:";
    public string TagAlbum => IsJapanese ? "アルバム名 (番組名):" : "Album / Show Name:";
    public string TagTrack => IsJapanese ? "トラック番号 (回数):" : "Track Number (Episode #):";
    public string TagArtwork => IsJapanese ? "アートワーク画像 (JPEG/PNG):" : "Artwork Image (JPEG/PNG):";
    public string SelectImage => IsJapanese ? "画像選択..." : "Select Image...";
    public string ClearImage => IsJapanese ? "クリア" : "Clear";

    // アクション・ログ
    public string StartProcessing => IsJapanese ? "音声処理・書き出しを開始" : "Start Processing & Export";
    public string Cancel => IsJapanese ? "処理を中止" : "Cancel";
    public string ClearLog => IsJapanese ? "ログ消去" : "Clear Log";
    public string ReadyStatus => IsJapanese ? "待機中" : "Ready";
    public string ProcessingStatus => IsJapanese ? "処理中..." : "Processing...";
    public string SuccessStatus => IsJapanese ? "完了しました！" : "Completed successfully!";
    public string FailedStatus => IsJapanese ? "エラーが発生しました" : "Error occurred";

    // 設定
    public string SettingsHeader => IsJapanese ? "設定" : "Settings";
    public string SaveDefaults => IsJapanese ? "現在の入力値をデフォルト設定として保存" : "Save Current Values as Defaults";
    public string LanguageLabel => IsJapanese ? "言語 / Language:" : "Language / 言語:";
}

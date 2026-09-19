using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NAudio.Wave;
using WoodStreamAudioEditor.Models;
using WoodStreamAudioEditor.Services;
using WoodStreamAudioEditor.Services.Audio;
using WoodStreamAudioEditor.Services.Metadata;

namespace WoodStreamAudioEditor.ViewModels;

/// <summary>
/// メインウィンドウ用 ViewModel
/// CommunityToolkit.Mvvm を使用した MVVM パターン実装
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly AudioPipelineService _audioPipelineService;
    private readonly MetadataService _metadataService;
    private CancellationTokenSource? _cts;

    public LocalizationService Strings => LocalizationService.Instance;

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _audioPipelineService = new AudioPipelineService();
        _metadataService = new MetadataService();

        // ビットレート選択肢
        AvailableBitrates = new ObservableCollection<int> { 128, 192, 256, 320 };

        // 言語設定の初期化
        LocalizationService.Instance.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(Strings));
            OnPropertyChanged(nameof(StatusText));
        };

        // 設定の読み込み
        LoadSettings();
    }

    // ==========================================
    // 1. ファイル入出力プロパティ
    // ==========================================
    [ObservableProperty]
    private string _inputFilePath = string.Empty;

    [ObservableProperty]
    private string _outputDirectoryPath = string.Empty;

    // ==========================================
    // 2. VST プラグイン設定
    // ==========================================
    [ObservableProperty]
    private string _deClickPluginPath = string.Empty;

    [ObservableProperty]
    private bool _enableDeClick = true;

    [ObservableProperty]
    private string _voiceDeNoisePluginPath = string.Empty;

    [ObservableProperty]
    private bool _enableVoiceDeNoise = true;

    // ==========================================
    // 3. 音声処理・トリミング・無音自動カット設定
    // ==========================================
    [ObservableProperty]
    private bool _enableTrim = false;

    [ObservableProperty]
    private double _trimStartSeconds = 0.0;

    [ObservableProperty]
    private double _trimEndSeconds = 0.0;

    [ObservableProperty]
    private double _audioTotalSeconds = 0.0;

    [ObservableProperty]
    private string _audioDurationDisplay = string.Empty;

    [ObservableProperty]
    private string _estimatedDurationDisplay = string.Empty;

    partial void OnEnableTrimChanged(bool value) => UpdateEstimatedDuration();
    partial void OnTrimStartSecondsChanged(double value) => UpdateEstimatedDuration();
    partial void OnTrimEndSecondsChanged(double value) => UpdateEstimatedDuration();

    private void UpdateEstimatedDuration()
    {
        if (AudioTotalSeconds <= 0)
        {
            EstimatedDurationDisplay = string.Empty;
            return;
        }

        double cut = EnableTrim ? (TrimStartSeconds + TrimEndSeconds) : 0;
        double remaining = Math.Max(0, AudioTotalSeconds - cut);
        EstimatedDurationDisplay = $"{TimeSpan.FromSeconds(remaining):mm\\:ss} ({remaining:F1}s)";
    }

    [ObservableProperty]
    private bool _enableSilenceTruncation = true;

    [ObservableProperty]
    private double _silenceThresholdDb = -45.0;

    [ObservableProperty]
    private int _minSilenceDurationMs = 400;

    [ObservableProperty]
    private int _selectedBitrate = 192;

    public ObservableCollection<int> AvailableBitrates { get; }

    // ==========================================
    // 4. ID3 タグ情報
    // ==========================================
    [ObservableProperty]
    private string _tagTitle = string.Empty;

    [ObservableProperty]
    private string _tagArtist = "木澤 朋和";

    [ObservableProperty]
    private string _tagAlbum = "WoodStreamのデジタル生活";

    [ObservableProperty]
    private string _tagTrackNumber = "1";

    [ObservableProperty]
    private string _tagArtworkPath = string.Empty;

    // ==========================================
    // 5. 処理状態・ログ
    // ==========================================
    [ObservableProperty]
    private bool _isProcessing = false;

    [ObservableProperty]
    private double _progressPercentage = 0;

    [ObservableProperty]
    private string _statusText = LocalizationService.Instance.ReadyStatus;

    [ObservableProperty]
    private string _logOutput = string.Empty;

    private readonly StringBuilder _logBuilder = new();

    // ==========================================
    // 言語設定
    // ==========================================
    [ObservableProperty]
    private string _selectedLanguage = "Auto";

    partial void OnSelectedLanguageChanged(string value)
    {
        LocalizationService.Instance.SetLanguage(value);
    }

    // ==========================================
    // コマンド群
    // ==========================================

    [RelayCommand]
    private void BrowseInputFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = Strings.IsJapanese 
                ? "音声ファイル (*.wav;*.mp3)|*.wav;*.mp3|すべてのファイル (*.*)|*.*"
                : "Audio Files (*.wav;*.mp3)|*.wav;*.mp3|All Files (*.*)|*.*",
            Title = Strings.InputFile
        };

        if (dlg.ShowDialog() == true)
        {
            SetInputFile(dlg.FileName);
        }
    }

    public void SetInputFile(string filePath)
    {
        InputFilePath = filePath;
        AppendLog($"[入力ファイル設定] {filePath}");

        // 出力先が未設定の場合、入力ファイルと同じフォルダを設定
        if (string.IsNullOrWhiteSpace(OutputDirectoryPath) || !Directory.Exists(OutputDirectoryPath))
        {
            OutputDirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;
        }

        // タイトルが未入力の場合、ファイル名を仮タイトルとして補完
        if (string.IsNullOrWhiteSpace(TagTitle))
        {
            TagTitle = Path.GetFileNameWithoutExtension(filePath);
        }

        // 音声ファイル長を取得
        try
        {
            if (File.Exists(filePath))
            {
                using var reader = new AudioFileReader(filePath);
                AudioTotalSeconds = reader.TotalTime.TotalSeconds;
                AudioDurationDisplay = $"{reader.TotalTime:mm\\:ss} ({AudioTotalSeconds:F1}s)";
                UpdateEstimatedDuration();
            }
        }
        catch
        {
            AudioTotalSeconds = 0;
            AudioDurationDisplay = string.Empty;
            EstimatedDurationDisplay = string.Empty;
        }
    }

    [RelayCommand]
    private void BrowseOutputDirectory()
    {
        var dlg = new OpenFolderDialog
        {
            Title = Strings.OutputDirectory
        };

        if (dlg.ShowDialog() == true)
        {
            OutputDirectoryPath = dlg.FolderName;
            AppendLog($"[出力先フォルダ設定] {OutputDirectoryPath}");
        }
    }

    [RelayCommand]
    private void BrowseDeClickPlugin()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "VST2 Plugin (*.dll)|*.dll|All Files (*.*)|*.*",
            Title = Strings.DeClickLabel
        };

        if (dlg.ShowDialog() == true)
        {
            DeClickPluginPath = dlg.FileName;
            AppendLog($"[De-click プラグイン設定] {dlg.FileName}");
        }
    }

    [RelayCommand]
    private void BrowseVoiceDeNoisePlugin()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "VST2 Plugin (*.dll)|*.dll|All Files (*.*)|*.*",
            Title = Strings.VoiceDeNoiseLabel
        };

        if (dlg.ShowDialog() == true)
        {
            VoiceDeNoisePluginPath = dlg.FileName;
            AppendLog($"[Voice De-noise プラグイン設定] {dlg.FileName}");
        }
    }

    [RelayCommand]
    private void AutoDetectPlugins()
    {
        AppendLog("[自動検出] iZotope RX 8 プラグインをスキャンしています...");

        string[] commonDirs =
        {
            @"C:\Program Files\Steinberg\VstPlugins",
            @"C:\Program Files\VstPlugins\iZotope RX 8 Elements",
            @"C:\Program Files\Steinberg\VstPlugins\iZotope RX 8 Elements",
            @"C:\Program Files\VstPlugins",
            @"C:\Program Files\Common Files\VST2"
        };

        bool foundDeClick = false;
        bool foundDeNoise = false;

        foreach (var dir in commonDirs)
        {
            if (!Directory.Exists(dir)) continue;

            if (!foundDeClick)
            {
                // iZRX8De-click.dll はエンジンDLLのため除外、正規の 'RX 8 De-click.dll' を優先
                var deClickFiles = Directory.GetFiles(dir, "*De-click*.dll", SearchOption.AllDirectories);
                var validDeClick = deClickFiles
                    .Where(f => !Path.GetFileName(f).StartsWith("iZ", StringComparison.OrdinalIgnoreCase))
                    .Concat(deClickFiles)
                    .FirstOrDefault();

                if (validDeClick != null)
                {
                    DeClickPluginPath = validDeClick;
                    foundDeClick = true;
                    AppendLog($"[自動検出] De-click を発見: {DeClickPluginPath}");
                }
            }

            if (!foundDeNoise)
            {
                var deNoiseFiles = Directory.GetFiles(dir, "*Voice De-noise*.dll", SearchOption.AllDirectories);
                var validDeNoise = deNoiseFiles
                    .Where(f => !Path.GetFileName(f).StartsWith("iZ", StringComparison.OrdinalIgnoreCase))
                    .Concat(deNoiseFiles)
                    .FirstOrDefault();

                if (validDeNoise != null)
                {
                    VoiceDeNoisePluginPath = validDeNoise;
                    foundDeNoise = true;
                    AppendLog($"[自動検出] Voice De-noise を発見: {VoiceDeNoisePluginPath}");
                }
            }
        }

        if (!foundDeClick && !foundDeNoise)
        {
            AppendLog("[自動検出] 既定フォルダに RX 8 プラグインが見つかりませんでした。「参照...」から手動で指定してください。");
        }
    }

    [RelayCommand]
    private void BrowseArtwork()
    {
        var dlg = new OpenFileDialog
        {
            Filter = Strings.IsJapanese 
                ? "画像ファイル (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|すべてのファイル (*.*)|*.*"
                : "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All Files (*.*)|*.*",
            Title = Strings.TagArtwork
        };

        if (dlg.ShowDialog() == true)
        {
            TagArtworkPath = dlg.FileName;
            AppendLog($"[アートワーク画像設定] {dlg.FileName}");
        }
    }

    [RelayCommand]
    private void ClearArtwork()
    {
        TagArtworkPath = string.Empty;
        AppendLog("[アートワーク画像] 解除しました。");
    }

    [RelayCommand]
    private void ClearLog()
    {
        _logBuilder.Clear();
        LogOutput = string.Empty;
    }

    [RelayCommand]
    private void SaveDefaults()
    {
        var settings = ToSettings();
        _settingsService.Save(settings);
        AppendLog(Strings.IsJapanese 
            ? "[設定] 現在の入力内容を設定ファイルに保存しました。" 
            : "[Settings] Current settings saved to local config.");
    }

    // ==========================================
    // メイン処理の実行
    // ==========================================
    [RelayCommand]
    private async Task StartProcessingAsync()
    {
        if (string.IsNullOrWhiteSpace(InputFilePath) || !File.Exists(InputFilePath))
        {
            MessageBox.Show(
                Strings.IsJapanese ? "入力音声ファイルを選択してください。" : "Please select an input audio file.",
                Strings.AppTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectoryPath))
        {
            OutputDirectoryPath = Path.GetDirectoryName(InputFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        }

        IsProcessing = true;
        ProgressPercentage = 0;
        StatusText = Strings.ProcessingStatus;
        _cts = new CancellationTokenSource();

        AppendLog("==================================================");
        AppendLog($"[処理開始] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        AppendLog($"[入力] {InputFilePath}");
        AppendLog($"[出力フォルダ] {OutputDirectoryPath}");
        AppendLog("==================================================");

        var progress = new Progress<PipelineProgress>(p =>
        {
            ProgressPercentage = p.Percentage;
            StatusText = p.Message;
            if (p.LogToConsole)
            {
                AppendLog(p.Message);
            }
        });

        try
        {
            var settings = ToSettings();

            // 1. 音声パイプライン処理実行 (VST + 無音カット + MP3変換)
            string generatedMp3Path = await _audioPipelineService.ProcessAudioAsync(
                InputFilePath,
                OutputDirectoryPath,
                settings,
                progress,
                _cts.Token);

            // 2. ID3 メタデータ埋め込み
            ProgressPercentage = 98;
            StatusText = Strings.IsJapanese ? "ID3タグを埋め込み中..." : "Writing ID3 tags...";
            var metadata = new AudioMetadata(
                TagTitle,
                TagArtist,
                TagAlbum,
                TagTrackNumber,
                TagArtworkPath);

            await _metadataService.ApplyMetadataAsync(generatedMp3Path, metadata, AppendLog);

            ProgressPercentage = 100;
            StatusText = Strings.SuccessStatus;
            AppendLog($"[完了] 処理がすべて完了しました！\n出力ファイル: {generatedMp3Path}");

            // 設定を自動保存
            _settingsService.Save(settings);
        }
        catch (OperationCanceledException)
        {
            StatusText = Strings.IsJapanese ? "処理が中止されました。" : "Processing was cancelled.";
            AppendLog("[中断] ユーザーによって処理が中止されました。");
        }
        catch (Exception ex)
        {
            StatusText = Strings.FailedStatus;
            AppendLog($"[ERROR] エラーが発生しました: {ex.Message}\n{ex.StackTrace}");
            MessageBox.Show(
                $"{Strings.FailedStatus}\n\n{ex.Message}",
                Strings.AppTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelProcessing()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            AppendLog("[要求] キャンセルを要求しました...");
        }
    }

    public void AppendLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string line = $"[{timestamp}] {message}";
        _logBuilder.AppendLine(line);
        LogOutput = _logBuilder.ToString();
    }

    // ==========================================
    // 設定の相互変換
    // ==========================================
    public void LoadSettings()
    {
        var settings = _settingsService.Load();

        SelectedLanguage = string.IsNullOrEmpty(settings.Language) ? "Auto" : settings.Language;
        LocalizationService.Instance.SetLanguage(SelectedLanguage);

        InputFilePath = settings.LastInputFilePath;
        OutputDirectoryPath = settings.OutputDirectoryPath;

        DeClickPluginPath = settings.DeClickPluginPath;
        EnableDeClick = settings.EnableDeClick;

        VoiceDeNoisePluginPath = settings.VoiceDeNoisePluginPath;
        EnableVoiceDeNoise = settings.EnableVoiceDeNoise;

        EnableTrim = settings.EnableTrim;
        TrimStartSeconds = settings.TrimStartSeconds;
        TrimEndSeconds = settings.TrimEndSeconds;

        EnableSilenceTruncation = settings.EnableSilenceTruncation;
        SilenceThresholdDb = settings.SilenceThresholdDb;
        MinSilenceDurationMs = settings.MinSilenceDurationMs;

        SelectedBitrate = settings.Mp3Bitrate > 0 ? settings.Mp3Bitrate : 192;

        TagArtist = settings.DefaultArtist;
        TagAlbum = settings.DefaultAlbum;
        TagArtworkPath = settings.DefaultArtworkPath;
        if (!string.IsNullOrWhiteSpace(settings.DefaultTitle))
        {
            TagTitle = settings.DefaultTitle;
        }

        // 以前のバージョンで iZRX8De-click.dll (エンジンDLL) が保存されている場合、正規の VST2 プラグインに補正
        if (!string.IsNullOrWhiteSpace(DeClickPluginPath) && Path.GetFileName(DeClickPluginPath).Equals("iZRX8De-click.dll", StringComparison.OrdinalIgnoreCase))
        {
            string dir = Path.GetDirectoryName(DeClickPluginPath) ?? string.Empty;
            string correctPath = Path.Combine(dir, "RX 8 De-click.dll");
            if (File.Exists(correctPath))
            {
                DeClickPluginPath = correctPath;
            }
        }

        // プラグインパスが空または見つからない場合は自動検出を試みる
        if (string.IsNullOrWhiteSpace(DeClickPluginPath) || !File.Exists(DeClickPluginPath) ||
            string.IsNullOrWhiteSpace(VoiceDeNoisePluginPath) || !File.Exists(VoiceDeNoisePluginPath))
        {
            AutoDetectPlugins();
        }
    }

    public AppSettings ToSettings()
    {
        return new AppSettings
        {
            Language = SelectedLanguage,
            LastInputFilePath = InputFilePath,
            OutputDirectoryPath = OutputDirectoryPath,
            DeClickPluginPath = DeClickPluginPath,
            EnableDeClick = EnableDeClick,
            VoiceDeNoisePluginPath = VoiceDeNoisePluginPath,
            EnableVoiceDeNoise = EnableVoiceDeNoise,
            EnableTrim = EnableTrim,
            TrimStartSeconds = TrimStartSeconds,
            TrimEndSeconds = TrimEndSeconds,
            EnableSilenceTruncation = EnableSilenceTruncation,
            SilenceThresholdDb = SilenceThresholdDb,
            MinSilenceDurationMs = MinSilenceDurationMs,
            Mp3Bitrate = SelectedBitrate,
            DefaultTitle = TagTitle,
            DefaultArtist = TagArtist,
            DefaultAlbum = TagAlbum,
            DefaultArtworkPath = TagArtworkPath
        };
    }

    public void SaveSettingsOnClose(Window window)
    {
        var settings = ToSettings();
        if (window.WindowState == WindowState.Maximized)
        {
            settings.IsMaximized = true;
        }
        else
        {
            settings.IsMaximized = false;
            settings.WindowLeft = window.Left;
            settings.WindowTop = window.Top;
            settings.WindowWidth = window.ActualWidth;
            settings.WindowHeight = window.ActualHeight;
        }
        _settingsService.Save(settings);
    }
}

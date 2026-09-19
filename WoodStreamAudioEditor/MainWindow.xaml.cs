using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WoodStreamAudioEditor.Services;
using WoodStreamAudioEditor.ViewModels;

namespace WoodStreamAudioEditor;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// ウィンドウの位置・サイズ復元および保存、ドラッグ＆ドロップを処理します。
/// </summary>
public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
    }

    /// <summary>
    /// 起動時: ウィンドウハンドル初期化時に保存されたウィンドウ位置・サイズ・状態を復元
    /// </summary>
    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var settings = _settingsService.Load();

            // 位置とサイズが有効か確認
            if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue &&
                !double.IsNaN(settings.WindowLeft.Value) && !double.IsNaN(settings.WindowTop.Value) &&
                settings.WindowWidth >= 400 && settings.WindowHeight >= 300)
            {
                // 仮想スクリーン領域（全モニター領域）と交差しているかを判定
                var virtualScreenRect = new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);

                var windowRect = new Rect(
                    settings.WindowLeft.Value,
                    settings.WindowTop.Value,
                    settings.WindowWidth,
                    settings.WindowHeight);

                if (virtualScreenRect.IntersectsWith(windowRect))
                {
                    Left = settings.WindowLeft.Value;
                    Top = settings.WindowTop.Value;
                    Width = settings.WindowWidth;
                    Height = settings.WindowHeight;
                }
            }

            if (settings.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ウィンドウ位置の復元に失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// 終了時: ウィンドウ位置・サイズを設定ファイルに保存
    /// </summary>
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SaveSettingsOnClose(this);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ウィンドウ位置の保存に失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// ファイルドラッグオーバー処理
    /// </summary>
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    /// <summary>
    /// ファイルドロップ処理 (WAV / MP3 または 画像)
    /// </summary>
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        if (DataContext is not MainViewModel vm) return;

        foreach (var file in files)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".wav" or ".mp3")
            {
                vm.SetInputFile(file);
                break;
            }
            else if (ext is ".jpg" or ".jpeg" or ".png")
            {
                vm.TagArtworkPath = file;
                vm.AppendLog($"[アートワーク画像ドロップ] {file}");
            }
            else if (ext == ".dll")
            {
                // VST プラグインのドロップ対応
                string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                if (name.Contains("de-click") || name.Contains("click"))
                {
                    vm.DeClickPluginPath = file;
                    vm.AppendLog($"[De-click プラグインドロップ] {file}");
                }
                else if (name.Contains("voice") || name.Contains("noise") || name.Contains("denoise"))
                {
                    vm.VoiceDeNoisePluginPath = file;
                    vm.AppendLog($"[Voice De-noise プラグインドロップ] {file}");
                }
            }
        }
    }

    /// <summary>
    /// ログ追加時にテキストボックスを末尾に自動スクロール
    /// </summary>
    private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.ScrollToEnd();
        }
    }
}
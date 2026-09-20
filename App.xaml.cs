using System.Windows;
using WoodStreamAudioEditor.Services;

namespace WoodStreamAudioEditor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Windowsのシステムテーマ（ダークモード / ライトモード）を検知して適用
        ThemeService.Instance.DetectAndApplySystemTheme();
    }
}

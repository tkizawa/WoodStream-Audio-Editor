using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace WoodStreamAudioEditor.Services;

/// <summary>
/// Windowsのテーマ設定（ダークモード / ライトモード）を検知し、
/// アプリケーションのリソース（カラー・ブラシ）を動的に適用するサービス
/// </summary>
public class ThemeService : INotifyPropertyChanged
{
    private static ThemeService? _instance;
    public static ThemeService Instance => _instance ??= new ThemeService();

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isDarkMode = true;
    public bool IsDarkMode
    {
        get => _isDarkMode;
        private set
        {
            if (_isDarkMode != value)
            {
                _isDarkMode = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDarkMode)));
            }
        }
    }

    private ThemeService()
    {
        // 初期のシステムテーマを検出
        DetectAndApplySystemTheme();

        // Windowsのテーマ変更イベントを監視
        SystemEvents.UserPreferenceChanged += (sender, e) =>
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
            {
                Application.Current?.Dispatcher.Invoke(DetectAndApplySystemTheme);
            }
        };
    }

    /// <summary>
    /// Windowsのレジストリからダークモード設定を取得
    /// </summary>
    public static bool CheckIsWindowsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var val = key.GetValue("AppsUseLightTheme");
                if (val is int intVal)
                {
                    // 0 = Dark Mode, 1 = Light Mode
                    return intVal == 0;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"システムテーマの検出に失敗しました: {ex.Message}");
        }

        // デフォルトはダークモード
        return true;
    }

    /// <summary>
    /// 現在のシステムテーマを検出して適用
    /// </summary>
    public void DetectAndApplySystemTheme()
    {
        bool isDark = CheckIsWindowsDarkMode();
        IsDarkMode = isDark;
        ApplyTheme(isDark);
    }

    /// <summary>
    /// アプリケーションリソースのブラシ・カラーを切り替えます
    /// </summary>
    public void ApplyTheme(bool isDark)
    {
        var res = Application.Current?.Resources;
        if (res == null) return;

        if (isDark)
        {
            // ===== ダークモード =====
            SetResource(res, "BgDarkColor", (Color)ColorConverter.ConvertFromString("#12151C"));
            SetResource(res, "CardBgColor", (Color)ColorConverter.ConvertFromString("#1A1F29"));
            SetResource(res, "CardBorderColor", (Color)ColorConverter.ConvertFromString("#2A3241"));
            SetResource(res, "InputBgColor", (Color)ColorConverter.ConvertFromString("#10131A"));
            SetResource(res, "InputBorderColor", (Color)ColorConverter.ConvertFromString("#2E384A"));
            SetResource(res, "TextPrimaryColor", (Color)ColorConverter.ConvertFromString("#F3F4F6"));
            SetResource(res, "TextSecondaryColor", (Color)ColorConverter.ConvertFromString("#9CA3AF"));

            SetResource(res, "PopupBgColor", (Color)ColorConverter.ConvertFromString("#1E2532"));
            SetResource(res, "ItemHoverColor", (Color)ColorConverter.ConvertFromString("#2C3647"));
            SetResource(res, "ConsoleBgColor", (Color)ColorConverter.ConvertFromString("#0C0E14"));
            SetResource(res, "DropAreaBgColor", (Color)ColorConverter.ConvertFromString("#141820"));
        }
        else
        {
            // ===== ライトモード =====
            SetResource(res, "BgDarkColor", (Color)ColorConverter.ConvertFromString("#F1F5F9"));
            SetResource(res, "CardBgColor", (Color)ColorConverter.ConvertFromString("#FFFFFF"));
            SetResource(res, "CardBorderColor", (Color)ColorConverter.ConvertFromString("#CBD5E1"));
            SetResource(res, "InputBgColor", (Color)ColorConverter.ConvertFromString("#FFFFFF"));
            SetResource(res, "InputBorderColor", (Color)ColorConverter.ConvertFromString("#94A3B8"));
            SetResource(res, "TextPrimaryColor", (Color)ColorConverter.ConvertFromString("#0F172A"));
            SetResource(res, "TextSecondaryColor", (Color)ColorConverter.ConvertFromString("#475569"));

            SetResource(res, "PopupBgColor", (Color)ColorConverter.ConvertFromString("#FFFFFF"));
            SetResource(res, "ItemHoverColor", (Color)ColorConverter.ConvertFromString("#E2E8F0"));
            SetResource(res, "ConsoleBgColor", (Color)ColorConverter.ConvertFromString("#F8FAFC"));
            SetResource(res, "DropAreaBgColor", (Color)ColorConverter.ConvertFromString("#F8FAFC"));
        }

        // ブラシを再生成
        res["BgDarkBrush"] = new SolidColorBrush((Color)res["BgDarkColor"]);
        res["CardBgBrush"] = new SolidColorBrush((Color)res["CardBgColor"]);
        res["CardBorderBrush"] = new SolidColorBrush((Color)res["CardBorderColor"]);
        res["InputBgBrush"] = new SolidColorBrush((Color)res["InputBgColor"]);
        res["InputBorderBrush"] = new SolidColorBrush((Color)res["InputBorderColor"]);
        res["TextPrimaryBrush"] = new SolidColorBrush((Color)res["TextPrimaryColor"]);
        res["TextSecondaryBrush"] = new SolidColorBrush((Color)res["TextSecondaryColor"]);

        res["PopupBgBrush"] = new SolidColorBrush((Color)res["PopupBgColor"]);
        res["ItemHoverBrush"] = new SolidColorBrush((Color)res["ItemHoverColor"]);
        res["ConsoleBgBrush"] = new SolidColorBrush((Color)res["ConsoleBgColor"]);
        res["DropAreaBgBrush"] = new SolidColorBrush((Color)res["DropAreaBgColor"]);
    }

    private static void SetResource(ResourceDictionary res, string key, object value)
    {
        res[key] = value;
    }
}

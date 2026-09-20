using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using WoodStreamAudioEditor.Models;

namespace WoodStreamAudioEditor.Services;

/// <summary>
/// アプリケーション設定の読み込みと保存を管理するサービス
/// 保存先: %LOCALAPPDATA%\WoodStream Audio Editor\settings.json
/// 日本語はUnicodeエスケープせずUTF-8可視テキストとして保存します。
/// </summary>
public class SettingsService
{
    private readonly string _appDirectory;
    private readonly string _settingsFilePath;

    /// <summary>
    /// コンストラクタ。パスを省略した場合は既定の %LOCALAPPDATA%\WoodStream Audio Editor\settings.json を使用します。
    /// テスト時は一時パスを渡すことで本番環境の設定ファイル汚染を防止できます。
    /// </summary>
    public SettingsService(string? customSettingsFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customSettingsFilePath))
        {
            _settingsFilePath = customSettingsFilePath;
            _appDirectory = Path.GetDirectoryName(customSettingsFilePath) ?? string.Empty;
        }
        else
        {
            _appDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WoodStream Audio Editor");
            _settingsFilePath = Path.Combine(_appDirectory, "settings.json");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // 日本語文字列をエスケープせず可視テキストとして保存
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    /// <summary>
    /// 設定ファイルから設定情報を読み込みます。ファイルが存在しない場合はデフォルト設定を返します。
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"設定ファイルの読み込みに失敗しました: {ex.Message}");
        }

        // デフォルト設定を返却
        return new AppSettings();
    }

    /// <summary>
    /// 設定情報をJSONファイルとして保存します。
    /// </summary>
    public void Save(AppSettings settings)
    {
        try
        {
            if (!string.IsNullOrEmpty(_appDirectory) && !Directory.Exists(_appDirectory))
            {
                Directory.CreateDirectory(_appDirectory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"設定ファイルの保存に失敗しました: {ex.Message}");
        }
    }
}

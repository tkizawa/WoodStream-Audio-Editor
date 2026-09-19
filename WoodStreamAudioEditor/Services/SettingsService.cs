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
    private static readonly string AppDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WoodStream Audio Editor");

    private static readonly string SettingsFilePath = Path.Combine(AppDirectory, "settings.json");

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
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
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
            if (!Directory.Exists(AppDirectory))
            {
                Directory.CreateDirectory(AppDirectory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"設定ファイルの保存に失敗しました: {ex.Message}");
        }
    }
}

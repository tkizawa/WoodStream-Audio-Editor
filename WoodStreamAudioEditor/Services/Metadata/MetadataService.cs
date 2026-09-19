using System;
using System.IO;
using System.Threading.Tasks;

namespace WoodStreamAudioEditor.Services.Metadata;

/// <summary>
/// ID3タグ情報モデル
/// </summary>
public record AudioMetadata(
    string Title,
    string Artist,
    string Album,
    string TrackNumber,
    string ArtworkPath);

/// <summary>
/// TagLib# を使用して MP3 ファイルに ID3v2 タグ（タイトル、アーティスト、アルバム、トラック、アートワーク画像）を埋め込むサービス
/// </summary>
public class MetadataService
{
    /// <summary>
    /// MP3ファイルにID3タグを非同期で書き込みます。
    /// </summary>
    public async Task ApplyMetadataAsync(string mp3FilePath, AudioMetadata metadata, Action<string>? logger = null)
    {
        await Task.Run(() =>
        {
            if (!File.Exists(mp3FilePath))
            {
                throw new FileNotFoundException($"対象のMP3ファイルが見つかりません: {mp3FilePath}");
            }

            try
            {
                logger?.Invoke($"[ID3] メタデータ書き込み開始: {Path.GetFileName(mp3FilePath)}");

                using var file = TagLib.File.Create(mp3FilePath);

                // タイトル
                if (!string.IsNullOrWhiteSpace(metadata.Title))
                {
                    file.Tag.Title = metadata.Title.Trim();
                }

                // アーティスト
                if (!string.IsNullOrWhiteSpace(metadata.Artist))
                {
                    file.Tag.Performers = new[] { metadata.Artist.Trim() };
                }

                // アルバム名 (番組名)
                if (!string.IsNullOrWhiteSpace(metadata.Album))
                {
                    file.Tag.Album = metadata.Album.Trim();
                }

                // トラック番号
                if (uint.TryParse(metadata.TrackNumber, out uint track))
                {
                    file.Tag.Track = track;
                }

                // アートワーク画像
                if (!string.IsNullOrWhiteSpace(metadata.ArtworkPath) && File.Exists(metadata.ArtworkPath))
                {
                    try
                    {
                        var picture = new TagLib.Picture(metadata.ArtworkPath)
                        {
                            Type = TagLib.PictureType.FrontCover,
                            Description = "Cover"
                        };
                        file.Tag.Pictures = new TagLib.IPicture[] { picture };
                        logger?.Invoke($"[ID3] アートワーク画像を埋め込みました: {Path.GetFileName(metadata.ArtworkPath)}");
                    }
                    catch (Exception ex)
                    {
                        logger?.Invoke($"[ID3 WARNING] アートワーク画像の読み込みに失敗しました: {ex.Message}");
                    }
                }

                file.Save();
                logger?.Invoke($"[ID3] ID3タグの書き込みが正常に完了しました。（タイトル: {metadata.Title}, アーティスト: {metadata.Artist}, アルバム: {metadata.Album}）");
            }
            catch (Exception ex)
            {
                logger?.Invoke($"[ID3 ERROR] ID3タグ書き込み中にエラーが発生しました: {ex.Message}");
                throw;
            }
        });
    }
}

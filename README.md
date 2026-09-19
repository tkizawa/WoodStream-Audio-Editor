# WoodStream Audio Editor

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/Platform-Windows%20x64-blue?logo=windows)
![License](https://img.shields.io/badge/License-MIT-green)

ポッドキャスト用の音声データを自動で高音質化・編集し、タグ付けして書き出すための Windows デスクトップアプリケーションです。

---

## 主な機能

1. **音声ファイルの読み込み**
   - WAV および MP3 形式の入力に対応。
   - ファイルのドラッグ＆ドロップ対応。
   - 96kHz / 192kHz 等のハイレゾ音源も 48kHz に自動高品質リサンプリング。

2. **トリミング（先頭・末尾カット）**
   - 録音開始時の準備音や録音終了後の不要な区間を指定秒数でカット。
   - 0.1秒単位のスライダーおよび数値指定に対応。
   - 5ms のスムーズな自動フェードイン・フェードアウトによりクリックノイズを完全防止。

3. **VST 2.4 プラグインによる音声処理**
   - **iZotope RX 8 Elements**（De-click / Voice De-noise）との連携。
   - 既定のインストールフォルダから 64bit VST プラグインを自動検出。
   - 1024 サンプル固定ブロック FIFO ストリーミングによる音飛び・ノイズのない安定処理。

4. **無音部分の自動カット (Silence Truncation)**
   - トークの途中で生じる長すぎる沈黙や考え込みを自動削除。
   - 無音判定音量閾値（dB）および保持する最小無音時間（ms）を調整可能。
   - つなぎ目の 5ms スムーズフェードにより、会話の自然な間合いを保ちながら時短編集を実現。

5. **BGM・エンディング曲の精密タイムラインミキシング**
   - **BGM**: 音声開始から再生（自動ループ対応）。**本編最後の10秒前から5秒間かけてフェードアウト**して終了。
   - **エンディング曲**: **本編最後の5秒前から5秒間かけてフェードイン**し、**曲の最後まで完全再生**（フェードアウトなし）。
   - 音声・BGM・エンディング曲それぞれの独立した音量調整およびソフトクリッピング防止。

6. **MP3 エンコード & メタデータ（ID3 タグ）埋め込み**
   - LAME による高品質 MP3 エンコード（128 / 192 / 256 / 320 kbps）。
   - タイトル、アーティスト名、アルバム名、トラック番号、アートワーク画像（JPEG/PNG）の ID3v2 自動埋め込み。

7. **モダン UI & 状態永続化**
   - Windows のテーマ設定（ダークモード / ライトモード）に自動連動。
   - 多言語対応（日本語 / 英語）。
   - アプリ終了時にすべての画面設定・入力値・ウィンドウ位置とサイズを `%LOCALAPPDATA%\WoodStream Audio Editor\settings.json` へ完全保存し、次回起動時に自動復元。

---

## システム要件

- **OS**: Windows 10 / 11 (64-bit)
- **ランタイム**: [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0)
- **推奨プラグイン**: iZotope RX 8 Elements (または同等の VST 2.4 x64 プラグイン)

---

## ビルドと実行

### 開発環境でのビルド

```powershell
# リポジトリのクローン
git clone https://github.com/tkizawa/WoodStream-Audio-Editor.git
cd WoodStream-Audio-Editor

# ビルド (x64)
dotnet build -p:Platform=x64

# テスト実行
dotnet test -p:Platform=x64

# アプリケーションの起動
dotnet run --project WoodStreamAudioEditor\WoodStreamAudioEditor.csproj -p:Platform=x64
```

---

## 技術スタック

- **言語**: C# 13
- **フレームワーク**: .NET 10 (WPF)
- **MVVM**: CommunityToolkit.Mvvm
- **音声処理コア**: [NAudio](https://github.com/naudio/NAudio)
- **MP3 エンコード**: [NAudio.Lame](https://github.com/Corey-M/NAudio.Lame)
- **VST ホスト**: [VST.NET2-Host](https://github.com/obiwanjacobi/vst.net)
- **メタデータ**: [TagLib#](https://github.com/mono/taglib-sharp)

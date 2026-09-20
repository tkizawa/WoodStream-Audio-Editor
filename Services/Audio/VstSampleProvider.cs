using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jacobi.Vst.Core;
using Jacobi.Vst.Host.Interop;
using NAudio.Wave;

namespace WoodStreamAudioEditor.Services.Audio;

/// <summary>
/// VST 2.4 プラグイン（iZotope RX 8 De-click / Voice De-noise 等）を NAudio のパイプラインに組み込む SampleProvider
/// VST プラグインの要件に合致するよう、常に完全な固定ブロックサイズ（1024サンプル）単位で連続処理し、
/// 内部 FIFO キューを経由して出力することで、バッファ境界での音飛びやプチプチノイズ（断片化）を完全に防止します。
/// </summary>
public class VstSampleProvider : ISampleProvider, IDisposable
{
    private readonly ISampleProvider _source;
    private readonly int _blockSize;
    private readonly Action<string>? _logger;
    private readonly VstPluginContext? _pluginContext;
    private readonly VstAudioBufferManager? _inputBufferMgr;
    private readonly VstAudioBufferManager? _outputBufferMgr;
    private readonly VstAudioBuffer[]? _inputBuffers;
    private readonly VstAudioBuffer[]? _outputBuffers;
    private readonly float[] _sourceBlockBuffer;
    private readonly Queue<float> _outputQueue = new();
    private readonly bool _isLoaded;
    private bool _sourceEnded = false;

    public WaveFormat WaveFormat => _source.WaveFormat;
    public bool IsPluginLoaded => _isLoaded;
    public string PluginName => Path.GetFileNameWithoutExtension(PluginPath);
    public string PluginPath { get; }

    public VstSampleProvider(ISampleProvider source, string pluginPath, int blockSize = 1024, Action<string>? logger = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _blockSize = blockSize;
        _logger = logger;
        PluginPath = pluginPath ?? string.Empty;

        int channels = _source.WaveFormat.Channels;
        _sourceBlockBuffer = new float[_blockSize * channels];

        if (string.IsNullOrWhiteSpace(pluginPath) || !File.Exists(pluginPath))
        {
            _logger?.Invoke($"[VST] プラグインが未指定または見つかりません: {pluginPath} (バイパスします)");
            return;
        }

        try
        {
            var hostCmdStub = new VstHostCommandStub(_blockSize, _source.WaveFormat.SampleRate);
            _pluginContext = VstPluginContext.Create(pluginPath, hostCmdStub);
            hostCmdStub.PluginContext = _pluginContext;

            // プラグイン初期化
            _pluginContext.PluginCommandStub.Commands.SetSampleRate(_source.WaveFormat.SampleRate);
            _pluginContext.PluginCommandStub.Commands.SetBlockSize(_blockSize);
            _pluginContext.PluginCommandStub.Commands.MainsChanged(true);
            _pluginContext.PluginCommandStub.Commands.StartProcess();

            int pluginInputs = Math.Max(1, _pluginContext.PluginInfo.AudioInputCount);
            int pluginOutputs = Math.Max(1, _pluginContext.PluginInfo.AudioOutputCount);

            _inputBufferMgr = new VstAudioBufferManager(pluginInputs, _blockSize);
            _outputBufferMgr = new VstAudioBufferManager(pluginOutputs, _blockSize);
            _inputBuffers = _inputBufferMgr.Buffers.ToArray();
            _outputBuffers = _outputBufferMgr.Buffers.ToArray();

            _isLoaded = true;
            _logger?.Invoke($"[VST] プラグイン読み込み成功: {PluginName} (In:{pluginInputs}ch, Out:{pluginOutputs}ch)");
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"[VST ERROR] プラグイン初期化失敗 ({pluginPath}): {ex.Message} (バイパスします)");
            Dispose();
            _isLoaded = false;
        }
    }

    public int Read(Span<float> buffer)
    {
        // プラグインが無効または未読み込みの場合はバイパス
        if (!_isLoaded || _pluginContext == null || _inputBuffers == null || _outputBuffers == null)
        {
            return _source.Read(buffer);
        }

        int channels = WaveFormat.Channels;
        int totalWritten = 0;
        int count = buffer.Length;

        while (totalWritten < count)
        {
            // 1. 内部キューに処理済みデータがあれば先に出力バッファへコピー
            while (_outputQueue.Count > 0 && totalWritten < count)
            {
                buffer[totalWritten++] = _outputQueue.Dequeue();
            }

            if (totalWritten >= count) break;
            if (_sourceEnded) break;

            // 2. 内部キューが空になったら、ソースから完全な 1 ブロック (_blockSize * channels) を読み出す
            int neededSamples = _blockSize * channels;
            int totalReadFromSource = 0;

            while (totalReadFromSource < neededSamples)
            {
                int read = _source.Read(_sourceBlockBuffer.AsSpan(totalReadFromSource, neededSamples - totalReadFromSource));
                if (read == 0)
                {
                    _sourceEnded = true;
                    // 残りをゼロパディング
                    Array.Clear(_sourceBlockBuffer, totalReadFromSource, neededSamples - totalReadFromSource);
                    break;
                }
                totalReadFromSource += read;
            }

            if (totalReadFromSource == 0)
            {
                // ソース終了かつ未処理ブロックなし
                break;
            }

            int validFrames = totalReadFromSource / channels;

            // 3. 入力バッファへデインターリーブコピー
            for (int ch = 0; ch < _inputBuffers.Length; ch++)
            {
                var inputChannel = _inputBuffers[ch];
                int srcCh = ch < channels ? ch : 0; // モノラル音源なら ch0 を両チャンネルに供給

                for (int f = 0; f < _blockSize; f++)
                {
                    inputChannel[f] = _sourceBlockBuffer[f * channels + srcCh];
                }
            }

            // 4. VST プラグインによる完全な 1 ブロック処理 (1024 サンプル)
            _pluginContext.PluginCommandStub.Commands.ProcessReplacing(_inputBuffers, _outputBuffers);

            // 5. 処理結果をインターリーブして内部キューへ格納 (有効なフレーム数分のみ)
            for (int f = 0; f < validFrames; f++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    int outCh = ch < _outputBuffers.Length ? ch : 0;
                    _outputQueue.Enqueue(_outputBuffers[outCh][f]);
                }
            }
        }

        return totalWritten;
    }

    public void Dispose()
    {
        if (_pluginContext != null)
        {
            try
            {
                _pluginContext.PluginCommandStub.Commands.StopProcess();
                _pluginContext.PluginCommandStub.Commands.MainsChanged(false);
                _pluginContext.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[VST] 解放例外: {ex.Message}");
            }
        }

        _inputBufferMgr?.Dispose();
        _outputBufferMgr?.Dispose();
    }
}

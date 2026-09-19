using System;
using System.IO;
using System.Linq;
using Jacobi.Vst.Core;
using Jacobi.Vst.Host.Interop;
using NAudio.Wave;

namespace WoodStreamAudioEditor.Services.Audio;

/// <summary>
/// VST 2.4 プラグイン（iZotope RX 8 De-click / Voice De-noise 等）を NAudio のパイプラインに組み込む SampleProvider
/// ステレオ (2ch) またはモノラル (1ch) のオーディオストリームを VST プラグインに入力し、
/// 処理後の音声を後続のプロバイダー（無音カットやMP3エンコーダー）へ渡します。
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
    private readonly float[] _sourceBuffer;
    private readonly bool _isLoaded;

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
        _sourceBuffer = new float[_blockSize * channels];

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
        int totalRead = 0;
        int count = buffer.Length;

        while (totalRead < count)
        {
            // 1ブロック分のサンプル数を計算
            int samplesNeeded = Math.Min(count - totalRead, _blockSize * channels);
            int framesNeeded = samplesNeeded / channels;
            if (framesNeeded == 0) break;

            int samplesRead = _source.Read(_sourceBuffer.AsSpan(0, framesNeeded * channels));
            if (samplesRead == 0) break; // ソース終了

            int framesRead = samplesRead / channels;

            // 入力バッファへコピー（デインターリーブ）
            for (int ch = 0; ch < _inputBuffers.Length; ch++)
            {
                var inputChannel = _inputBuffers[ch];
                int srcCh = ch < channels ? ch : 0; // 足りない場合はch0を使用

                for (int f = 0; f < framesRead; f++)
                {
                    inputChannel[f] = _sourceBuffer[f * channels + srcCh];
                }
                // 残りはゼロクリア
                for (int f = framesRead; f < _blockSize; f++)
                {
                    inputChannel[f] = 0.0f;
                }
            }

            // VST 処理実行
            _pluginContext.PluginCommandStub.Commands.ProcessReplacing(_inputBuffers, _outputBuffers);

            // 出力バッファからインターリーブして出力配列へ書き出し
            for (int f = 0; f < framesRead; f++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    int outCh = ch < _outputBuffers.Length ? ch : 0;
                    buffer[totalRead++] = _outputBuffers[outCh][f];
                }
            }
        }

        return totalRead;
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

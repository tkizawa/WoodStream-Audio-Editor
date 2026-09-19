using System;
using Jacobi.Vst.Core;
using Jacobi.Vst.Core.Host;

namespace WoodStreamAudioEditor.Services.Audio;

#pragma warning disable CS8766

/// <summary>
/// VST2 ホストコマンドスタブ実装
/// VSTプラグイン（iZotope RX 8 Elements 等）からの問い合わせに応答します。
/// </summary>
public class VstHostCommandStub : IVstHostCommandStub, IVstHostCommands20
{
    private readonly int _blockSize;
    private readonly float _sampleRate;

    public VstHostCommandStub(int blockSize = 1024, float sampleRate = 44100.0f)
    {
        _blockSize = blockSize;
        _sampleRate = sampleRate;
    }

    public IVstPluginContext PluginContext { get; set; } = null!;
    public IVstHostCommands20 Commands => this;

    // IVstHostCommands10 実装
    public void SetParameterAutomated(int index, float value) { }
    public int GetVersion() => 2400;
    public int GetCurrentPluginID() => PluginContext?.PluginInfo?.PluginID ?? 0;
    public void ProcessIdle() { }

    // IVstHostCommands20 実装
    public bool ProcessEvents(VstEvent[] events) => true;
    public bool IoChanged() => false;
    public bool SizeWindow(int width, int height) => false;
    public float GetSampleRate() => _sampleRate;
    public int GetBlockSize() => _blockSize;
    public int GetInputLatency() => 0;
    public int GetOutputLatency() => 0;
    public VstProcessLevels GetProcessLevel() => VstProcessLevels.User;
    public VstAutomationStates GetAutomationState() => VstAutomationStates.Off;
    public VstTimeInfo? GetTimeInfo(VstTimeInfoFlags filter) => null;
    public string GetVendorString() => "WoodStream";
    public string GetProductString() => "WoodStream Audio Editor";
    public int GetVendorVersion() => 1000;

    public VstCanDoResult CanDo(string cando)
    {
        return cando switch
        {
            "sendVstEvents" => VstCanDoResult.Yes,
            "sendVstTimeInfo" => VstCanDoResult.No,
            "sizeWindow" => VstCanDoResult.No,
            "shellCategory" => VstCanDoResult.Yes,
            _ => VstCanDoResult.Unknown
        };
    }

    public VstHostLanguage GetLanguage() => VstHostLanguage.NotSupported;
    public string GetDirectory() => AppDomain.CurrentDomain.BaseDirectory;
    public bool UpdateDisplay() => true;
    public bool BeginEdit(int index) => true;
    public bool EndEdit(int index) => true;
    public bool OpenFileSelector(VstFileSelect fileSelect) => false;
    public bool CloseFileSelector(VstFileSelect fileSelect) => false;
}

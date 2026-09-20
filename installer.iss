; WoodStream Audio Editor - Inno Setup Script
; プロジェクトルール:
; - スタンドアロンインストーラは exe 形式で .\Installer フォルダに作成し、ファイル名にはバージョン番号を含めること。
; - 実行環境のアーキテクチャ (x64, arm64 等) に合わせたものを作成すること。

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0.0"
#endif

#ifndef MyAppArch
  #define MyAppArch "x64"
#endif

#ifndef MyOutputDir
  #define MyOutputDir "Installer"
#endif

#ifndef MySourceDir
  #define MySourceDir "publish\win-" + MyAppArch
#endif

#define MyAppName "WoodStream Audio Editor"
#define MyAppPublisher "tkizawa"
#define MyAppExeName "WoodStreamAudioEditor.exe"
#define MyOutputBaseFilename "WoodStreamAudioEditor_Setup_v" + MyAppVersion + "_" + MyAppArch

[Setup]
AppId={{06773897-23A8-462E-A0B8-BE371D694FF2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; ユーザー権限でもインストール可能（管理者権限があれば昇格可能）
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyOutputBaseFilename}
SetupIconFile=Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

; アーキテクチャに応じた制限設定
#if MyAppArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; dotnet publish による出力ファイル一式
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

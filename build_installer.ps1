param(
    [ValidateSet("x64", "arm64", "all")]
    [string]$Architecture
)

$ErrorActionPreference = "Stop"

# プロジェクト情報およびバージョンの取得
$projPath = Join-Path $PSScriptRoot "WoodStreamAudioEditor.csproj"
[xml]$projXml = Get-Content $projPath
$version = $projXml.Project.PropertyGroup.Version
if (-not $version) { $version = $projXml.Project.PropertyGroup.FileVersion }
if (-not $version) { $version = "1.0.0.0" }

# 対象アーキテクチャの決定（指定がなければ実行環境に合わせる）
$targetArchs = @()
if ($Architecture -eq "all") {
    $targetArchs = @("x64", "arm64")
} elseif ($Architecture) {
    $targetArchs = @($Architecture)
} else {
    $currentArch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) { "arm64" } else { "x64" }
    $targetArchs = @($currentArch)
}


Write-Host "=== WoodStream Audio Editor スタンドアロンインストーラー作成 (v$version) ===" -ForegroundColor Cyan

# 出力先ディレクトリ (規約: .\Installer)
$installerDir = Join-Path $PSScriptRoot "Installer"
if (-not (Test-Path $installerDir)) {
    New-Item -ItemType Directory -Path $installerDir | Out-Null
}

# Inno Setup コンパイラの検出
Write-Host "Inno Setup Compiler (ISCC.exe) を検索中..." -ForegroundColor Green
$candidatePaths = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$innoSetupPath = $null
foreach ($path in $candidatePaths) {
    if (Test-Path $path) {
        $innoSetupPath = $path
        break
    }
}

if (-not $innoSetupPath) {
    Write-Host "Inno Setup Compiler が見つかりません。winget を用いてインストールを試行します..." -ForegroundColor Yellow
    winget install --id JRSoftware.InnoSetup -e --accept-source-agreements --accept-package-agreements
    
    Start-Sleep -Seconds 2

    foreach ($path in $candidatePaths) {
        if (Test-Path $path) {
            $innoSetupPath = $path
            break
        }
    }

    if (-not $innoSetupPath) {
        Write-Error "Inno Setup のインストールまたは検出に失敗しました。https://jrsoftware.org/isinfo.php より手動でインストールしてください。"
        exit 1
    }
}

Write-Host "Inno Setup Compiler: $innoSetupPath" -ForegroundColor Gray

$issFile = Join-Path $PSScriptRoot "installer.iss"
$builtFiles = @()

foreach ($arch in $targetArchs) {
    Write-Host ""
    Write-Host "--------------------------------------------------" -ForegroundColor Cyan
    Write-Host ">>> [win-$arch] 発行およびインストーラー作成開始" -ForegroundColor Cyan
    Write-Host "--------------------------------------------------" -ForegroundColor Cyan

    $publishDir = Join-Path $PSScriptRoot "publish\win-$arch"
    if (Test-Path $publishDir) {
        Remove-Item -Path $publishDir -Recurse -Force
    }

    Write-Host "dotnet publish を実行中 (win-$arch, self-contained)..." -ForegroundColor Green
    dotnet publish "$projPath" -c Release -r "win-$arch" --self-contained true -o "$publishDir"

    if ($LASTEXITCODE -ne 0) {
        Write-Error "win-$arch の発行に失敗しました。"
        exit $LASTEXITCODE
    }

    $outputBaseFilename = "WoodStreamAudioEditor_Setup_v${version}_$arch"

    Write-Host "Inno Setup によるインストーラー作成中 ($outputBaseFilename.exe)..." -ForegroundColor Green
    & $innoSetupPath "/DMyAppVersion=$version" "/DMyAppArch=$arch" "/DMyOutputDir=$installerDir" "/DMyOutputBaseFilename=$outputBaseFilename" "/DMySourceDir=$publishDir" "$issFile"

    if ($LASTEXITCODE -ne 0) {
        Write-Error "win-$arch のインストーラー作成に失敗しました。"
        exit $LASTEXITCODE
    }

    $targetExe = Join-Path $installerDir "$outputBaseFilename.exe"
    if (Test-Path $targetExe) {
        $fileInfo = Get-Item $targetExe
        $hashInfo = Get-FileHash -Path $targetExe -Algorithm SHA256
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        $builtFiles += [PSCustomObject]@{
            Arch     = $arch
            Path     = $fileInfo.FullName
            FileName = $fileInfo.Name
            SizeMB   = $sizeMB
            SizeByte = $fileInfo.Length
            SHA256   = $hashInfo.Hash
        }
    } else {
        Write-Warning "生成されたインストーラーが見つかりません: $targetExe"
    }
}

Write-Host ""
Write-Host "==================================================" -ForegroundColor Green
Write-Host "すべてのインストーラーの作成が完了しました" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
foreach ($item in $builtFiles) {
    Write-Host "[$($item.Arch)] $($item.FileName)" -ForegroundColor Yellow
    Write-Host "  パス:    $($item.Path)" -ForegroundColor White
    Write-Host "  サイズ:  $($item.SizeMB) MB ($($item.SizeByte) bytes)" -ForegroundColor White
    Write-Host "  SHA256:  $($item.SHA256)" -ForegroundColor White
}

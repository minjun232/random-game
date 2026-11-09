$ErrorActionPreference = "Stop"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpfDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"
$fxDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"

$tmpExe = Join-Path $PSScriptRoot "StrikeRoulette_build.exe"
$bakExe = Join-Path $PSScriptRoot "prev_instance.bak"
$outDir = Split-Path -Parent $PSScriptRoot
$oldKorName = "$([char]0xC5F0)$([char]0xC18D)$([char]0xB8F0)$([char]0xB81B).exe"
$oldExe = Join-Path $outDir $oldKorName
$korName = "$([char]0xB79C)$([char]0xB364)$([char]0xAC8C)$([char]0xC784).exe"
$outExe = Join-Path $outDir $korName

Push-Location $PSScriptRoot
try {
    & $csc `
        /nologo `
        /target:winexe `
        /optimize+ `
        /codepage:65001 `
        /utf8output `
        "/win32icon:app.ico" `
        "/resource:app.ico,StrikeRoulette.app.ico" `
        "/out:StrikeRoulette_build.exe" `
        "/r:$wpfDir\PresentationFramework.dll" `
        "/r:$wpfDir\PresentationCore.dll" `
        "/r:$wpfDir\WindowsBase.dll" `
        "/r:$fxDir\System.Xaml.dll" `
        "/r:$fxDir\System.Drawing.dll" `
        "Program.cs" `
        "LadderTab.cs" `
        "LotsTab.cs" `
        "TeamSplitTab.cs"

    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed with exit code $LASTEXITCODE"
    }
} finally {
    Pop-Location
}

if (Test-Path -LiteralPath $oldExe) {
    try {
        Remove-Item -LiteralPath $oldExe -Force -ErrorAction Stop
    } catch {
        Remove-Item -LiteralPath $bakExe -Force -ErrorAction SilentlyContinue
        Move-Item -Force -LiteralPath $oldExe -Destination $bakExe -ErrorAction SilentlyContinue
    }
}

if (Test-Path -LiteralPath $outExe) {
    try {
        Remove-Item -LiteralPath $outExe -Force -ErrorAction Stop
    } catch {
        # Running EXE lock: move old image aside via NTFS FILE_SHARE_DELETE
        Remove-Item -LiteralPath $bakExe -Force -ErrorAction SilentlyContinue
        Move-Item -Force -LiteralPath $outExe -Destination $bakExe
    }
}
Move-Item -Force -LiteralPath $tmpExe -Destination $outExe
try { Start-Process -FilePath "ie4uinit.exe" -ArgumentList "-show" -NoNewWindow -ErrorAction SilentlyContinue } catch {}
Write-Output "Successfully built EXE!"
Get-Item -LiteralPath $outExe | ForEach-Object {
    Write-Output ("File: " + $_.FullName + " (" + $_.Length + " bytes)")
}

# 랜덤 게임 v2.0 웹 빌드 / 로컬 실행 스크립트
#   빌드:      powershell -ExecutionPolicy Bypass -File .\web\build.ps1
#   로컬 실행: powershell -ExecutionPolicy Bypass -File .\web\build.ps1 -Serve   (http://127.0.0.1:8080)
# 결과물은 web\dist 폴더 (정적 파일만 있으므로 아무 웹 호스팅에나 그대로 올리면 됨)
param([switch]$Serve)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Rust(gnu) 툴체인 + 사용자 폴더의 mingw 도구 (proc-macro 빌드에 필요)
$env:PATH = "$env:USERPROFILE\.cargo\bin;$env:USERPROFILE\.mingw-winlibs\mingw64\bin;$env:PATH"
# mingw 도구가 한글/전각 공백이 들어간 경로를 못 읽으므로 빌드 중간 파일은 ASCII 경로에 둔다
$env:CARGO_TARGET_DIR = "$env:USERPROFILE\.cargo\target-random-game-web"

if ($Serve) {
    trunk serve --release --address 127.0.0.1 --port 8080
} else {
    trunk build --release
}

# LXIUM 포터블 빌드
# 1. launcher/를 단일 exe(자체 포함)로 게시해 dist/LXIUM-<버전>/에 둡니다 (lang/ 언어 파일, 아이콘 라이선스 포함).
# 2. 결과 폴더를 dist/LXIUM-<버전>-portable.zip으로 묶습니다.
# 모드는 넣지 않습니다. 런처가 애드온 모음(catalog.json)으로 각 모드 저장소의 릴리스를 직접 읽어 사용자가 추가합니다 (docs/ai/catalog.md).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$ver = (Get-Content "$root/ver.txt" -Raw).Trim()
$out = "$root/dist/LXIUM-$ver"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish "$root/launcher/LXIUM.csproj" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out
if ($LASTEXITCODE) { throw "런처 빌드 실패" }
Copy-Item "$root/launcher/Assets/Lucide-LICENSE.txt" $out

Compress-Archive "$out/*" "$root/dist/LXIUM-$ver-portable.zip" -Force
Write-Host "완료: dist/LXIUM-$ver-portable.zip"

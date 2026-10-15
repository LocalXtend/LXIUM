# LXIUM 포터블 빌드
# 1. launcher/를 단일 exe(자체 포함)로 게시해 dist/LXIUM-<버전>/에 둡니다.
# 2. optimizeMods/<id>/dist/<id>-<ver.txt 버전>.zip을 찾아 addons/에 넣습니다.
# 3. 결과 폴더를 dist/LXIUM-<버전>-portable.zip으로 묶습니다.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$ver = (Get-Content "$root/ver.txt" -Raw).Trim()
$out = "$root/dist/LXIUM-$ver"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish "$root/launcher/LXIUM.csproj" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out
if ($LASTEXITCODE) { throw "런처 빌드 실패" }

New-Item "$out/addons" -ItemType Directory | Out-Null
foreach ($mod in Get-ChildItem "$root/optimizeMods" -Directory) {
    $verFile = Join-Path $mod.FullName 'ver.txt'
    $dist = Join-Path $mod.FullName 'dist'
    if (-not (Test-Path $verFile) -or -not (Test-Path $dist)) { continue }
    $modVer = (Get-Content $verFile -Raw).Trim()
    $zip = Join-Path $dist "$($mod.Name)-$modVer.zip"
    if (Test-Path $zip) {
        Copy-Item $zip "$out/addons/"
        Write-Host "애드온: $($mod.Name) $modVer"
    } else {
        Write-Warning "$($mod.Name): dist에 ver.txt 버전($modVer) zip이 없어 건너뜀"
    }
}

Compress-Archive "$out/*" "$root/dist/LXIUM-$ver-portable.zip" -Force
Write-Host "완료: dist/LXIUM-$ver-portable.zip"

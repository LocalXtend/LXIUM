# LXIUM 포터블 빌드
# 1. launcher/를 단일 exe(자체 포함)로 게시해 dist/LXIUM-<버전>/에 둡니다.
# 2. optimizeMods/<id>/dist/<id>-<ver.txt 버전>.zip을 찾아 addons/에 넣습니다. (optimizeMods/<id> = 모드 저장소 LocalXtend/<id>의 clone)
# 3. 결과 폴더를 dist/LXIUM-<버전>-portable.zip으로 묶습니다.
# 4. 공식 애드온 모음 dist/catalog.json을 만듭니다 (docs/ai/catalog.md). 항목 url은 모드 저장소 GitHub 릴리스 <id>-v<버전>의 zip
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = $PSScriptRoot
$owner = 'LocalXtend'
$ver = (Get-Content "$root/ver.txt" -Raw).Trim()
$out = "$root/dist/LXIUM-$ver"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish "$root/launcher/LXIUM.csproj" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out
if ($LASTEXITCODE) { throw "런처 빌드 실패" }
Copy-Item "$root/launcher/Assets/Lucide-LICENSE.txt" $out

New-Item "$out/addons" -ItemType Directory | Out-Null
$catalog = [ordered]@{ schema = 1; name = 'LXIUM 공식'; addons = @() }
foreach ($mod in Get-ChildItem "$root/optimizeMods" -Directory) {
    $verFile = Join-Path $mod.FullName 'ver.txt'
    $dist = Join-Path $mod.FullName 'dist'
    if (-not (Test-Path $verFile) -or -not (Test-Path $dist)) { continue }
    $modVer = (Get-Content $verFile -Raw).Trim()
    $zip = Join-Path $dist "$($mod.Name)-$modVer.zip"
    if (-not (Test-Path $zip)) {
        Write-Warning "$($mod.Name): dist에 ver.txt 버전($modVer) zip이 없어 건너뜀"
        continue
    }
    Copy-Item $zip "$out/addons/"

    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $reader = New-Object System.IO.StreamReader ($archive.GetEntry('lxium.json').Open())
        $manifest = $reader.ReadToEnd() | ConvertFrom-Json
        $reader.Dispose()
    } finally { $archive.Dispose() }
    if ($manifest.version -ne $modVer) { throw "$($mod.Name): lxium.json version($($manifest.version))이 ver.txt($modVer)와 다름" }
    $catalog.addons += [ordered]@{
        url      = "https://github.com/$owner/$($mod.Name)/releases/download/$($mod.Name)-v$modVer/$($mod.Name)-$modVer.zip"
        sha256   = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
        manifest = $manifest
    }
    Write-Host "애드온: $($mod.Name) $modVer"
}

[System.IO.File]::WriteAllText("$root/dist/catalog.json", ($catalog | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding $false))
Compress-Archive "$out/*" "$root/dist/LXIUM-$ver-portable.zip" -Force
Write-Host "완료: dist/LXIUM-$ver-portable.zip, dist/catalog.json"

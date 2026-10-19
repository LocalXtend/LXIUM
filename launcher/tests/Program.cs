// 런처 모델 자체 검사. 실행: dotnet run --project launcher/tests
using System.IO.Compression;
using System.Text;
using LXIUM.Models;

static void Check(bool ok, string what)
{
    if (!ok) throw new Exception("실패: " + what);
    Console.WriteLine("ok  " + what);
}

// 문구는 기준 언어(ko)로 검사 (lang/은 런처 프로젝트에서 같이 복사됨)
Loc.I.Load(AppContext.BaseDirectory, "ko");
Check(Loc.T("mod.installed", "1.0.0", "BepInEx 6") == "v1.0.0 설치됨 · BepInEx 6" && Loc.Available(AppContext.BaseDirectory).Count == 5, "언어 파일 5개, 자리 채우기");

// 로더 버전 비교
Check(Versions.Compare("6.0.0-be.785", "6.0.0-be.785") == 0, "같은 버전");
Check(Versions.Compare("6.0.0-be.697", "6.0.0-be.785") < 0, "프리릴리스 번호 비교");
Check(Versions.Compare("6.0.0", "6.0.0-be.785") > 0, "정식 > 프리릴리스");
Check(Versions.Compare("5.4.23.2", "5.4.21") > 0, "숫자 자리 비교");
Check(Versions.Compare("6.0.0-be.785+abc", "6.0.0-be.785") == 0, "빌드 메타 무시");

// VDF 읽기
const string vdf = """
"libraryfolders"
{
	"0" { "path"		"C:\\Program Files (x86)\\Steam" }
	"1" { "path"		"D:\\SteamLibrary" "label" "" }
}
""";
var libs = Steam.ReadValues(vdf, "path");
Check(libs.SequenceEqual([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"]), "libraryfolders.vdf 경로");

// 변형 선택
var manifest = new Manifest(1, "LXIUM_test", "Test", "1.0.0", null, new GameRef("test", "Test", "Test.exe", null),
    new() { ["bepinex6"] = new("6.0.0-be.785") });
Check(Installer.PickVariant(manifest, "bepinex6").Variant == "bepinex6", "감지된 로더 변형");
Check(Installer.PickVariant(manifest, "melonloader").Error is not null, "지원하지 않는 로더 거부");
Check(Installer.PickVariant(manifest, null).Error is not null, "로더 없음 거부");
var withNone = manifest with { Variants = new() { ["bepinex6"] = new(null), ["none"] = new(null) } };
Check(Installer.PickVariant(withNone, "melonloader").Variant == "none", "none 변형으로 대체");

// 설치·제거 왕복
var work = Directory.CreateTempSubdirectory("lxium-test").FullName;
try
{
    var root = Path.Combine(work, "game");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "Test.exe"), "");
    File.WriteAllText(Path.Combine(root, "data.txt"), "원본");

    var zipPath = Path.Combine(work, "LXIUM_test-1.0.0.zip");
    MakeZip(zipPath, new()
    {
        ["lxium.json"] = """{"schema":1,"id":"LXIUM_test","name":"Test","version":"1.0.0","game":{"id":"test","name":"Test","exe":"Test.exe"},"variants":{"bepinex6":{}}}""",
        ["bepinex6/data.txt"] = "모드",
        ["bepinex6/BepInEx/plugins/LXIUM_test/LXIUM_test.dll"] = "dll",
    });
    var addon = Addon.Load(zipPath);
    Check(addon.Manifest.Game.Exe == "Test.exe", "lxium.json 읽기");

    Installer.Install(addon, "bepinex6", root);
    Check(File.ReadAllText(Path.Combine(root, "data.txt")) == "모드", "덮어쓰기");
    Check(File.Exists(Path.Combine(root, "BepInEx/plugins/LXIUM_test/LXIUM_test.dll")), "모드 파일 복사");
    Check(Installer.Read(root, "LXIUM_test") is { Files.Count: 2, Backups.Count: 1 }, "설치 기록");

    File.WriteAllText(Path.Combine(root, "BepInEx/plugins/LXIUM_test/user.cfg"), "설정");
    Installer.Uninstall(addon, root);
    Check(File.ReadAllText(Path.Combine(root, "data.txt")) == "원본", "백업 복원");
    Check(!File.Exists(Path.Combine(root, "BepInEx/plugins/LXIUM_test/LXIUM_test.dll")), "모드 파일 삭제");
    Check(File.Exists(Path.Combine(root, "BepInEx/plugins/LXIUM_test/user.cfg")), "기록에 없는 설정 보존");
    Check(!Directory.Exists(Path.Combine(root, ".lxium", "backup")) && Installer.Read(root, "LXIUM_test") is null, "기록 정리");

    File.Delete(Path.Combine(root, "BepInEx/plugins/LXIUM_test/user.cfg"));
    Installer.Install(addon, "bepinex6", root);
    Installer.Uninstall(addon, root);
    Check(!Directory.Exists(Path.Combine(root, "BepInEx")), "빈 폴더 삭제");

    // 게임 루트 밖으로 나가는 경로 거부
    var evilZip = Path.Combine(work, "evil.zip");
    MakeZip(evilZip, new()
    {
        ["lxium.json"] = """{"schema":1,"id":"evil","name":"E","version":"1.0.0","game":{"id":"test","name":"T","exe":"Test.exe"},"variants":{"none":{}}}""",
        ["none/../escaped.txt"] = "x",
    });
    var threw = false;
    try { Installer.Install(Addon.Load(evilZip), "none", root); } catch (InvalidDataException) { threw = true; }
    Check(threw && !File.Exists(Path.Combine(work, "escaped.txt")), "zip 경로 탈출 거부");

    // 이미 같은 모드가 수동으로 깔려 있던 경우: 설치 후 제거하면 모드가 남지 않아야 함
    var own = Path.Combine(root, "BepInEx/plugins/LXIUM_test/LXIUM_test.dll");
    Directory.CreateDirectory(Path.GetDirectoryName(own)!);
    File.WriteAllText(own, "수동 복사");
    Check(Installer.FindUntracked(addon, root).SequenceEqual(["BepInEx/plugins/LXIUM_test/LXIUM_test.dll"]), "기록 없는 설치 감지");
    Installer.Install(addon, "bepinex6", root);
    Check(Installer.Read(root, "LXIUM_test") is { Backups.Count: 1 }, "모드 자신의 파일은 백업하지 않음");
    Installer.Uninstall(addon, root);
    Check(!File.Exists(own) && File.ReadAllText(Path.Combine(root, "data.txt")) == "원본", "수동 설치 위에 설치 후 제거");

    Directory.CreateDirectory(Path.GetDirectoryName(own)!);
    File.WriteAllText(own, "수동 복사");
    Installer.Uninstall(addon, root);
    Check(!File.Exists(own), "기록 없이 제거");

    // 0.1.x 기록: 모드 자신의 파일을 백업해 둔 경우 복원하지 않음
    var legacyBackup = Path.Combine(root, ".lxium/backup/LXIUM_test/BepInEx/plugins/LXIUM_test/LXIUM_test.dll");
    Directory.CreateDirectory(Path.GetDirectoryName(legacyBackup)!);
    File.WriteAllText(legacyBackup, "옛 모드");
    File.WriteAllText(Path.Combine(root, ".lxium/LXIUM_test.json"),
        """{"id":"LXIUM_test","version":"1.0.0","variant":"bepinex6","files":["BepInEx/plugins/LXIUM_test/LXIUM_test.dll"],"backups":["BepInEx/plugins/LXIUM_test/LXIUM_test.dll"]}""");
    Installer.Uninstall(addon, root);
    Check(!File.Exists(own) && !Directory.Exists(Path.Combine(root, ".lxium/backup")) && Installer.Read(root, "LXIUM_test") is null, "옛 기록의 모드 백업 복원 안 함");

    // 로더 제거: 표의 파일·폴더를 지우고, BepInEx 것이 아닌 changelog.txt는 남김
    var game = Path.Combine(work, "game2");
    foreach (var f in new[] { "BepInEx/core/BepInEx.Core.dll", "BepInEx/plugins/Other/o.dll", "dotnet/x.dll", "winhttp.dll", "doorstop_config.ini", "Game.exe" })
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(game, f))!);
        File.WriteAllText(Path.Combine(game, f), "");
    }
    File.WriteAllText(Path.Combine(game, "changelog.txt"), "게임 패치 노트");
    LoaderSetup.Uninstall("bepinex6", game);
    Check(Directory.EnumerateFileSystemEntries(game).Select(Path.GetFileName).Order().SequenceEqual(["changelog.txt", "Game.exe"]), "BepInEx 6 제거");
    Check(LoaderSetup.Is64Bit(Environment.ProcessPath!) == Environment.Is64BitProcess, "PE 비트 판별");
}
finally
{
    Directory.Delete(work, true);
}

// Steam 상점 응답 해석
using var doc = System.Text.Json.JsonDocument.Parse("""
{"ok":{"success":true,"data":{"name":"G","short_description":"A &amp; B","release_date":{"coming_soon":false,"date":"2025년 7월 18일"},
  "genres":[{"id":"25","description":"어드벤처"}],"developers":["Dev"],"publishers":["Pub1","Pub2"]}},
 "missing":{"success":false}}
""");
var info = SteamStore.Parse(doc.RootElement.GetProperty("ok"));
Check(info is { Summary: "A & B", Released: "2025년 7월 18일", Genres: ["어드벤처"], Developers: ["Dev"], Publishers: ["Pub1", "Pub2"] }, "Steam 상점 해석");
Check(SteamStore.Parse(doc.RootElement.GetProperty("missing")) is null, "상점에 없는 앱");

// 설정 파일 읽기·저장 (BepInEx .cfg, MelonLoader TOML)
var cfgDir = Directory.CreateTempSubdirectory("lxium-cfg").FullName;
try
{
    var bepPath = Path.Combine(cfgDir, "bep.cfg");
    const string bep = "## Settings file was created by plugin X v1\r\n## Plugin GUID: X\r\n\r\n[Frame Rate]\r\n\r\n" +
        "## 목표 fps\r\n# Setting type: Int32\r\n# Default value: 0\r\n# Acceptable value range: From -1 to 240\r\nTargetFps = 0\r\n\r\n" +
        "## 끄기\r\n# Setting type: Boolean\r\n# Default value: true\r\nMsaaOff = true\r\n\r\n" +
        "## 모드\r\n# Setting type: SsaoMode\r\n# Default value: Low\r\n# Acceptable values: Off, Low, High\r\nSsao = Low\r\n\r\n" +
        "## 로그\r\n# Setting type: LogLevel\r\n# Default value: Warn, Error\r\n# Acceptable values: Info, Warn, Error\r\n" +
        "# Multiple values can be set at the same time by separating them with , (e.g. Debug, Warning)\r\nLog = Warn, Error\r\n";
    File.WriteAllText(bepPath, bep);
    var cfg = ConfigFile.Load(bepPath);
    var entries = cfg.Sections.SelectMany(s => s.Entries).ToDictionary(e => e.Key);
    Check(cfg.Sections is [{ Name: "Frame Rate" }] && entries.Count == 4, "BepInEx 섹션·항목");
    Check(entries["TargetFps"] is { Description: "목표 fps", Type: "Int32", Default: "0", Range: "From -1 to 240", IsSlider: true, Min: -1, Max: 240 }, "BepInEx 주석 해석");
    entries["TargetFps"].NumberValue = 143.6;
    Check(entries["TargetFps"].Value == "144", "슬라이더 정수 반올림");
    Check(entries["MsaaOff"].IsBool && entries["Ssao"] is { HasChoices: true, Choices: ["Off", "Low", "High"] }, "참/거짓·선택지 판별");

    Check(entries["Log"] is { Flags: true, IsText: true, Description: "로그" }, "플래그 열거형은 글 입력");
    entries["Log"].Value = "Warn, Nope";
    Check(cfg.Invalid().SequenceEqual(["Log: Warn, Nope"]), "플래그 잘못된 값 감지");
    entries["Log"].Value = "Info, Error";
    entries["TargetFps"].Value = "abc";
    Check(cfg.Invalid().SequenceEqual(["TargetFps: abc"]), "잘못된 숫자 감지");
    entries["TargetFps"].Value = "144";
    entries["MsaaOff"].BoolValue = false;
    entries["Ssao"].Value = "High";
    Check(cfg.Invalid().Count == 0 && cfg.IsChanged, "바뀐 값 확인");
    cfg.Save();
    var expected = bep.Replace("TargetFps = 0", "TargetFps = 144").Replace("MsaaOff = true", "MsaaOff = false").Replace("Ssao = Low", "Ssao = High").Replace("Log = Warn, Error", "Log = Info, Error");
    Check(File.ReadAllText(bepPath) == expected && !cfg.IsChanged, "바뀐 줄만 저장");

    var tomlPath = Path.Combine(cfgDir, "ml.cfg");
    const string toml = "[LXIUM_test]\n# 이름\nName = \"a \\\"b\\\"\"\nScale = 1.5\nList = [\n  1,\n]\nOn = false\n";
    File.WriteAllText(tomlPath, toml);
    var ml = ConfigFile.Load(tomlPath);
    var me = ml.Sections.SelectMany(s => s.Entries).ToDictionary(e => e.Key);
    Check(me.Keys.Order().SequenceEqual(["Name", "On", "Scale"]) && me["Name"] is { Value: "a \"b\"", Description: "이름" }, "TOML 읽기 (여러 줄 값 제외)");
    me["Name"].Value = "c\"d";
    me["On"].BoolValue = true;
    ml.Save();
    Check(File.ReadAllText(tomlPath) == toml.Replace("Name = \"a \\\"b\\\"\"", "Name = \"c\\\"d\"").Replace("On = false", "On = true"), "TOML 저장 (따옴표 유지)");
}
finally
{
    Directory.Delete(cfgDir, true);
}

// 애드온 모음: https·sha256 없는 항목은 빼고, 받은 것과 모음 버전을 합쳐 최신순으로
var manifestJson = """{"schema":1,"id":"LXIUM_test","name":"Test","version":"1.1.0","game":{"id":"test","name":"Test","exe":"Test.exe"},"variants":{"bepinex6":{"minLoaderVersion":"6.0.0-be.800"}}}""";
var hash = new string('a', 64);
var catJson = $$"""
{"schema":1,"name":"모음","addons":[
 {"url":"https://x/a.zip","sha256":"{{hash}}","manifest":{{manifestJson}}},
 {"url":"http://x/b.zip","sha256":"{{hash}}","manifest":{{manifestJson}}},
 {"url":"https://x/c.zip","sha256":"no","manifest":{{manifestJson}}}]}
""";
var catErrors = new List<string>();
var cat = Catalogs.Parse(catJson, "https://x/catalog.json", catErrors);
Check(cat is [{ Source: "모음" }] && catErrors.Count == 2, "모음 해석 (http·sha256 없는 항목 제외)");

// 모음 항목 받기 검사: sha256·lxium.json이 맞을 때만 addons/에 둠
var storeDir = Directory.CreateTempSubdirectory("lxium-store").FullName;
try
{
    var zipFile = Path.Combine(storeDir, "src.zip");
    MakeZip(zipFile, new() { ["lxium.json"] = manifestJson, ["bepinex6/BepInEx/plugins/LXIUM_test/a.dll"] = "x" });
    var good = cat[0] with { Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(zipFile))) };
    var addonsOut = Path.Combine(storeDir, "addons");
    await using (var s = File.OpenRead(zipFile))
        Check((await Catalogs.StoreAsync(s, good, addonsOut)).ZipPath.EndsWith("LXIUM_test-1.1.0.zip"), "모음 항목 저장");
    File.Delete(Path.Combine(addonsOut, "LXIUM_test-1.1.0.zip"));
    var badHash = false;
    await using (var s = File.OpenRead(zipFile))
        try { await Catalogs.StoreAsync(s, cat[0], addonsOut); } catch (InvalidDataException) { badHash = true; }
    var otherVersion = false;
    await using (var s = File.OpenRead(zipFile))
        try { await Catalogs.StoreAsync(s, good with { Manifest = good.Manifest with { Version = "9.9.9" } }, addonsOut); } catch (InvalidDataException) { otherVersion = true; }
    Check(badHash && otherVersion && !Directory.EnumerateFiles(addonsOut).Any(), "sha256·버전이 다르면 거부하고 남기지 않음");
}
finally
{
    Directory.Delete(storeDir, true);
}

var m100 = cat[0].Manifest with { Version = "1.0.0", Variants = new() { ["bepinex6"] = new(null) } };
var built = Game.Build([new Addon("local.zip", m100)], cat, []);
Check(built is [{ Packages: [{ Versions: [{ Version: "1.1.0", Local: null }, { Version: "1.0.0", Local: not null }] }] }], "받은 것·모음 버전 합치기 (최신순)");
Check(Game.Build([], cat, []).Count == 0, "설치 안 된 게임의 모음 항목은 라이브러리에서 뺌");
var schema3 = false;
try { Catalogs.ParseDoc("""{"schema":3,"name":"x","addons":[]}""", "https://x/c.json"); } catch (InvalidDataException) { schema3 = true; }
Check(schema3 && Catalogs.ParseDoc("""{"schema":2,"name":"","addons":[{"github":"a/b"}]}""", "https://host.example/c.json") is { Name: "host.example", Addons: [{ Github: "a/b" }] }, "모음 schema 2 읽기, 3 거부");

var pkg = built[0].Packages[0];
var bep788 = new LoaderInfo("bepinex6", "BepInEx 6", "6.0.0-be.788");
var iconCache = Path.Combine(Path.GetTempPath(), "lxium-icons");
var addonRow = new AddonRow(pkg, new InstallRecord("LXIUM_test", "1.0.0", "bepinex6", [], []), false, [bep788], iconCache);
Check(addonRow.IsAdded && addonRow.InstallLabel == "업데이트" && addonRow.Status.Contains("v1.1.0 있음") && addonRow.Compat!.Contains("6.0.0-be.800"), "업데이트·로더 버전 호환 표시");
addonRow.Selected = pkg.Versions[1];
Check(addonRow.InstallLabel == "재설치" && addonRow.Compat is null, "버전 고르기");
Check(new AddonRow(pkg, null, false, [new LoaderInfo("melonloader", "MelonLoader", "0.7.3")], iconCache).Compat!.Contains("맞지 않음"), "다른 로더와 호환 안 됨 표시");
var notAdded = new AddonRow(new AddonPackage([new AddonVersion(cat[0].Manifest, null, cat[0])]), null, false, [], iconCache);
Check(!notAdded.IsAdded && notAdded.InstallLabel == "추가" && notAdded.Status == "추가 안 함", "모음에만 있는 모드는 추가부터");
Check(notAdded.Matches("test", []) && notAdded.Matches("", []) && !notAdded.Matches("없는이름", []) && !notAdded.Matches("", ["fun"]), "검색·태그 거르기");

// 이름을 바꾼 애드온(replaces)은 같은 Steam 게임으로 묶고 옛 것을 숨김
var oldM = m100 with { Id = "LXIUM_old", Game = m100.Game with { Id = "old-id", SteamAppId = 42 } };
var newM = m100 with { Id = "LXIUM_new", Game = m100.Game with { Id = "new-id", SteamAppId = 42 }, Replaces = ["LXIUM_old"] };
var renamed = Game.Build([new Addon("a.zip", oldM), new Addon("b.zip", newM)], [], []);
Check(renamed is [{ Key: "42", Packages: [{ Id: "LXIUM_new" }] }], "이름 바꾼 애드온 묶기·옛 것 숨김");

// 로더 충돌
Check(Loaders.Conflict([bep788]) is null && Loaders.Conflict([bep788, new LoaderInfo("melonloader", "MelonLoader", "0.7.3")]) is not null
      && Loaders.Conflict([bep788, new LoaderInfo("bepinex5", "BepInEx 5", "5.4.23")])!.Contains("BepInEx 5와 6"), "로더 충돌 경고");

// 런처 릴리스: LXIUM-v 태그의 정식 릴리스 중 지금보다 새 최신
using var relDoc = System.Text.Json.JsonDocument.Parse("""
[{"tag_name":"LXIUM-v1.2.0","prerelease":false,"draft":false,"assets":[{"name":"LXIUM-1.2.0-portable.zip","browser_download_url":"https://x/1.2.0.zip"}]},
 {"tag_name":"LXIUM-v2.0.0","prerelease":true,"draft":false,"assets":[{"name":"LXIUM-2.0.0-portable.zip","browser_download_url":"https://x/2.zip"}]},
 {"tag_name":"LXIUM_mahosaba-v9.0.0","prerelease":false,"draft":false,"assets":[{"name":"LXIUM_mahosaba-9.0.0.zip","browser_download_url":"https://x/m.zip"}]},
 {"tag_name":"LXIUM-v1.1.0","prerelease":false,"draft":false,"assets":[{"name":"LXIUM-1.1.0-portable.zip","browser_download_url":"https://x/1.1.0.zip"}]}]
""");
Check(Updater.Pick(relDoc.RootElement, "1.0.0") is { Version: "1.2.0", ZipUrl: "https://x/1.2.0.zip" } && Updater.Pick(relDoc.RootElement, "1.2.0") is null, "런처 릴리스 고르기");

// 런처 설정: 없으면 기본값(공식 모음, 자동 확인), 저장 후 다시 읽기
var setDir = Directory.CreateTempSubdirectory("lxium-set").FullName;
try
{
    var st = AppSettings.Load(setDir);
    Check(st is { AutoUpdate: true, Catalogs: [AppSettings.OfficialCatalog] }, "런처 설정 기본값");
    st.AutoUpdate = false;
    st.Catalogs.Add("https://example.com/catalog.json");
    st.Save(setDir);
    Check(AppSettings.Load(setDir) is { AutoUpdate: false, Catalogs.Count: 2 }, "런처 설정 저장·읽기");
}
finally
{
    Directory.Delete(setDir, true);
}

// .lxconfig: 형식·기본값·범위·선택지·언어별 설명, 게임을 실행하지 않아도 설정 파일 만들기
const string lx = """
# 예시
Section FrameRate
Set TargetFps Int 0 Range -1 240
Lang TargetFps ko "fps 제한\n(-1은 제한 없음)"
Lang TargetFps en "FPS limit"
Name TargetFps ko "목표 fps"
Name FrameRate ko "프레임"
Section Urp
Set Ssao Enum Off Values Off|StageOnly|Game
Set MsaaOff Bool true
Set Label String "a b"
Oops line
""";
var lxc = LxConfig.Parse(lx);
Check(lxc.Settings.Count == 4 && lxc.Errors.Count == 1 && lxc.Find("TargetFps") is { Min: -1, Max: 240, Type: "Int" }
      && lxc.Find("Urp.Ssao").Values.SequenceEqual(["Off", "StageOnly", "Game"]) && lxc.Find("TargetFps").Descriptions["ko"] == "fps 제한\n(-1은 제한 없음)", ".lxconfig 읽기");
var lxDir = Directory.CreateTempSubdirectory("lxium-lx").FullName;
try
{
    var cfgPath = Path.Combine(lxDir, "BepInEx", "config", "LXIUM_test.cfg");
    var created = ConfigFile.Open(cfgPath, lxc, "ko")!;
    var ce = created.Sections.SelectMany(x => x.Entries).ToDictionary(e => e.Key);
    Check(created.IsNew && created.Sections[0].DisplayName == "프레임" && ce["TargetFps"] is { DisplayName: "목표 fps", IsSlider: true, Value: "0" }
          && ce["Ssao"].HasChoices && ce["MsaaOff"].IsBool, "설정 파일 없이 .lxconfig로 편집");
    ce["TargetFps"].Value = "144";
    created.Save();
    Check(File.ReadAllText(cfgPath) == "[FrameRate]\nTargetFps = 144\n\n[Urp]\nSsao = Off\nMsaaOff = true\nLabel = a b\n", "기본값으로 설정 파일 만들기");

    // 이미 있는 파일에 .lxconfig에만 있는 설정은 그 섹션 끝에 더함
    File.WriteAllText(cfgPath, "[FrameRate]\nTargetFps = 60\n");
    var merged = ConfigFile.Open(cfgPath, lxc, "en")!;
    Check(!merged.IsNew && merged.Sections.SelectMany(x => x.Entries).First(e => e.Key == "TargetFps") is { Value: "60", Description: "FPS limit" }, "있는 파일 + .lxconfig 설명 (언어별)");
    merged.Save();
    Check(File.ReadAllText(cfgPath) == "[FrameRate]\nTargetFps = 60\n\n[Urp]\nSsao = Off\nMsaaOff = true\nLabel = a b\n", "없는 설정 더하기");
    var tomlPath = Path.Combine(lxDir, "UserData", "LXIUM_test.cfg");
    ConfigFile.Open(tomlPath, lxc, "ko")!.Save();
    Check(File.ReadAllText(tomlPath).Contains("Ssao = \"Off\"") && File.ReadAllText(tomlPath).Contains("Label = \"a b\""), "MelonLoader(TOML)은 문자열 따옴표");
    Check(ConfigFile.Open(Path.Combine(lxDir, "none.cfg"), null, "ko") is null, ".lxconfig도 파일도 없으면 null");
}
finally
{
    Directory.Delete(lxDir, true);
}

// 외부 모드 다시 묶기: include 경로만, 변형 폴더 아래로
var repDir = Directory.CreateTempSubdirectory("lxium-rep").FullName;
try
{
    var srcZip = Path.Combine(repDir, "src.zip");
    MakeZip(srcZip, new()
    {
        ["BepInEx/core/BepInEx.Core.dll"] = "x", ["winhttp.dll"] = "x", ["BepInEx/config/Ext.cfg"] = "x",
        ["BepInEx/plugins/Ext/Ext.dll"] = "x", ["BepInEx/plugins/Ext/Lib.dll"] = "x",
    });
    var extM = cat[0].Manifest with { Id = "Ext", Version = "2.1.0" };
    var outZip = Path.Combine(repDir, "out.zip");
    Catalogs.Repackage(srcZip, outZip, extM, new() { ["bepinex6"] = ["BepInEx/plugins/Ext/"] });
    using (var z = ZipFile.OpenRead(outZip))
        Check(z.Entries.Select(e => e.FullName).Order().SequenceEqual(["bepinex6/BepInEx/plugins/Ext/Ext.dll", "bepinex6/BepInEx/plugins/Ext/Lib.dll", "lxium.json"])
              && Addon.Load(outZip).Manifest is { Id: "Ext", Version: "2.1.0" }, "외부 모드 다시 묶기 (로더·설정 제외)");
}
finally
{
    Directory.Delete(repDir, true);
}

// addons/에 직접 넣은 외부 형식: ManosabaLoader 배포 zip(BepInEx 통째), .msmod
var forDir = Directory.CreateTempSubdirectory("lxium-foreign").FullName;
try
{
    var addonsIn = Path.Combine(forDir, "addons");
    var cacheIn = Path.Combine(forDir, "cache");
    Directory.CreateDirectory(addonsIn);
    MakeZip(Path.Combine(addonsIn, "ManosabaLoader_KR_V2_1_0.zip"), new()
    {
        ["winhttp.dll"] = "x", ["BepInEx/core/BepInEx.Core.dll"] = "x", ["BepInEx/config/ManosabaLoader.cfg"] = "x",
        ["BepInEx/plugins/ManosabaLoader/ManosabaLoader.dll"] = "not a real dll", ["BepInEx/plugins/ManosabaLoader/NVorbis.dll"] = "x",
    });
    File.WriteAllText(Path.Combine(addonsIn, "테스트 팩.msmod"), "encrypted");
    File.WriteAllText(Path.Combine(addonsIn, "readme.txt"), "무시");
    MakeZip(Path.Combine(addonsIn, "unknown.zip"), new() { ["a.txt"] = "x" });

    var found = Addon.LoadAll(addonsIn, cacheIn).ToDictionary(a => a.Manifest.Id.StartsWith("msmod.") ? "msmod" : a.Manifest.Id);
    Check(found.Count == 2 && found["ManosabaLoader"].Manifest is { Version: "2.1.0", Game.SteamAppId: 3101040, Variants.Count: 1 }
          && found["msmod"].Manifest is { Name: "테스트 팩", Requires: ["ManosabaLoader"], Tags: ["msmod"] }, "ManosabaLoader zip·.msmod 알아보기 (모르는 파일 무시)");
    using (var z = ZipFile.OpenRead(found["ManosabaLoader"].ZipPath))
        Check(z.Entries.Select(e => e.FullName).Order().SequenceEqual(["bepinex6/BepInEx/plugins/ManosabaLoader/ManosabaLoader.dll", "bepinex6/BepInEx/plugins/ManosabaLoader/NVorbis.dll", "lxium.json"]),
              "ManosabaLoader는 플러그인 폴더만 (BepInEx·설정 제외)");
    Check(Addon.LoadAll(addonsIn, cacheIn).Count == 2 && Directory.EnumerateFiles(Path.Combine(cacheIn, "converted"), "*.zip").Count() == 2, "바꾼 zip 캐시 재사용");
    Check(Addon.TryRead(Path.Combine(addonsIn, "unknown.zip"), cacheIn) is null && Game.Build(found.Values, [], []) is [{ Key: "3101040", Packages.Count: 2 }], "모르는 zip 거부, 마녀재판으로 묶기");

    var gameDir = Path.Combine(forDir, "game");
    Directory.CreateDirectory(gameDir);
    Installer.Install(found["msmod"], "bepinex6", gameDir);
    Check(File.Exists(Path.Combine(gameDir, "ManosabaMod", "테스트 팩.msmod")), ".msmod는 ManosabaMod/에 설치");
    Installer.Uninstall(found["msmod"], gameDir);
    Check(!Directory.Exists(Path.Combine(gameDir, "ManosabaMod")), ".msmod 제거");
}
finally
{
    Directory.Delete(forDir, true);
}

// 실제 배포처에서 로더 받기 (네트워크 필요): dotnet run --project launcher/tests -- --net
if (args.Contains("--net"))
{
    var net = Directory.CreateTempSubdirectory("lxium-net").FullName;
    try
    {
        foreach (var (loader, il2cpp) in new[] { ("bepinex5", false), ("bepinex6", true), ("melonloader", false) })
        {
            var game = Path.Combine(net, loader);
            Directory.CreateDirectory(game);
            File.Copy(Environment.ProcessPath!, Path.Combine(game, "Game.exe"));
            if (il2cpp) File.WriteAllText(Path.Combine(game, "GameAssembly.dll"), "");
            var version = LoaderSetup.InstallAsync(loader, game, "Game.exe").GetAwaiter().GetResult();
            var found = Loaders.Detect(game).SingleOrDefault();
            Check(found?.Id == loader, $"{loader} {version} 설치·감지 ({found?.Version})");
            LoaderSetup.Uninstall(loader, game);
            Check(Loaders.Detect(game).Count == 0, $"{loader} 제거");
        }
    }
    finally
    {
        Directory.Delete(net, true);
    }
}

Console.WriteLine("모두 통과");

static void MakeZip(string path, Dictionary<string, string> files)
{
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var (name, text) in files)
    {
        using var s = zip.CreateEntry(name).Open();
        s.Write(Encoding.UTF8.GetBytes(text));
    }
}

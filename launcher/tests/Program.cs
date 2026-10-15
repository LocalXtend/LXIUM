// 런처 모델 자체 검사. 실행: dotnet run --project launcher/tests
using System.IO.Compression;
using System.Text;
using LXIUM.Models;

static void Check(bool ok, string what)
{
    if (!ok) throw new Exception("실패: " + what);
    Console.WriteLine("ok  " + what);
}

// 로더 버전 비교
Check(Loaders.Compare("6.0.0-be.785", "6.0.0-be.785") == 0, "같은 버전");
Check(Loaders.Compare("6.0.0-be.697", "6.0.0-be.785") < 0, "프리릴리스 번호 비교");
Check(Loaders.Compare("6.0.0", "6.0.0-be.785") > 0, "정식 > 프리릴리스");
Check(Loaders.Compare("5.4.23.2", "5.4.21") > 0, "숫자 자리 비교");
Check(Loaders.Compare("6.0.0-be.785+abc", "6.0.0-be.785") == 0, "빌드 메타 무시");

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
    Installer.Uninstall(root, "LXIUM_test");
    Check(File.ReadAllText(Path.Combine(root, "data.txt")) == "원본", "백업 복원");
    Check(!File.Exists(Path.Combine(root, "BepInEx/plugins/LXIUM_test/LXIUM_test.dll")), "모드 파일 삭제");
    Check(File.Exists(Path.Combine(root, "BepInEx/plugins/LXIUM_test/user.cfg")), "기록에 없는 설정 보존");
    Check(!Directory.Exists(Path.Combine(root, ".lxium", "backup")) && Installer.Read(root, "LXIUM_test") is null, "기록 정리");

    File.Delete(Path.Combine(root, "BepInEx/plugins/LXIUM_test/user.cfg"));
    Installer.Install(addon, "bepinex6", root);
    Installer.Uninstall(root, "LXIUM_test");
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

# LXIUM 런처 (AI용)

- 사람용 문서: `../human/launcher.md`
- 소스: `launcher/` (WPF, .NET 10, MVC). 버전은 저장소 루트 `ver.txt` (csproj가 읽음)
- 애드온 처리 = `addon.md`, 애드온 모음 = `catalog.md`, 화면 규칙 = `design.md`
- 1.0.0부터 최적화 모드 전용이 아닌 **게임 모드 런처** (BepInEx·MelonLoader 모드 설치·설정·모음 받기)

## 구조

| 폴더 | 역할 |
|---|---|
| `launcher/Models/` | 데이터와 로직. WPF 형식을 쓰지 않음 (테스트에서 그대로 부름) |
| `launcher/Controllers/LibraryController.cs` | 라이브러리·게임 상세: 게임 찾기, 모음 받기, 애드온 설치·제거·버전, 로더, 설정 파일 편집 |
| `launcher/Controllers/SettingsController.cs` | 런처 설정 화면: 시작 처리, 업데이트 확인·적용, 자동 확인, 모음 추가·빼기 |
| `launcher/Views/MainWindow` | 페이지 3개(`LibraryView` `DetailView` `SettingsView`)를 `ShowPage`로 전환. 표시 메서드만, 로직 금지 |
| `launcher/Views/ConfigWindow` | 설정 파일 편집 모달 창 |
| `launcher/Assets/` | Lucide 아이콘 라이선스 (빌드가 배포 폴더에 복사) |
| `launcher/tests/` | 모델 자체 검사 콘솔. `dotnet run --project launcher/tests` (`-- --net`이면 로더 실제 받기까지) |

| 모델 | 내용 |
|---|---|
| `Addon` | `addons/*.zip`의 `lxium.json` 읽기. `Addon.Validate(Manifest)` = `addon.md` 3장 검사 (zip·모음 공용) |
| `Catalog` (`Catalogs`) | `catalog.md`. 모음 받기·항목 검사, 항목 zip 받기 → sha256·id·version 확인 → `addons/<id>-<version>.zip` |
| `Game` | `AddonVersion`(Local zip 또는 Remote 모음 항목) → `AddonPackage`(같은 id, 최신순) → `Game`(같은 game.id). 라이브러리 = 받아 둔 애드온이 있거나 Steam에 설치된 게임 |
| `AddonRow` | 상세 화면 한 줄. `Selected` 버전을 바꾸면(INotifyPropertyChanged) 라벨·호환 표시 갱신. `Compat` = 지금 로더와 맞지 않는 이유 |
| `Steam` | 레지스트리의 Steam 경로 → `libraryfolders.vdf`의 모든 라이브러리. `appmanifest_<steamAppId>.acf`의 `installdir` 우선, 없으면 `steamapps/common/*/<game.exe>` |
| `Loaders` | `addon.md` 4장 감지 표. 버전 = 감지 DLL의 ProductVersion(`+` 뒤 제거) |
| `Versions` | 버전 비교 (숫자 → 정식 > 프리릴리스 → 프리릴리스 숫자). 로더·애드온·런처 공용 |
| `Installer` | 변형 선택·설치·제거. 기록 `<게임 루트>/.lxium/<id>.json`, 백업 `.lxium/backup/<id>/`. `Uninstall(root, id)`는 zip 없이 기록으로 제거 |
| `LoaderSetup` | 로더 설치·제거. BepInEx 5 = GitHub `BepInEx/BepInEx` `v5.*`, BepInEx 6 = `builds.bepinex.dev` 최신 BE, MelonLoader = GitHub `LavaGang/MelonLoader` 최신. 비트 = exe PE Machine, 백엔드 = `GameAssembly.dll` 유무. 제거 = `Footprint` 표 |
| `ConfigFile` | BepInEx .cfg·MelonLoader TOML 줄 단위 편집. 편집기 종류: Boolean → `Switch`, Acceptable values(플래그 아님) → 목록, Acceptable value range → 슬라이더 + 값, 그 밖 → 글. 저장 = 바뀐 줄만 |
| `SteamStore` | `store.steampowered.com/api/appdetails?appids=<id>&l=koreana`. 키 없음 |
| `AppSettings` | 실행 파일 옆 `settings.json`: `autoUpdate`(기본 true), `catalogs`(기본 공식 모음). 깨졌으면 기본값 |
| `Updater` | GitHub `LocalXtend/LXIUM` 릴리스 중 태그 `LXIUM-v<버전>`, 정식, `*-portable.zip` asset이 있는 최신. 적용 = zip을 앱 폴더에 풂(`settings.json` 제외), 실행 중 exe → `.old`로 이름 변경 후 새 exe, 다시 실행. 시작할 때 `.old` 삭제 |

## 흐름

- 시작(`SettingsController.StartAsync`): `.old` 정리 → 게임 목록(자동 확인이 켜져 있으면 모음도 받음) → 자동 확인이면 런처 릴리스 확인, 새 버전이면 머리글에 업데이트 버튼
- 새로 고침(머리글 아이콘): 게임 + 모음 다시 받기
- 설치: 고른 버전 → 로더 없으면 확인 후 로더 설치 → 변형 선택 → minLoaderVersion 경고 → (Remote면) 받기·검사 → 설치 → 목록 다시 만들기
- 로더 카드: 감지 없으면 로더별 설치 버튼, 감지되면 "로더 설정"·"선택한 로더 제거". 로더 제거 전에 그 변형으로 설치한 애드온을 먼저 제거
- 설정 파일: 애드온 줄 "설정" → `addon.md` 5장 6번 경로, 로더 설정 → `BepInEx.cfg` / `UserData/Loader.cfg`. 파일이 없으면 게임을 한 번 실행하라고 안내

## 업데이트해도 유지되는 것

| 무엇 | 어디 | 왜 유지되나 |
|---|---|---|
| 런처 설정 | `settings.json` (런처 폴더) | 업데이트가 이 파일을 건너뜀 |
| 받아 둔 애드온 | `addons/` | 업데이트는 덮어쓰기만 하고 지우지 않음 |
| 모드 설정 | 게임 폴더 `BepInEx/config/`, `UserData/` | 애드온은 설정 파일을 넣지 않고(addon.md 5장), 설치 기록에도 없어 제거·업데이트가 건드리지 않음 |
| 설치 기록·백업 | 게임 폴더 `.lxium/` | 런처 폴더와 따로 있음 |

## 실행 폴더

```
LXIUM.exe
Lucide-LICENSE.txt
settings.json          # 처음 설정을 바꿀 때 생김
addons/<id>-<version>.zip
```

## 빌드·배포

- `./build.ps1`: 단일 exe(win-x64, 자체 포함) → `dist/LXIUM-<ver>/` + 라이선스 + `addons/`(각 모드 `ver.txt` 버전 zip) → `dist/LXIUM-<ver>-portable.zip`, 공식 모음 `dist/catalog.json`
- 런처 릴리스: 태그 `LXIUM-v<ver>`, asset `LXIUM-<ver>-portable.zip`, 본문 형식은 저장소 `AGENTS.md` "git 릴리즈에 관해서"
- 모드 릴리스·공식 모음 갱신: `catalog.md` 4장
- 저장소가 비공개거나 릴리스가 없으면 업데이트 확인·공식 모음은 실패 메시지만 보이고 나머지 기능은 그대로 동작

## Windows 10

- 글꼴은 Segoe UI로 대체, 아이콘은 경로(Geometry)라 OS와 무관. Win11 전용 API를 쓰지 않음 (`design.md` 8장)
- .NET 10 공식 지원 목록의 Windows 10은 LTSC/IoT 21H2·1809·1607뿐. 일반 22H2는 지원 종료로 목록에 없음 (자체 포함 빌드라 보통 실행됨, 실기 확인 필요)

## 아직 없는 것

- 수동 게임 폴더 지정 (Steam 게임만)
- 모음 항목 서명 (지금은 https + sha256)
- 런처 업데이트 전 변경 내용 보기

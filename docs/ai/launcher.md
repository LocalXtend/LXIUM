# LXIUM 런처 (AI용)

- 사람용 문서: `../human/launcher.md`
- 소스: `launcher/` (WPF, .NET 10, MVC). 버전은 저장소 루트 `ver.txt` (csproj가 읽음)
- 애드온 = `addon.md`, 애드온 모음 = `catalog.md`, 설정 설명 = `lxconfig.md`, 화면 규칙 = `design.md`
- 게임 모드 런처: BepInEx·MelonLoader 모드 추가·설치·업데이트·제거, 로더 설치·업데이트·제거, 모드·로더 설정 편집

## 구조

| 위치 | 역할 |
|---|---|
| `launcher/Models/` | 데이터와 로직. WPF 형식을 쓰지 않음 (테스트에서 그대로 부름) |
| `launcher/Controllers/LibraryController.cs` | 라이브러리·게임 설정·모드 패널: 게임 찾기, 모음 받기, 추가·설치·제거·버전, requires, 로더, 설정 파일 편집 |
| `launcher/Controllers/SettingsController.cs` | 시작 처리(언어 선택 → 시작), 런처 설정 화면: 언어, 업데이트 확인·적용, 자동 확인, 모음 추가·빼기·기본값 |
| `launcher/Views/MainWindow` | 머리글 + 페이지 전환(`ShowPage`, 0.3초 나타남) + 덮개(`BusyOverlay`, `LanguagePage`). 표시 메서드만, 로직 금지 |
| `launcher/Views/Pages/` | `LibraryPage`(카드·세 칸), `GamePage`(게임 설정 패널), `ModsPage`(모드 패널), `SettingsPage`(런처 설정), `LanguagePage`(처음 실행) |
| `launcher/Views/BusyOverlay` | 오래 걸리는 작업 덮개. `MainWindow.StartBusy(문구)` → `BusyScope`(IProgress&lt;double&gt;, Dispose로 닫힘) |
| `launcher/Views/Motion.cs`, `T.cs` | 공용 움직임(easeInOutCubic 0.3초), XAML 문구 `{l:T 키}` |
| `launcher/lang/*.json` | 문구. exe 옆 `lang/`에 복사(단일 exe에 넣지 않음). `ko.json`이 기준 |
| `launcher/Assets/` | Lucide 아이콘 라이선스 |
| `launcher/tests/` | 모델 자체 검사. `dotnet run --project launcher/tests` (`-- --net`이면 로더 실제 받기까지) |

| 모델 | 내용 |
|---|---|
| `Loc` | 언어. `Loc.T(키, 인자…)`, `Loc.I[키]`, `Load(appDir, code)`가 `Item[]` 변경 알림 → 화면 문구 바로 바뀜. 키가 없으면 ko, 그래도 없으면 키 |
| `Addon` | zip의 `lxium.json` 읽기·검증(`Validate`, 태그 거르기), `addon.png` 꺼내기(`ExtractIcon` → `cache/icons`), `<id>.lxconfig` 읽기 |
| `Catalog` (`Catalogs`) | `catalog.md`. 고정·GitHub·외부 항목 해석, GitHub 릴리스 ETag 캐시(`cache/github`), 받기 → sha256 → (외부면 `Repackage`) → id·version 확인 |
| `Game` | `AddonVersion`(Local 또는 Remote) → `AddonPackage`(같은 id, 최신순, `IsAdded`) → `Game`(Steam 앱 ID, 없으면 game.id로 묶음, replaces로 대신된 패키지 숨김) |
| `AddonRow` | 모드 패널 한 줄. `IsAdded`(받아 둠 또는 설치됨), `Selected` 버전, `InstallLabel`(추가·설치·업데이트·이 버전으로·재설치), `Compat`, `Icon`, `TagNames`, `Matches(검색, 태그)` |
| `GameDetail` | 게임 설정·모드 패널 공용 상태. `LoaderConflict`, `InstallableLoaders`, `ModSummary` |
| `Steam` | 레지스트리 → `libraryfolders.vdf` → `appmanifest_<id>.acf`의 `installdir`, 없으면 `steamapps/common/*/<exe>` |
| `Loaders` | 감지 표, `Name(id)`, `Conflict(감지 목록)` (BepInEx+MelonLoader, BepInEx 5+6) |
| `Versions` | 버전 비교 (숫자 → 정식 > 프리릴리스 → 프리릴리스 숫자) |
| `Installer` | 변형 선택·설치·제거. `replaces` 처리(옛 id 제거, 설정 파일 복사). 기록 `.lxium/<id>.json`, 백업 `.lxium/backup/<id>/` |
| `LoaderSetup` | 로더 설치(덮어쓰기 = 업데이트)·`LatestAsync`·제거(`Footprint`). 받는 곳은 BepInEx/BE 빌드/MelonLoader 공식 |
| `LxConfig` | `.lxconfig` 해석, 기본값 설정 파일 만들기 |
| `ConfigFile` | 설정 파일 줄 단위 편집. `Open(path, lx, lang)`: 파일 + .lxconfig 정보, 파일이 없으면 새로 만들 준비(`IsNew`), 없는 설정은 저장 때 섹션 끝에 더함 |
| `SteamStore` | appdetails, 런처 언어에 맞는 Steam 언어 |
| `Net` | 공용 HttpClient(User-Agent), 진행률 받기 |
| `AppSettings` | `settings.json`: `autoUpdate`, `catalogs`, `language`. `ResetCatalogs()` |
| `Updater` | `LXIUM-v*` 릴리스의 portable zip으로 자체 업데이트 (`settings.json` 제외, exe는 `.old`로 바꿔 교체) |

## 흐름

- 시작: `settings.language`가 없거나 lang에 없으면 `LanguagePage` → 고르면 그 언어로 미리 보기 → "다음"에서 저장 → `.old` 정리 → 게임 목록(자동 확인이면 모음도) → 자동 확인이면 런처 릴리스 확인
- 라이브러리 카드: 표지 클릭 → 세 칸(설정·모드·플레이). 카드에서 마우스가 나가거나 표지 밖을 누르면 닫힘
  - 설정 → `GamePage` (게임 정보, 폴더·실행, 로더 설치·업데이트·제거·설정, 충돌 경고, 모드 패널 바로 가기)
  - 모드 → `ModsPage` (검색·태그, 추가한 모드 / 모드 추가하기, 충돌 경고, 게임 설정 패널로)
  - 플레이 → `steam://rungameid/<id>`
- 모드 주 버튼: 추가 안 함 → 받기만(`addons/`). 추가함 → requires → 로더(없으면 확인 후 설치) → 변형 → minLoaderVersion 경고 → (Remote면) 받기 → 설치(백그라운드)
- 오래 걸리는 작업(받기·설치·제거·로더·런처 업데이트)은 모두 `StartBusy`로 덮개 + 진행률, 파일 작업은 `Task.Run`
- 모드 설정: 설치한 버전 zip(없으면 아무 받아 둔 zip)의 `.lxconfig` + `addon.md` 5장 6번 경로 → `ConfigWindow`

## 문구 (다국어)

- 화면 글은 코드·XAML에 직접 쓰지 않음. XAML = `{l:T 키}`, 코드 = `Loc.T("키", 인자…)`
- 새 키는 `lang/ko.json`에 먼저, 같은 커밋에서 `en` `ja` `zh-Hans` `zh-Hant`에도. 자리 `{0}` `{1}`은 모든 언어에서 같게
- 언어를 더하려면 `lang/<코드>.json`(`_name` = 그 언어로 쓴 언어 이름)을 두면 언어 목록에 나옴. 코드가 Steam 언어와 이어지려면 `SteamStore.SteamLanguage`에도 더함
- 언어 선택 화면의 "Select language"는 언어를 고르기 전이라 영어 고정

## 업데이트해도 유지되는 것

| 무엇 | 어디 | 왜 유지되나 |
|---|---|---|
| 런처 설정 | `settings.json` (런처 폴더) | 업데이트가 이 파일을 건너뜀 |
| 받아 둔 애드온 | `addons/` | 업데이트는 덮어쓰기만 하고 지우지 않음 |
| 캐시 | `cache/` | 위와 같음. 지워도 다시 받음 |
| 모드 설정 | 게임 폴더 `BepInEx/config/`, `UserData/` | 애드온은 설정 파일을 넣지 않고 설치 기록에도 없어 제거·업데이트가 건드리지 않음 |
| 설치 기록·백업 | 게임 폴더 `.lxium/` | 런처 폴더와 따로 있음 |

## 실행 폴더

```
LXIUM.exe
Lucide-LICENSE.txt
lang/<코드>.json
settings.json          # 언어를 고르면 생김
addons/<id>-<version>.zip   # 모드 패널에서 추가한 애드온
cache/                 # GitHub 릴리스·아이콘 캐시
```

## 빌드·배포

- `./build.ps1`: 단일 exe(win-x64, 자체 포함) + `lang/` + 라이선스 → `dist/LXIUM-<ver>-portable.zip`. 모드는 넣지 않음 (모음으로 받음)
- 런처 릴리스: 태그·제목 `LXIUM-v<ver>`, asset `LXIUM-<ver>-portable.zip`, 본문은 `AGENTS.md` "git 릴리즈에 관해서"
- 공식 모음(`catalog.json`)은 저장소 목록이라 모드 릴리스 때 고치지 않음 (`catalog.md` 4장)

## Windows 10

- 글꼴은 Segoe UI로 대체, 아이콘은 경로(Geometry)라 OS와 무관. Win11 전용 API를 쓰지 않음 (`design.md` 8장)
- .NET 10 공식 지원 목록의 Windows 10은 LTSC/IoT 21H2·1809·1607뿐. 일반 22H2는 지원 종료로 목록에 없음 (자체 포함 빌드라 보통 실행됨, 실기 확인 필요)

## 아직 없는 것

- 수동 게임 폴더 지정 (Steam 게임만)
- 모음 항목 서명 (지금은 https + sha256)
- `.msmod` 같은 콘텐츠 팩을 모음으로 받기 (지금은 ManosabaLoader 자체만)

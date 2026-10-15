# LXIUM 런처 (AI용)

- 사람용 문서: `../human/launcher.md`
- 소스: `launcher/` (WPF, .NET 10, MVC). 버전은 저장소 루트 `ver.txt`
- 애드온 처리는 `addon.md` 6장을 따름

## 구조

| 폴더 | 역할 |
|---|---|
| `launcher/Models/` | 데이터와 로직. WPF 형식을 쓰지 않음 |
| `launcher/Controllers/LibraryController.cs` | 뷰 이벤트 → 모델 호출 → 뷰 갱신 |
| `launcher/Views/` | XAML과 표시 메서드(`ShowLibrary`, `ShowDetail`, `ShowInfo`, `Alert`, `Confirm`). 로직 금지 |
| `launcher/tests/` | 모델 자체 검사 콘솔. `dotnet run --project launcher/tests` |

| 모델 | 내용 |
|---|---|
| `Addon` | `addons/*.zip`의 `lxium.json` 읽기·검증. 실패한 zip은 건너뜀 |
| `Steam` | 레지스트리의 Steam 경로 → `libraryfolders.vdf`의 모든 라이브러리. `steamAppId`의 `appmanifest_<id>.acf` `installdir` 우선, 없으면 모든 라이브러리 `steamapps/common/*/<game.exe>` 탐색 |
| `Game` | `game.id`로 애드온을 묶은 게임. 라이브러리 = 애드온이 있는 게임만 |
| `Loaders` | `addon.md` 4장 감지 표. 버전 = 감지 DLL의 ProductVersion(`+` 뒤 제거). 새 로더는 규격 표 → `Loaders.Table` 순서로 추가 |
| `Installer` | 변형 선택·설치·제거. 기록 `<게임 루트>/.lxium/<id>.json`, 백업 `.lxium/backup/<id>/`. zip 경로가 루트 밖이면 거부 |
| `LoaderSetup` | 로더 설치·제거. 받는 곳: BepInEx 5 = GitHub `BepInEx/BepInEx` 정식 릴리스 중 `v5.*`의 `BepInEx_win_<x64/x86>_*.zip`, BepInEx 6 = `builds.bepinex.dev/projects/bepinex_be` 첫(최신) `BepInEx-Unity.<IL2CPP/Mono>-win-<arch>-6.0.0-be.N*.zip`, MelonLoader = GitHub `LavaGang/MelonLoader` 최신 정식 `MelonLoader.<x64/x86>.zip`. 비트 = 게임 exe PE Machine, 백엔드 = `GameAssembly.dll` 유무. 세 zip 모두 게임 루트에 그대로 풂. 제거 = `Footprint` 표의 파일·폴더 삭제(폴더 안 다른 모드·설정 포함, `changelog.txt`는 내용에 BepInEx가 있을 때만) |
| `SteamStore` | `store.steampowered.com/api/appdetails?appids=<steamAppId>&l=koreana`. 키 없음. 설명·장르·출시일·개발·배급. 한국어가 없으면 Steam이 영어로 돌려줌. `steamAppId`가 없으면 정보 없음 |

## 로더 흐름

- 로더 없음 + 애드온이 로더 필요 → 확인 후 변형 중 첫 번째로 설치 가능한 로더 설치 → 애드온 설치 (`addon.md` 6장 5단계)
- 로더 카드: 감지 없으면 애드온 변형의 로더별 설치 버튼, 감지되면 선택한 로더 제거 버튼
- 로더 제거 전에 그 로더 변형으로 설치한 LXIUM 애드온을 먼저 제거(백업 복원)
- 네트워크 검사: `dotnet run --project launcher/tests -- --net` (임시 폴더에 세 로더 설치·감지·제거)

## 실행 폴더

```
LXIUM.exe
addons/<id>-<version>.zip
```

- Steam 상점 API는 공식 문서가 없음. 게임을 열 때 한 번만 호출 (과다 호출 시 차단)
- 검토 후 쓰지 않는 정보원: IGDB·MobyGames(키 필요), SteamDB(API 없음·스크래핑 금지), VGDB(API 없음)

## 빌드

- `./build.ps1`: 자체 포함 단일 exe(win-x64) 게시 → `dist/LXIUM-<ver>/`, `optimizeMods/<id>/dist/<id>-<그 프로젝트 ver.txt>.zip`을 `addons/`에 복사 → `dist/LXIUM-<ver>-portable.zip`
- 포터블 배포만 있음. 설치 프로그램 없음
- zip이 없는 모드는 경고 후 건너뜀. 먼저 각 모드 프로젝트를 빌드

## 아직 없는 것

- 수동 게임 폴더 지정 (Steam 게임만 지원)

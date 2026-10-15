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
| `SteamStore` | `store.steampowered.com/api/appdetails?appids=<steamAppId>&l=koreana`. 키 없음. 설명·장르·출시일·개발·배급. 한국어가 없으면 Steam이 영어로 돌려줌. `steamAppId`가 없으면 정보 없음 |

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

- 로더 자동 설치 (`addon.md` 6장 설치 5단계). 지금은 로더가 없으면 필요한 로더 이름을 알리고 설치 거부
- 수동 게임 폴더 지정 (Steam 게임만 지원)

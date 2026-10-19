# LXIUM

> **AI를 사용하여 만들어진 프로젝트입니다.**
> 
> **문제가 생긴다면 Issues에 등록해주세요.**

게임 모드 런처입니다. BepInEx·MelonLoader 모드(최적화 모드 포함)를 게임에 설치·제거하고, 모드 설정을 바꾸고, 애드온 모음에서 받아 업데이트합니다.

## 버전
> 현재: `1.2.0`

- 2026 ⓒ iyu.e — https://iyu-e.tistory.com/

## 개발하기

### 준비물

- Windows 10/11 x64
- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- PowerShell (빌드 스크립트)
- 시험할 Steam 게임 (런처는 Steam 라이브러리에서 게임을 찾습니다)

### 저장소 구조

| 위치 | 내용 |
|---|---|
| `launcher/` | 런처 (WPF, MVC: `Models/` `Views/` `Controllers/`) |
| `launcher/lang/` | 런처 문구 (ko 기준, en·ja·zh-Hans·zh-Hant). 새 문구는 모든 언어에 같이 넣음 |
| `launcher/tests/` | 런처 모델 자체 검사 |
| `optimizeMods/LXIUM_<게임>/` | 게임별 모드 저장소의 clone 자리 (git 제외). 모드는 [LXIUM_manosaba](https://github.com/LocalXtend/LXIUM_manosaba)처럼 저장소를 따로 두고, 새 게임은 [LXIUM_sample](https://github.com/LocalXtend/LXIUM_sample)을 복사 |
| `catalog.json` | 공식 애드온 모음. 모드 저장소 목록이라 런처가 각 저장소의 릴리스를 직접 읽음 |
| `docs/human/`, `docs/ai/` | 사람용·AI용 문서. 같은 주제는 같은 파일 이름 |
| `build.ps1` | 포터블 빌드 (런처만, 모드는 모음으로 받음) |
| `AGENTS.md` | AI 에이전트 작업 규칙 (버전·커밋·릴리스 형식) |

### 자주 쓰는 명령

```
dotnet run --project launcher               # 런처 실행 (처음 실행이면 언어 선택부터)
dotnet run --project launcher/tests         # 자체 검사
dotnet run --project launcher/tests -- --net  # + BepInEx·MelonLoader 실제 받기 검사
./build.ps1                                 # dist/LXIUM-<버전>-portable.zip
```

### 문서

| 주제 | 사람용 | AI용 |
|---|---|---|
| 런처 사용·구조 | [launcher](docs/human/launcher.md) | [launcher](docs/ai/launcher.md) |
| 애드온(모드 zip) 규격 | [addon](docs/human/addon.md) | [addon](docs/ai/addon.md) |
| 애드온 모음(catalog.json) | [catalog](docs/human/catalog.md) | [catalog](docs/ai/catalog.md) |
| 설정 설명(.lxconfig) | [lxconfig](docs/human/lxconfig.md) | [lxconfig](docs/ai/lxconfig.md) |

### 릴리스

- 모드: 모드 저장소(`LocalXtend/<id>`)의 GitHub 릴리스 `<id>-v<버전>`에 `dist/<id>-<버전>.zip`을 올립니다. 공식 모음이 저장소를 가리키므로 `catalog.json`은 고치지 않습니다. ([catalog](docs/human/catalog.md))

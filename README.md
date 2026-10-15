# LXIUM

> **AI를 사용하여 만들어진 프로젝트입니다.**
> 
> **문제가 생긴다면 Issues에 등록해주세요.**

게임 모드 런처입니다. BepInEx·MelonLoader 모드(최적화 모드 포함)를 게임에 설치·제거하고, 모드 설정을 바꾸고, 애드온 모음에서 받아 업데이트합니다.

## 버전
> 현재: `1.0.0`

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
| `launcher/tests/` | 런처 모델 자체 검사 |
| `optimizeMods/LXIUM_<게임>/` | 게임별 모드(애드온) 프로젝트. 새 게임은 `LXIUM_sample/`을 복사 |
| `docs/human/`, `docs/ai/` | 사람용·AI용 문서. 같은 주제는 같은 파일 이름 |
| `build.ps1` | 포터블 빌드 + 공식 애드온 모음 생성 |
| `AGENTS.md` | AI 에이전트 작업 규칙 (버전·커밋·릴리스 형식) |

### 자주 쓰는 명령

```
dotnet run --project launcher               # 런처 실행 (addons는 launcher/bin/.../addons 폴더)
dotnet run --project launcher/tests         # 자체 검사
dotnet run --project launcher/tests -- --net  # + BepInEx·MelonLoader 실제 받기 검사
./build.ps1                                 # dist/LXIUM-<버전>-portable.zip, dist/catalog.json
```

### 문서

| 주제 | 사람용 | AI용 |
|---|---|---|
| 런처 사용·구조 | [launcher](docs/human/launcher.md) | [launcher](docs/ai/launcher.md) |
| 애드온(모드 zip) 규격 | [addon](docs/human/addon.md) | [addon](docs/ai/addon.md) |
| 애드온 모음(catalog.json) | [catalog](docs/human/catalog.md) | [catalog](docs/ai/catalog.md) |
| 디자인 규칙과 이유 | [design](docs/human/design.md) | [design](docs/ai/design.md) |

### 작업 순서

1. `dev` 브랜치에서 작업합니다.
2. 바꾼 기능의 문서를 사람용·AI용 둘 다 같은 커밋에서 고칩니다.
3. `ver.txt`를 올립니다. (x = 방향 전환, y = 새 기능, z = 오류 수정·동작 변경)
4. `dotnet run --project launcher/tests`가 통과하는지 확인하고 커밋합니다.

### 릴리스

- 런처: `./build.ps1` → GitHub 릴리스 `LXIUM-v<버전>`에 `LXIUM-<버전>-portable.zip`을 올립니다. 런처의 자동 업데이트가 이 릴리스를 찾습니다. 본문 형식은 `AGENTS.md`의 "git 릴리즈에 관해서"를 따릅니다.
- 모드: GitHub 릴리스 `<id>-v<버전>`에 `build.ps1`이 모은 zip을 올리고, `dist/catalog.json`을 저장소 루트 `catalog.json`에 반영합니다. ([catalog](docs/human/catalog.md))

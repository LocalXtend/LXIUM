# LXIUM 설정 설명 파일 .lxconfig (AI용)

- 사람용 문서: `../human/lxconfig.md`
- 런처 구현: `launcher/Models/LxConfig.cs`(읽기·새 설정 파일 만들기), `ConfigFile.Open/Apply/Save`(편집)
- 쓰는 곳: 애드온 zip 루트·모드 저장소 루트의 `<id>.lxconfig` (`addon.md` 2장)

## 1. 왜

- 설정 파일(.cfg)에는 주석을 달지 않음 → 설명을 한 언어로 박아 넣지 않고, 여기서 언어별로 관리
- 형식·기본값을 런처가 알면 게임을 한 번도 실행하지 않아도 설정 파일을 만들고 편집할 수 있음
- 범위·선택지가 있으면 런처가 슬라이더·목록으로 보여 주고 잘못된 값을 저장 전에 막음

## 2. 문법

UTF-8 텍스트, 한 줄에 지시어 하나. 빈 줄과 `#`로 시작하는 줄은 무시. 공백으로 나누고, 공백이 들어간 값은 `"..."`로 감쌈 (`\n` 줄바꿈, `\"`, `\\`).

| 지시어 | 모양 | 뜻 |
|---|---|---|
| `Section` | `Section <섹션>` | 이후 `Set`이 들어갈 설정 파일 섹션 (`[섹션]`) |
| `Set` | `Set <키> <형식> <기본값> [Range <최소> <최대>] [Values <a\|b\|c>]` | 설정 하나. 형식 = `Bool` `Int` `Float` `String` `Enum`. `Enum`은 `Values` 필수 |
| `Lang` | `Lang <키> <언어> "<설명>"` | 설명. 언어 = lang 코드 (`ko` `en` `ja` `zh-Hans` `zh-Hant`) |
| `Name` | `Name <키 또는 섹션> <언어> "<표시 이름>"` | 화면에 보일 이름. 설정 키가 아니면 섹션 이름으로 봄 |

- `<키>`는 `키` 또는 `섹션.키`. 그냥 `키`는 그 이름의 가장 최근 `Set` (보통 바로 위). 다른 섹션에 같은 키가 있으면 `섹션.키`로 적음
- 기본값·범위 숫자는 `.` 소수점 (InvariantCulture)
- 잘못된 줄은 그 줄만 무시하고 `LxConfig.Errors`에 남김
- 언어 고르기: 런처 언어 → `en` → `ko` → 아무거나

```
Section FrameRate
Name FrameRate ko "프레임"
Set TargetFps Int 0 Range -1 360
Name TargetFps ko "목표 fps"
Lang TargetFps ko "fps 제한을 바꿉니다.\n(-1은 제한 없음)"
Lang TargetFps en "Changes the FPS cap.\n(-1 = unlimited)"

Section Urp
Set Ssao Enum Off Values Off|StageOnly|Game
Set MsaaOff Bool true
```

## 3. 런처 동작

| 상황 | 동작 |
|---|---|
| 설정 파일 있음 | 파일 값을 쓰고, 형식·기본값·범위·선택지·설명·표시 이름은 .lxconfig에서 |
| 설정 파일 없음 | .lxconfig 기본값으로 화면을 채우고 "새로 만듦" 안내. 저장하면 파일 생성 (BepInEx = `키 = 값`, MelonLoader TOML = String·Enum을 따옴표로) |
| .lxconfig에 있고 파일에 없는 설정 | 기본값으로 보여 주고, 저장할 때 그 섹션 끝(섹션이 없으면 파일 끝)에 더함 |
| 파일에 있고 .lxconfig에 없는 설정 | 파일 주석(BepInEx `# Setting type` 등)으로 보여 줌 |
| 편집기 | Bool = 활성화 버튼, Enum = 목록, Range 있는 Int·Float = 슬라이더 + 값, 그 밖 = 글 |
| 저장 전 검사 | 형식(정수·실수), Range, Enum 값. 틀리면 알리고 창을 다시 엶 |

## 4. 모드 쪽 규칙

- 코드의 설정 등록(BepInEx `Config.Bind(섹션, 키, 기본값)`, MelonLoader `CreateEntry`)과 .lxconfig의 섹션·키·형식·기본값이 같아야 함. 코드를 바꾸면 같은 커밋에서 .lxconfig도 고침
- 설명 문자열은 코드에 두지 않음 (Bind의 description 비움)
- 최소 `ko`와 `en` 설명·이름을 둠
- BepInEx 열거형은 `Enum` + `Values`에 enum 이름 그대로
- 예시: `LocalXtend/LXIUM_manosaba`의 `LXIUM_manosaba.lxconfig`, 템플릿: `LocalXtend/LXIUM_sample`의 `LXIUM_sample.lxconfig`

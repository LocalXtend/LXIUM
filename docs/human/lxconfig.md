# 설정 설명 파일 (.lxconfig)

모드 설정의 **형식·기본값·설명**을 적는 파일입니다. 모드 저장소 루트에 `<모드 id>.lxconfig`로 두면 빌드가 애드온 zip에 넣고, 런처가 읽습니다.

> 정확한 규칙은 AI용 문서 [`../ai/lxconfig.md`](../ai/lxconfig.md)에 있습니다. 두 문서는 항상 같이 고칩니다.

## 왜 따로 두나요?

- **여러 언어로 설명할 수 있습니다.** 설정 파일(.cfg)에 설명을 주석으로 넣으면 한 언어밖에 못 씁니다. 그래서 설정 파일에는 주석을 달지 않고, 설명은 여기에 언어별로 적습니다.
- **게임을 실행하지 않아도 설정을 바꿀 수 있습니다.** 런처가 형식과 기본값을 알기 때문에, 설정 파일이 아직 없어도 기본값으로 만들어 줍니다.
- **알맞은 편집기가 나옵니다.** 켬/끔은 버튼, 정해진 선택지는 목록, 범위가 있는 숫자는 슬라이더로 보여 주고, 잘못된 값은 저장 전에 막습니다.

## 쓰는 법

한 줄에 하나씩 적습니다. `#`로 시작하는 줄은 메모입니다.

```
# 섹션 = 설정 파일의 [FrameRate]
Section FrameRate
Name FrameRate ko "프레임"

# 설정: 키, 형식, 기본값, (범위)
Set TargetFps Int 0 Range -1 360
Name TargetFps ko "목표 fps"
Name TargetFps en "Target FPS"
Lang TargetFps ko "fps 제한을 바꿀 수 있습니다.\n(-1은 프레임 무제한)"
Lang TargetFps en "Changes the FPS cap.\n(-1 = unlimited)"

Section Urp
Set Ssao Enum Off Values Off|StageOnly|Game
Set MsaaOff Bool true
```

| 줄 | 뜻 |
|---|---|
| `Section 이름` | 아래 설정들이 들어갈 설정 파일 섹션 |
| `Set 키 형식 기본값` | 설정 하나. 형식은 `Bool`(켬/끔) `Int`(정수) `Float`(소수) `String`(글) `Enum`(선택지) |
| `Range 최소 최대` | `Set` 뒤에 붙이면 범위. 런처가 슬라이더로 보여 줌 |
| `Values a\|b\|c` | `Enum`의 선택지 |
| `Lang 키 언어 "설명"` | 그 언어의 설명. 언어는 `ko` `en` `ja` `zh-Hans` `zh-Hant` |
| `Name 키 언어 "이름"` | 그 언어로 보일 이름. 키 대신 섹션 이름을 쓰면 섹션 제목 |

- 다른 섹션에 같은 키가 있으면 `섹션.키`로 적습니다. (예: `Stage.Resolution`)
- 설명에 줄을 바꾸려면 `\n`을 씁니다.
- 런처 언어의 설명이 없으면 영어, 그것도 없으면 한국어를 보여 줍니다. **최소 한국어와 영어는 적어 주세요.**

## 지킬 것

- 코드에서 등록한 설정(섹션·키·기본값)과 똑같이 적습니다. 코드를 바꾸면 이 파일도 같이 고칩니다.
- 코드의 설정 등록에는 설명을 넣지 않습니다. (`Config.Bind("FrameRate", "TargetFps", 0)`)

예시: [LXIUM_manosaba.lxconfig](https://github.com/LocalXtend/LXIUM_manosaba/blob/dev/LXIUM_manosaba.lxconfig)

# LXIUM 애드온 규격 (AI용)

- schema: `1`
- 사람용 문서: `../human/addon.md`
- 이 문서는 규범입니다. "해야 함 / 금지"는 예외 없이 지킵니다.
- 규격에 없는 필드·폴더를 임의로 추가하지 않습니다. 필요하면 이 문서 수정을 먼저 제안합니다.

## 1. 용어

| 용어 | 뜻 |
|---|---|
| 애드온 | 런처가 설치·제거하는 배포 단위. zip 파일 하나 |
| 로더 | 게임에 모드를 불러오는 프레임워크 (BepInEx, MelonLoader 등) |
| 변형 | 로더 하나에 맞춘 애드온 파일 묶음. 애드온은 변형을 하나 이상 가짐 |
| 게임 루트 | 게임 실행 파일이 있는 폴더 |

## 2. 패키지

- 파일명: `<id>-<version>.zip`
- zip 루트에는 아래만 있어야 함
  - `lxium.json`
  - `variants`의 키마다 같은 이름의 폴더 하나 (변형 폴더)
- 변형 폴더 안의 구조 = 게임 루트에 그대로 복사될 구조
- 경로는 `/` 구분, 상대 경로만. 절대 경로·`..`·드라이브 문자 금지

```
LXIUM_sephiria-1.0.0.zip
├─ lxium.json
├─ bepinex5/
│  └─ BepInEx/plugins/LXIUM_sephiria/LXIUM_sephiria.dll
└─ melonloader/
   └─ Mods/LXIUM_sephiria.dll
```

## 3. lxium.json

UTF-8 JSON. 주석 금지.

```json
{
  "schema": 1,
  "id": "LXIUM_sephiria",
  "name": "LXIUM Sephiria",
  "version": "1.0.0",
  "description": "세피리아 최적화",
  "game": {
    "id": "sephiria",
    "name": "Sephiria",
    "exe": "Sephiria.exe"
  },
  "variants": {
    "bepinex5": { "minLoaderVersion": "5.4.21" },
    "melonloader": {}
  }
}
```

| 필드 | 타입 | 필수 | 규칙 |
|---|---|---|---|
| `schema` | int | O | `1` |
| `id` | string | O | `^[A-Za-z0-9_.-]+$`. 소스 프로젝트 폴더명과 같음 (`LXIUM_<게임>`). 한 번 정하면 바꾸지 않음 |
| `name` | string | O | 런처 표시 이름 |
| `version` | string | O | `x.y.z`. 소스 프로젝트의 `ver.txt`와 같음 |
| `description` | string | X | 한 줄 설명 |
| `game.id` | string | O | `^[a-z0-9-]+$`. 런처가 같은 게임의 애드온을 묶는 키 |
| `game.name` | string | O | 게임 표시 이름 |
| `game.exe` | string | O | 게임 루트 기준 실행 파일 상대 경로. 게임 폴더 확인·실행에 사용 |
| `game.steamAppId` | int | X | Steam 라이브러리 자동 탐지·Steam 실행에 사용 |
| `variants` | object | O | 키 = 4장의 로더 ID. 하나 이상 |
| `variants.<로더>.minLoaderVersion` | string | X | 필요한 최소 로더 버전. 없으면 버전 무관 |

## 4. 로더 ID

| ID | 로더 | 감지 기준 (게임 루트 기준) | 모드 파일 위치 |
|---|---|---|---|
| `bepinex5` | BepInEx 5.x | `BepInEx/core/BepInEx.dll` | `BepInEx/plugins/<id>/` |
| `bepinex6` | BepInEx 6.x (Mono·IL2CPP) | `BepInEx/core/BepInEx.Core.dll` | `BepInEx/plugins/<id>/` |
| `melonloader` | MelonLoader 0.6 이상 | `MelonLoader/net35/MelonLoader.dll` 또는 `MelonLoader/net6/MelonLoader.dll` | `Mods/<id>.dll`, 의존 DLL은 `UserLibs/` |
| `none` | 로더 없음 | 항상 사용 가능 | 게임 루트 기준 자유 |

- Mono/IL2CPP는 게임이 정하므로 ID로 나누지 않음
- 새 로더(자체 로더 포함)는 이 표에 ID를 먼저 추가한 뒤 사용

## 5. 애드온 내용 규칙

1. 금지: 로더 자체 (`BepInEx/core/`, `MelonLoader/`, `winhttp.dll`, `version.dll`, `doorstop_config.ini` 등). 로더 설치는 런처 담당
2. 금지: 사용자 설정 파일. 기본 설정은 모드가 실행 중에 생성 (업데이트·제거 때 사용자 설정 보존)
3. 로더가 허용하면 모드 파일은 `<id>` 이름의 하위 폴더에 둠 (다른 모드와 파일명 충돌 방지)
4. 원본 게임 파일 교체는 최후 수단. 런타임 패치(Harmony 등)를 우선. 교체하면 런처가 백업·복원함
5. 변형마다 제공 기능이 다르면 `description`에 적음
6. 설정은 로더의 설정 기능으로, 런처가 편집할 수 있는 위치에 둠 (게임 루트 기준)
   - `bepinex5`·`bepinex6`: `Config.Bind`, 플러그인 GUID = `<id>` → `BepInEx/config/<id>.cfg`. 설명·기본값·형식·선택지는 BepInEx가 주석으로 기록하고 런처가 그대로 보여 줌
   - `melonloader`: `MelonPreferences.CreateCategory` 후 `category.SetFilePath("UserData/<id>.cfg")` → `UserData/<id>.cfg` (TOML). 항목 설명은 `CreateEntry`의 description
   - 별도 JSON 등 자체 설정 파일 금지 (런처 설정 창에 나오지 않음)

## 6. 런처 동작

애드온은 아래 동작을 전제로 만들고, 런처는 아래대로 구현합니다.

설치

1. 게임 루트 확인: `game.exe`가 있어야 함. `steamAppId`가 있으면 Steam 라이브러리에서 자동 탐지
2. 설치된 로더 감지 (4장 기준). 여러 개면 사용자에게 선택 요청
3. 감지된 로더가 `variants`에 있으면 그 변형 사용. `minLoaderVersion`보다 낮으면 경고
4. 감지된 로더가 `variants`에 없으면: `none` 변형이 있으면 사용, 없으면 설치 거부하고 이유 표시 (로더 두 개 공존은 기존 모드를 깨뜨릴 수 있음)
5. 로더가 없으면 사용자가 `variants` 중 선택. `none`이 아니면 런처가 로더 설치
6. 변형 폴더를 게임 루트에 복사. 덮어쓰는 기존 파일은 백업. 단, 경로 조각이나 파일 이름이 애드온 `id`와 같은 파일(이 애드온 자신의 파일, 예: 수동으로 넣은 이전 빌드)은 원본이 아니므로 백업하지 않고 먼저 지움
7. 설치한 파일 목록·백업 목록을 기록: 게임 루트의 `.lxium/<id>.json`, 백업은 `.lxium/backup/<id>/`

설정: 애드온 설정 파일(5장 6번)과 로더 설정(`BepInEx/config/BepInEx.cfg`, `UserData/Loader.cfg`)을 런처에서 편집. 바뀐 값의 줄만 고쳐 쓰고 나머지 줄은 그대로 둠. 파일이 없으면 게임을 한 번 실행하라고 안내

업데이트: 이전 버전 제거 후 새 버전 설치. 기록에 없는 파일(설정·로그·캐시)은 건드리지 않음

제거: 기록된 파일 삭제 → 백업 복원 → 비게 된 폴더 삭제. 로더는 남김 (다른 모드가 쓸 수 있음). 기록이 없으면 게임 폴더에 있는 이 애드온 자신의 파일만 지움

## 7. 소스 프로젝트 규칙 (`optimizeMods/LXIUM_<게임>/`)

- 저장소 루트에 `lxium.json` 원본을 둠
- 빌드 결과는 `dist/<id>-<version>.zip`. `dist/`는 git 제외
- `lxium.json`의 `version`이 `ver.txt`와 다르면 빌드 실패
- 시작 전 게임 백엔드 확인
  - IL2CPP: 게임 루트에 `GameAssembly.dll`
  - Mono: `<게임>_Data/Managed/Assembly-CSharp.dll`
- 여러 로더를 지원하면 최적화 로직은 로더와 무관한 코드로 두고, 로더별 진입점(BepInEx 플러그인 클래스, `MelonMod` 등)만 따로 둠

## 8. 패키징 후 검증

- [ ] zip 루트에 `lxium.json`과 변형 폴더만 있음
- [ ] `variants` 키와 변형 폴더 이름이 일치하고, 모두 4장의 ID
- [ ] `version` = `ver.txt`
- [ ] 로더 파일·사용자 설정 파일 없음
- [ ] 변형마다 해당 로더가 설치된 게임에 넣고 실행 → 로더 로그에 모드 로드 확인

## 9. 규격 변경

- 이 문서와 `../human/addon.md`를 같은 커밋에서 고침
- 기존 애드온이나 런처가 깨지는 변경이면 `schema`를 1 올림. 선택 필드 추가는 올리지 않음

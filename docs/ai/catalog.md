# LXIUM 애드온 모음 규격 (AI용)

- schema: `2` (런처 1.1.0부터. 1.0.0은 schema 1만 읽음)
- 사람용 문서: `../human/catalog.md`
- 애드온 zip 자체의 규격은 `addon.md`. 이 문서는 어디서 애드온을 찾는지만 다룸
- 런처 구현: `launcher/Models/Catalog.cs` (`Catalogs.ParseDoc`, `ResolveAsync`, `DownloadAsync`/`StoreAsync`, `Repackage`)

## 1. 용어

| 용어 | 뜻 |
|---|---|
| 애드온 모음 | 애드온을 어디서 받을지 적은 JSON 파일 하나 (`catalog.json`) |
| 항목 | 모음의 `addons` 배열 한 칸. 고정·GitHub·외부 세 가지 모양 |
| 공식 모음 | LXIUM 저장소 루트 `catalog.json` = `https://raw.githubusercontent.com/LocalXtend/LXIUM/dev/catalog.json` (런처 기본값, 설정의 "기본값으로 되돌리기") |

## 2. 형식

UTF-8 JSON. 주석 금지.

```json
{
  "schema": 2,
  "name": "LXIUM 공식",
  "addons": [
    { "github": "LocalXtend/LXIUM_manosaba" },
    {
      "github": "KOKOTO-DEV/ManosabaMod_KR",
      "asset": "^ManosabaLoader_KR_V[0-9_]+\\.zip$",
      "version": "^v(\\d+\\.\\d+\\.\\d+)",
      "include": { "bepinex6": ["BepInEx/plugins/ManosabaLoader/"] },
      "manifest": { "schema": 1, "id": "ManosabaLoader", "name": "...", "game": { "...": "" }, "variants": { "bepinex6": {} }, "tags": ["msmod"] }
    },
    { "url": "https://example.com/My_mod-1.0.0.zip", "sha256": "64자", "manifest": { "...": "zip 안 lxium.json 그대로" } }
  ]
}
```

| 필드 | 필수 | 규칙 |
|---|---|---|
| `schema` | O | `1` 또는 `2`. 더 크면 런처가 모음 전체를 거부하고 "런처 업데이트" 안내 |
| `name` | O | 런처에 출처로 표시. 비면 주소의 호스트 이름 |
| `addons` | O | 항목 배열 |

항목 세 가지 (위에서부터 판별: `github` 있음 → GitHub/외부, 없음 → 고정)

| 모양 | 필드 | 런처가 하는 일 |
|---|---|---|
| GitHub (LXIUM 규격 저장소) | `github` = `owner/repo` | 정식 릴리스마다: 태그 `<id>-v<x.y.z>`, asset `<id>-<x.y.z>.zip`(GitHub `digest` sha256 필수), 태그의 `lxium.json`(id·version이 태그와 같아야 함), 아이콘 = 태그의 `addon.png` |
| 외부 (LXIUM 규격이 아닌 모드) | `github`, `asset`(asset 이름 정규식), `manifest`(version 없이), `include`(변형 → 가져올 zip 경로 접두사들), 선택 `version`(태그에서 버전을 꺼낼 정규식, 그룹 1, 기본 `(\d+\.\d+\.\d+)`), `icon`(https) | 정식 릴리스마다 버전 = 태그에서, asset = 정규식에 맞는 첫 파일. 받은 뒤 `include` 경로만 `<변형>/<원래 경로>`로 옮겨 `lxium.json`과 함께 LXIUM 애드온 zip으로 다시 묶음 |
| 고정 (schema 1과 같음) | `url`(https), `sha256`(64자), `manifest`(zip의 lxium.json), 선택 `icon` | 적힌 그대로 |

- `manifest`는 `addon.md` 3장 검사를 그대로 받음 (외부는 version을 채운 뒤)
- 외부 모드의 `include`에는 로더 파일·설정 파일을 넣지 않음 (`addon.md` 5장). 외부 zip이 BepInEx 전체를 담고 있어도 플러그인 폴더만 고름
- 외부 모드의 `id`는 그 모드의 플러그인 폴더 이름과 같게 하면 손으로 넣은 설치를 런처가 알아봄 (`addon.md` 6장 8번)

## 3. 런처 동작

1. 시작할 때(`autoUpdate`가 켜져 있을 때), 새로 고침, "지금 업데이트 확인"에서 등록된 모든 모음을 받음
2. GitHub 릴리스 목록은 `cache/github/<owner>_<repo>.json`에 ETag와 함께 캐시. 바뀌지 않았으면 304(요청 한도에 세지 않음). 받지 못하면 캐시를 씀. 태그의 `lxium.json`은 한 번 받으면 계속 캐시 (태그는 바뀌지 않는다고 봄)
3. 모음 하나를 받지 못하거나 schema가 맞지 않으면 그 모음만 건너뛰고, 규칙에 어긋난 릴리스·항목은 그것만 건너뛰고 설정 화면에 이유 표시
4. 받아 둔 zip(`addons/`)과 모음 항목을 `(id, version)`으로 합침. 같은 버전이면 받아 둔 zip 우선. 게임은 `steamAppId`(없으면 `game.id`)로 묶음
5. 모음에서 찾은 애드온은 바로 추가·설치하지 않음. 모드 패널 "모드 추가하기"에 보이고, 사용자가 **추가**를 누르면 받기 → sha256 비교 → (외부면 다시 묶기) → zip의 lxium.json `id`·`version` 확인 → `addons/<id>-<version>.zip`. 하나라도 다르면 파일을 남기지 않고 거부
6. 추가한 애드온에 더 높은 버전이 있으면 "업데이트 있음". 설치는 사용자가 누를 때만 (자동 설치 금지)
7. 라이브러리에는 받아 둔 애드온이 있는 게임, 또는 Steam에 설치된 게임만 표시

## 4. 공식 모음 고치기

- 공식 모음은 저장소 목록이라 모드를 릴리스해도 고치지 않음. 새 게임 모드 저장소·외부 모드를 등록할 때만 고침
- 모드 릴리스는 각 모드 저장소에서: 태그 `<id>-v<버전>`을 그 버전 커밋에 두고, asset `<id>-<버전>.zip`을 올림 (`addon.md` 7장)
- 외부 모드를 등록하기 전: 릴리스 zip을 받아 `include` 경로가 맞는지, 라이선스가 재배포가 아닌 "원본 릴리스에서 받기"를 막지 않는지 확인

## 5. 보안 규칙

- 애드온은 게임 프로세스 안에서 실행되는 코드. 모음 추가 시 런처가 신뢰 경고를 띄움
- https와 sha256(고정 = 모음의 값, GitHub·외부 = GitHub가 계산한 asset digest) 필수는 바꾸지 않음. 검증을 약하게 하는 변경 금지
- 압축 해제는 `Installer.SafePath`로 게임 루트 밖 경로 거부 (`addon.md` 6장)

## 6. 규격 변경

- 이 문서와 `../human/catalog.md`를 같은 커밋에서 고침
- 기존 런처가 읽지 못하게 되는 변경이면 `schema`를 1 올림 (런처 `Catalogs.Schema`도). 선택 필드 추가는 올리지 않음

# LXIUM 애드온 모음 규격 (AI용)

- schema: `1`
- 사람용 문서: `../human/catalog.md`
- 애드온 zip 자체의 규격은 `addon.md`. 이 문서는 여러 애드온 zip을 한 JSON으로 알리는 방법만 다룸
- 런처 구현: `launcher/Models/Catalog.cs` (`Catalogs.Parse`, `FetchAsync`, `DownloadAsync`/`StoreAsync`)

## 1. 용어

| 용어 | 뜻 |
|---|---|
| 애드온 모음 | 애드온 zip들의 주소·해시·lxium.json을 담은 JSON 파일 하나 (`catalog.json`) |
| 항목 | 애드온 zip **한 버전**. 같은 애드온의 여러 버전은 항목 여러 개 |
| 공식 모음 | `https://raw.githubusercontent.com/LocalXtend/LXIUM/dev/catalog.json` (런처 기본값, `AppSettings.OfficialCatalog`) |

## 2. 형식

UTF-8 JSON. 주석 금지.

```json
{
  "schema": 1,
  "name": "LXIUM 공식",
  "addons": [
    {
      "url": "https://github.com/LocalXtend/LXIUM_mahosaba/releases/download/LXIUM_mahosaba-v0.5.1/LXIUM_mahosaba-0.5.1.zip",
      "sha256": "f40cb630...e5c1",
      "manifest": { "...": "zip 안 lxium.json 내용 그대로" }
    }
  ]
}
```

| 필드 | 타입 | 필수 | 규칙 |
|---|---|---|---|
| `schema` | int | O | `1`. 다르면 런처가 모음 전체를 거부하고 "런처 업데이트" 안내 |
| `name` | string | O | 런처에 출처로 표시. 비면 주소의 호스트 이름 |
| `addons[].url` | string | O | `https://`만. zip 직접 받기 주소 |
| `addons[].sha256` | string | O | zip 파일의 SHA-256, 16진수 64자 (대소문자 무관) |
| `addons[].manifest` | object | O | 그 zip의 `lxium.json`과 같은 내용. `addon.md` 3장 규칙을 그대로 검사 |

## 3. 런처 동작

1. 시작할 때(`settings.autoUpdate`가 켜져 있을 때), 새로 고침, "지금 업데이트 확인"에서 등록된 모든 모음을 받음
2. 모음 하나를 받지 못하거나 schema가 다르면 그 모음만 건너뛰고 설정 화면에 이유 표시
3. 규칙에 어긋난 항목(http, sha256 없음, manifest 오류)은 그 항목만 건너뛰고 이유 표시
4. 받아 둔 zip(`addons/`)과 모음 항목을 `(id, version)`으로 합침. 같은 버전이면 받아 둔 zip 우선
5. 라이브러리에는 받아 둔 애드온이 있는 게임, 또는 Steam에 설치된 게임만 표시 (모음에만 있고 게임도 없는 항목은 숨김)
6. 사용자가 받지 않은 버전을 설치하면: 받기 → SHA-256 비교 → zip의 lxium.json `id`·`version`이 항목과 같은지 확인 → `addons/<id>-<version>.zip`으로 저장 → `addon.md` 6장대로 설치. 하나라도 다르면 파일을 남기지 않고 거부
7. 설치된 버전보다 높은 버전이 있으면 "업데이트 있음"으로 표시. 설치는 사용자가 누를 때만 (자동 설치 금지)

## 4. 공식 모음 만들기·올리기

1. 각 모드 저장소(`LocalXtend/<id>`)를 LXIUM `optimizeMods/<id>/`에 clone해 빌드 → `optimizeMods/<id>/dist/<id>-<ver>.zip`
2. 저장소 루트에서 `./build.ps1` → `dist/catalog.json` (항목 url = `https://github.com/LocalXtend/<id>/releases/download/<id>-v<ver>/<id>-<ver>.zip`, sha256·manifest는 zip에서 계산)
3. 모드 저장소에 GitHub 릴리스 `<id>-v<ver>`를 만들고 같은 zip을 올림 (zip을 다시 빌드하면 sha256이 바뀌므로 `build.ps1`이 쓴 zip을 그대로 올림)
4. `dist/catalog.json`을 저장소 루트 `catalog.json`으로 복사해 dev에 커밋
5. 이전 버전 항목을 남기려면 루트 `catalog.json`의 기존 항목을 지우지 않고 새 항목만 더함 (`build.ps1`은 지금 버전만 씀)

## 5. 보안 규칙

- 애드온은 게임 프로세스 안에서 실행되는 코드. 모음 추가 시 런처가 신뢰 경고를 띄움
- https·sha256 필수는 바꾸지 않음. 서명·검증을 약하게 하는 변경 금지
- 압축 해제는 `Installer.SafePath`로 게임 루트 밖 경로 거부 (`addon.md` 6장)

## 6. 규격 변경

- 이 문서와 `../human/catalog.md`를 같은 커밋에서 고침
- 기존 런처가 읽지 못하게 되는 변경이면 `schema`를 1 올림. 선택 필드 추가는 올리지 않음

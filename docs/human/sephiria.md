# 세피리아: 새 분석 기준과 공식 모드 API

2026-10-14 현재 설치 파일을 새로 조사한 기록이다. 이전 세피리아 문서, 코드, 커밋은 분석 근거로 사용하지 않았다. 2026-10-15 공식 로더를 사용하는 비교 실험 모드 1.0.0을 구현했다. 실행 성능과 실제 F10 결과는 아직 검증 전이다.

모드는 `AddOns/LXIUM_sephiria/`에 설치한다. 첫 로드 시 생성하는 `config.json`에서 Options와 LastComparison을 관리하고 게임 장면에서 F10으로 비교한다. 배포 패키지는 런처의 `none` 변형을 사용하며, 기존 Harmony가 있으면 재사용한다. 정확한 실험 순서·지원 조건은 [렌더링 문서](sephiria-render.md)의 현재 구현 절에 있다. 빌드는 SDK10으로 하되 Unity Mono에서 읽을 DLL의 대상은 netstandard2.1이다.

## 확인 범위

- 설치된 `Sephiria_Data/Managed/Assembly-CSharp.dll`을 REA로 정적 분석했다.
- ILSpy로 필요한 타입만 재구성해 흐름을 확인했다. 재구성한 C#은 원본 소스가 아니며 실행 결과도 아니다.
- 설치된 모드는 별도로 조사했다. 파일 존재와 로더의 선택 규칙을 확인했으며 실제 로드 성공 여부는 확인하지 않았다.
- 게임 설치 파일, 모드, 세이브, 옵션은 수정하지 않았다.
- 렌더링 분석과 개발 후보는 [sephiria-render.md](sephiria-render.md)에 있다.

게임 본체는 Unity Mono의 관리 코드다. 이 PC의 운영체제가 보고한 GPU는 `Intel Iris Xe Graphics`다. UHD 620/680의 성능을 이 장치의 성능으로 간주하지 않는다. GPU 이름만으로 실제 게임에서 선택된 어댑터나 120 FPS 달성을 확인할 수 없다.

## 공식 로더로 들어가는 방법

`HorayModAPI`는 게임 본체에 실제로 존재한다. 모드를 발견하고 실행하는 쪽은 `AddOnLoader`이며, 진입 클래스는 `HorayModBase`를 상속한다.

로더는 게임 루트 `AddOns`의 바로 아래 폴더에서 `metadata.json`을 읽는다. 공식 필드는 다음 다섯 가지다.

| 필드 | 용도 |
|---|---|
| `modName` | 표시 이름 |
| `modVersion` | 버전, 기본값 `1.0.0` |
| `modAuthor` | 제작자 |
| `dllFile` | 로드할 DLL 파일 |
| `entryClass` | 생성할 진입 클래스 |

`dllFile`과 `entryClass`는 명시하는 것이 좋다. 생략하면 여러 DLL/클래스 중 첫 후보를 선택한다. 로더는 폴더 순서를 정렬하지 않는다. `Libs`의 DLL이나 폴더 안의 DLL을 전부 자동으로 읽는 구조도 아니다.

`HorayModBase`는 Unity 컴포넌트가 아닌 일반 클래스다. 매개변수 없는 생성자로 만든 뒤 `LoadMod(metadata)`가 `OnModLoaded()`를 호출한다. 종료 경로는 `UnloadMod()` → `OnModUnloaded()`다. 렌더러를 구동할 Unity 컴포넌트는 모드가 따로 만들어야 한다.

## 초기화 순서

`GameDataLoader.Awake`에서 확인한 순서는 다음과 같다.

1. 내부 모드 등록과 외부 모드 로드
2. 게임 데이터베이스 초기화
3. `OnAllDatabasesReady` 통지
4. 공간 해시·물리·SpriteFx/ParticleFx 등의 풀 초기화
5. 로컬라이제이션 준비 통지

따라서 `OnModLoaded()`나 `OnAllDatabasesReady` 직후에 카메라와 효과 풀이 모두 준비됐다고 가정하면 안 된다. 씬마다 실제 카메라와 렌더러의 준비 상태를 확인해야 한다.

## API로 할 수 있는 것

선택한 `HorayModAPI` 전체에서 이벤트 20개를 확인했다.

- 데이터베이스 8종: Miracle, Weapon, Status, Item, Keyword, Passive, Costume, Prop
- 데이터베이스 전체 준비, 로컬라이제이션 준비
- 게임 시작·세션 시작의 서버/클라이언트 알림
- 층 배정의 서버/클라이언트 알림
- 플레이어 회피, 디버프, 인벤토리 시작/종료 허용 관련 훅

네트워크 프리팹을 등록·해제하는 메서드도 있다. 이 타입에는 프레임 업데이트, 카메라 렌더 패스, 셰이더 교체 콜백이 없다. 공식 로더로 진입한 후 Unity 렌더링 확장이나 좁게 검증한 패치가 필요하다.

대부분의 Action 이벤트는 구독자별 예외를 처리한다. 그러나 `OnGetDebuff`는 마지막 구독자 하나만 호출한다. 여러 모드의 디버프 처리가 자동으로 합성되는 API가 아니다.

조사한 `GameDataLoader.OnDestroy`에는 전체 모드 언로드나 API 초기화 호출이 없다. 다른 종료 경로는 미확인이다. 모드는 자기 컴포넌트·이벤트·GPU 자원을 직접 정리하고 중복 구동도 막아야 한다.

## 현재 모드 환경

| 폴더 | metadata 버전 | 공식 로더가 선택하는 DLL |
|---|---|---|
| CustomSkin | 1.1.9 | `HarmonyBase.dll` 래퍼 |
| MiraItemMod | 1.1.5 | `MiraModBase.dll` 래퍼 |
| SendMoney Mod | 1.0.1 | `SendMoney.dll` |
| Sephiria QoL | 1.55.0 | `SephiriaQoL-1.55.0.dll` |

CustomSkin과 Mira는 래퍼가 추가 DLL을 읽는다. `modFile`, `modClass`, `modLibs`는 이런 외부 래퍼의 필드이며 공식 스키마와 구분한다. QoL 폴더에 있는 추가 `SephiriaQoL.dll`을 공식 로더가 함께 읽는 것은 아니다.

CustomSkin/Mira의 Harmony와 QoL의 Harmony는 서로 다른 파일이다. QoL은 이미 Harmony가 로드됐으면 자기 파일 로드를 생략한다. 모드 로드 순서가 실제 패치 환경에 영향을 줄 가능성이 있다. 충돌이 발생했다고 판정한 것은 아니다.

QoL에는 게임 속도, 개체 생성 배율, 카메라 줌, HUD 관련 기능이 있다. 비교 측정에서는 이 설정을 고정해야 한다. Mira 래퍼에는 현재 게임 API에 없는 옵션 콜백 참조도 있어 실제 사용 경로를 추가 확인해야 한다.

## 제작 기준

공식 로더를 진입점으로 사용하고, 처음부터 필수 의존성으로 Harmony를 추가하지 않는다. 필요한 렌더링 기능을 Unity의 기존 확장 지점에서 구현할 수 있는지 먼저 확인한다. 패치가 필요한 경우 현재 DLL의 타입·서명·버전을 검증하고 호환되지 않으면 해당 기능을 적용하지 않는다.

최적화 비교는 같은 설치 환경에서 LXIUM 기능만 끈 상태와 켠 상태를 비교한다. F10은 기능별 단독 구간과 전체 조합, 일반 상태 재측정을 제공한다. 별도의 무모드 기준이 필요하면 사용자 모드 폴더를 바꾸지 않는 분리된 테스트 설치에서 진행한다. 현재 패키지와 설치는 준비됐으며 무모드 측정과 실제 게임 GPU 측정은 하지 않았다.

## 근거

본체 SHA-256: `c57a0daeab8e8d0af7066a344133eec4c57d8f303fd9e25da9410fbfc4cf1510`.
MVID: `ccf97024-28bd-48f1-9666-c768c2bd4b2b`.

REA 근거 ID와 메서드 토큰은 [AI용 같은 주제 문서](../ai/sephiria.md)에 기록했다. 일부 문자열에 대해 REA가 #US terminal flag 문제를 보고했으며, 이를 모드 로드 실패의 증거로 해석하지 않는다.

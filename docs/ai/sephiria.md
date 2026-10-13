# Sephiria 분석 (AI용)

- 사람용 문서: `../human/sephiria.md`
- 대상 프로젝트: `optimizeMods/LXIUM_sephiria/`
- 이 문서의 사실은 아래 빌드에서 정적 분석(실행 없음)으로 얻음. 게임 업데이트 후에는 2장의 식별값을 먼저 비교하고, 다르면 해당 사실을 다시 확인함
- 표기: **[관찰]** 바이너리·로그에서 직접 확인 / **[추론]** 관찰에서 이끌어 낸 판단 / **[미확인]** 아직 증거 없음

## 1. 결정 사항

| 항목 | 결정 |
|---|---|
| 로더 | 게임 내장 AddOns 로더(HorayModAPI). BepInEx·MelonLoader 설치 안 함 |
| 애드온 변형 | `none` 하나. 변형 폴더 안 경로는 `AddOns/LXIUM_sephiria/` |
| 패치 방식 | 런타임만 (Unity API 직접 호출 + 필요 시 Harmony). 게임 원본 파일 교체·IL 재작성 금지 |
| Harmony | 이미 로드된 HarmonyLib 재사용, 없으면 동봉본 로드. 고유 ID, 언로드 때 `UnpatchSelf` |
| 게임 로직 | 데미지·판정·드롭 등 동기화 대상 로직은 바꾸지 않음 (멀티플레이 비동기화 방지) |

## 2. 분석 대상 식별값

| 항목 | 값 |
|---|---|
| 엔진 | Unity 6000.3.21f1, Mono (MonoBleedingEdge), 64비트 |
| `boot.config` build-guid | `ab7f9b797587402792f88d2aa08073b3` |
| `Assembly-CSharp.dll` SHA-256 | `c57a0daeab8e8d0af7066a344133eec4c57d8f303fd9e25da9410fbfc4cf1510` |
| `Assembly-CSharp.dll` MVID | `ccf97024-28bd-48f1-9666-c768c2bd4b2b` |
| 메타데이터 | TypeDef 4042, MethodDef 34246, 참조 netstandard 2.1 |
| 렌더 파이프라인 | URP 2D (`Unity.RenderPipelines.Universal.2D.Runtime`) |
| 그래픽 API | 기본 D3D11 [관찰: Player.log]. `D3D12/` 폴더도 동봉 |

## 3. 공식 모드 로더 (`AddOnLoader`) [관찰]

- 호출 시점: `GameDataLoader.Awake` → `InternalMod.Register` → `AddOnLoader.LoadAll` → 각 `*Database.Initialize` → `HorayModAPI.NotifyAllModDatabasesReady`. 즉 모드는 데이터베이스 초기화 전에 로드됨
- `GameDataLoader`는 타이틀에서 세이브를 골라 게임에 들어갈 때(서버 시작 직후) 생성됨. **타이틀 화면에서는 모드가 아직 로드되지 않음** [관찰: Player.log]. 한 번 로드되면 이후 타이틀로 돌아가도 유지됨
- 폴더: `Path.GetFullPath(Application.dataPath + "/../AddOns")` 의 하위 폴더 전부. `Directory.GetDirectories` 결과 순서 그대로, 정렬 코드 없음
- 폴더별 처리
  1. `metadata.json` 읽기 (`JsonUtility.FromJson<AddOnMetadata>`). 없으면 폴더명을 `modName`으로 진행
  2. `dllFile`이 있으면 그 파일, 없으면 폴더의 `*.dll` 중 첫 번째
  3. `Assembly.LoadFrom`
  4. `entryClass`로 `Assembly.GetType`. 없거나 `HorayModBase` 하위가 아니면 경고 후 `HorayModBase`를 상속한 첫 번째 비추상 타입 자동 탐색
  5. `Activator.CreateInstance` → `LoadMod(Metadata)` → `OnModLoaded()`
- `AddOnMetadata` 필드는 `modName`, `modVersion`, `modAuthor`, `dllFile`, `entryClass` 다섯 개뿐. 다른 필드(`modFile`, `modClass` 등)는 로더가 무시함
- 하위 폴더(`Libs/` 등)의 의존 DLL은 로더가 찾아주지 않음. 의존 DLL은 모드가 직접 해결해야 함
- 언로드: `AddOnLoader.UnloadAll` → `HorayModBase.UnloadMod` → `OnModUnloaded()`
- `AddOnsDisabled` 폴더는 게임 코드에 없음. 다른 도구의 관례 [추론]. 런처는 이 폴더를 건드리지 않음
- Steam 창작마당 로더(`SteamWorkshopLoader`)는 언어팩(`LocalizationManager.LoadLanguageFromWorkshop`)에만 쓰임. DLL 모드는 AddOns에서만 로드

## 4. HorayModAPI [관찰]

공식 문서: https://teamhoray.com/mod-api (WIP 표시). 바이너리가 문서보다 앞서 있음 (예: 문서의 `OnFloorAllocated`는 "미구현", 바이너리는 `OnFloorAllocatedServerside/Clientside`로 구현됨). 시그니처는 바이너리 기준으로 씀.

| 종류 | 멤버 |
|---|---|
| 베이스 클래스 | `HorayModBase` — `OnModLoaded()`, `OnModUnloaded()` (protected virtual), `metadata` |
| DB 로드 이벤트 (`Action`) | `OnLoadMiracleDatabase`, `OnLoadWeaponDatabase`, `OnLoadStatusDatabase`, `OnLoadItemDatabase`, `OnLoadKeywordDatabase`, `OnLoadPassiveDatabase`, `OnLoadCostumeDatabase`, `OnLoadPropDatabase`, `OnAllDatabasesReady` |
| 게임 흐름 이벤트 | `OnStartGameServerside/Clientside` (`Action`), `OnStartSessionServerside/Clientside` (`Action<bool>`), `OnFloorAllocatedServerside/Clientside` (`Action<string,string,FloorGenerator>`) |
| 전투·인벤토리 이벤트 | `OnPlayerEvade` (`Action<PlayerAvatar,DamageInstance>`), `OnGetDebuff` (`Func<CharacterDebuff,UnitAvatar,CharacterDebuff>`), `GridInventoryStartPermission/EndPermission` (`Action<GridInventory,PlayerAvatar>`) |
| 로컬라이제이션 | `OnLocalizationReady` (`Action<HorayModLocalizationContext>`), `ctx.AddText(lang,key,value)`, `ctx.AddText(key,Dictionary)` |
| 네트워크 | `RegisterNetworkPrefab(s)`, `UnregisterNetworkPrefab` |
| DB 등록·수정 | `ItemDatabase`, `WeaponDatabase`, `MiracleDatabase`, `StatusDatabase`, `KeywordDatabase`, `CostumeDatabase`, `PropDatabase`의 `Register` / `Modify(id, Action<T>)` |

- 이벤트 호출은 `SafeInvoke*`로 감싸져 있어 구독자 예외가 게임을 멈추지 않음
- 렌더링·성능·프레임 관련 API 없음. 최적화에 쓸 수 있는 건 "게임 프로세스 안의 진입점"뿐 [관찰]

## 5. 모드 감지 (`ModEnvironment`) [관찰]

- `RuntimeInitializeOnLoadMethod`로 시작, 이후 `AppDomain.AssemblyLoad` 감시
- 어셈블리 이름 접두사: `MelonLoader`, `BepInEx`, `UnityModManager`, `0Harmony`, `SPMod`. AddOns 모드가 1개 이상 로드돼도 감지됨
- 감지 시 하는 일: `IsModded = true`, `CrashReportHandler.enableCaptureExceptions = false`(클라우드 예외 업로드 중단). `IsModded`는 `HorayAnalyticsManager`만 읽음
- 차단·기능 제한 없음

## 6. 기존 커뮤니티 모드 [관찰]

| 모드 | 구조 | Harmony |
|---|---|---|
| CustomSkin | 부트스트랩 `HarmonyBase.dll`(HorayModBase) → `Libs/0Harmony.dll` 선행 로드 → 본 DLL 로드 | 2.10.2, 패치 17 |
| Mira's Item Mod | 부트스트랩 `MiraModBase.dll` → 본 DLL | 2.10.2, 패치 124 (대상 타입 68) |
| Sephiria QoL | 단일 DLL. `HarmonyLib.Harmony, 0Harmony`를 `Type.GetType`으로 찾고 없으면 `Libs/0Harmony.dll`을 `LoadFrom`, 리플렉션으로 패치 | 2.4.2 동봉 |
| SendMoney | 단일 DLL, 공식 API만 | 없음 |

- 같은 이름의 `0Harmony`가 여러 버전 동봉됨. 어느 버전이 최종 사용되는지 [미확인]
- 모든 모드가 공식 API와 Harmony를 함께 씀. AddOns 방식이 이 게임 모드 생태계의 표준 [추론]

## 7. 렌더링·성능 구조

| 항목 | 내용 |
|---|---|
| 렌더러 | URP 2D. `Light2D` 다수 사용, 일부는 매 프레임 쿠키 스프라이트 교체 (`SimpleAnimator2D_Light.Update`) [관찰] |
| 후처리 | `GameCamera.volume` (기본·세피아 `VolumeProfile`), 이벤트에서 Volume 오버라이드 추가 [관찰] |
| 화면 효과 | `Glitch`, `RadialDistortion`, `ReverseRadialDistortion`, `TexGlitch` (MonoBehaviour + 코루틴) [관찰] |
| 날씨·분위기 | `RainControl`, `SnowControl`, `FogControl`, 불티 스포너, `DayCycle`(LateUpdate에서 라이트 색) [관찰] |
| 사용자 그래픽 옵션 | 해상도, VSync, FPS 제한만 (`DisplayManager.SetVSyncAndLimitFPS`, `SetScreenResolution`) [관찰] |
| 품질·렌더 스케일 코드 | 없음 (`QualitySettings` 단계 변경, renderScale 설정 호출 없음) [관찰] |
| 커스텀 렌더 패스·Burst 잡 | Assembly-CSharp에 없음 [관찰] |
| 이미 된 최적화 | 오브젝트 풀링(`ObjectPoolingFactory<T>`, `PoolPrewarm`), 공간 해시 물리(`TopdownSpatialHash`, NonAlloc), 애니메이터 셀 컬링(`CellBasedCulling` → `SimpleAnimator2D.CulledUpdate`) [관찰] |
| 스크립트 부하 | MonoBehaviour 계열 1997 타입, `Update` 644, `LateUpdate` 97 [관찰] |
| Update 안 비용 후보 | 물리 쿼리 46곳(스포너류), 문자열 결합 59곳(`UI_HPBar` 등), `GetComponent` 55곳, LINQ 10곳(`PlayerAvatar`, `DayCycle`), `Camera.main` 2곳 [관찰]. 실제 비중 [미확인] |
| URP 에셋 값 | 8장 참고 [관찰: 진단 모드] |
| CPU/GPU 병목 | [미확인]. 이 빌드는 `FrameTimingManager`가 꺼져 있어 CPU/GPU 시간을 직접 못 읽음. renderScale A/B로 판별 |

## 8. 실행 중 측정 (진단 모드 0.1.0)

`LXIUM_sephiria`의 진단 기능(`src/Diagnostics.cs`)으로 읽은 값. 개발 PC는 내장 GPU 노트북, 창 모드 약 1440×1000.

URP 에셋 (`UniversalRenderPipelineAsset`) [관찰]

| 값 | 상태 |
|---|---|
| `m_RenderScale` | 1 |
| `m_MSAA` | Disabled |
| `m_SupportsHDR` | True (`m_HDRColorBufferPrecision=_32Bits`) |
| `m_UpscalingFilter` | Auto |
| `m_UseSRPBatcher` | True |
| `m_VolumeFrameworkUpdateMode` | EveryFrame |
| `m_ColorGradingMode` | LowDynamicRange |
| 3D 그림자·라이트 쿠키 등 | 그림자 꺼짐. 2D 게임이라 영향 작음 [추론] |

2D 렌더러 (`Renderer2DData`) [관찰]

| 값 | 상태 |
|---|---|
| `m_LightRenderTextureScale` | 1 (라이트 텍스처를 화면 해상도 그대로 그림) |
| `m_MaxLightRenderTextureCount` | 16 |
| `m_LightBlendStyles` | 4개 |
| `m_UseDepthStencilBuffer` | True |

카메라 [관찰]

- 메인 카메라: 직교, `renderPostProcessing=True`, AA 없음, 화면에 직접 그림
- 타이틀의 `WaterCamera`: 1920×640 렌더 텍스처에 별도로 그림 (HDR, MSAA 허용)

실행 환경 [관찰]

- D3D11, 그래픽 스레드 MultiThreaded, 품질 단계 1개(`Default`)
- 게임 옵션 기본값 VSync 0, `targetFrameRate` 0(제한 없음)
- 릴리스 빌드라 `FrameTimingManager`, `GC Allocated In Frame` 카운터는 값이 없음. Render 카운터(SetPass·Draw·Batches)는 읽힘

프레임 (마을 → Rabbittown → Moleland 초반, 모드 5개 설치) [관찰]

| 지표 | 값 |
|---|---|
| 평균 FPS | 118~165 |
| p50 | 5.7~7.7ms |
| p99 | 36~39ms. 모든 10초 구간에서 거의 같음, 타이틀에서도 같음 |
| SetPass / Draw | 101~195 / 137~346 |

- 평균 처리량은 충분하나 상위 1% 프레임이 평소의 5~6배. 체감 끊김의 1순위 후보 [추론]
- 일정한 길이(약 37ms)의 주기적 정지라 GC 또는 고정 작업일 가능성 [추론]. 진단 0.1.1에서 끊김 프레임과 GC 실행을 같이 기록해 판별

## 9. 다른 방식을 쓰지 않는 이유

| 방식 | 판단 근거 |
|---|---|
| BepInEx 5/6, MelonLoader | 게임 루트에 프록시 DLL 추가. 두 로더 동시 설치 시 충돌 위험. AddOns도 데이터 초기화 전에 로드되므로 조기 로드 이득 작음. 커뮤니티 모드는 전부 AddOns |
| `Assembly-CSharp.dll` 직접 패치 | Steam 업데이트·무결성 검사로 원복. 다른 모드의 Harmony 대상 메서드가 바뀌어 깨질 수 있음. 수정한 게임 DLL 배포는 저작권 문제 |
| 런처 실행 인자 (`-force-d3d12` 등) | 파일 변경 없이 쓸 수 있는 보조 수단. 효과는 측정 후 결정 |

- 멀티플레이 접속 검사(`HorayNetworkAuthenticator.OnServerVersionMessage`)는 `Application.version` 문자열만 비교. DLL 해시 검사 없음 [관찰]. 단 로직을 바꾸면 클라이언트 간 상태가 어긋날 수 있음 [추론]

## 10. 모드 구현 규칙 (LXIUM_sephiria)

1. 설치 위치 `AddOns/LXIUM_sephiria/`. `metadata.json`에는 3장의 공식 필드 5개만 씀. `entryClass`는 전체 이름을 정확히
2. 진입 클래스(`HorayModBase` 상속)의 필드·시그니처에 Harmony 타입을 쓰지 않음. `OnModLoaded`에서 의존 DLL 해결을 먼저 하고, Harmony를 쓰는 코드는 별도 클래스로 분리
3. 매 프레임 동작이 필요하면 `OnModLoaded`에서 `DontDestroyOnLoad` GameObject + MonoBehaviour를 만듦 (`HorayModBase`는 MonoBehaviour가 아님)
4. 최적화 우선순위: ① Unity 설정 API(URP 에셋, Light2D, Volume, 파티클 on/off) ② 프로파일링으로 확인된 핫스팟만 Harmony 패치
5. 다른 모드의 로드 순서나 패치를 전제하지 않음
6. 로그 접두사 `[LXIUM]`. 출력은 Unity 로그(`Player.log`)
7. 사용자 설정 파일은 모드가 실행 중 생성 (`docs/ai/addon.md` 5장)

## 11. 분석 방법

- REA(`inspect_managed_artifact`)로 `Assembly-CSharp.dll`을 관리 PE로 분류하고 메타데이터를 확인함. REA `inspect_managed_members`는 64MB 고정 한도 초과로 본체와 QoL DLL에서 실패
- 대체로 .NET `System.Reflection.Metadata`로 IL을 읽기만 하는 덤프를 만들어 타입·메서드·호출·문자열을 추출. 타입·메서드 수가 REA 수치와 일치함
- `Player.log`(이전 실행 기록)로 로드 순서·그래픽 API를 확인

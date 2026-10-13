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
- `GameDataLoader`는 타이틀에서 세이브를 골라 게임에 들어갈 때(서버 시작 직후) 생성됨. **타이틀 화면에서는 모드가 아직 로드되지 않음** [관찰: Player.log]
- **타이틀로 나갔다가 다시 게임에 들어가면 `LoadAll`이 다시 실행됨** [관찰: Player.log]. 그 사이 `UnloadAll`은 호출되지 않음. `Assembly.LoadFrom`은 이미 로드된 같은 어셈블리를 돌려주므로 static 상태는 유지되고, `HorayModBase` 인스턴스만 새로 생겨 `OnModLoaded`가 다시 불림
  - 모드는 `OnModLoaded`를 여러 번 받아도 안전해야 함 (static 플래그로 1회만 초기화)
  - Mira's Item Mod는 `Core.IsInitialized`로 막음 [관찰]. CustomSkin은 `HarmonyInitializer.Init`이 매번 `PatchAll`을 호출하고 막는 코드가 보이지 않아, 재진입마다 패치가 겹칠 수 있음 [추론, 미확인]
  - LXIUM 0.2.0까지는 재진입마다 호스트 GameObject가 하나씩 늘어나는 버그가 있었음. 0.2.1에서 static 호스트로 수정
- 폴더: `Path.GetFullPath(Application.dataPath + "/../AddOns")` 의 하위 폴더 전부. `Directory.GetDirectories` 결과 순서 그대로, 정렬 코드 없음
- 폴더별 처리
  1. `metadata.json` 읽기 (`JsonUtility.FromJson<AddOnMetadata>`). 없으면 폴더명을 `modName`으로 진행
  2. `dllFile`이 있으면 그 파일, 없으면 폴더의 `*.dll` 중 첫 번째
  3. `Assembly.LoadFrom`
  4. `entryClass`로 `Assembly.GetType`. 없거나 `HorayModBase` 하위가 아니면 경고 후 `HorayModBase`를 상속한 첫 번째 비추상 타입 자동 탐색
  5. `Activator.CreateInstance` → `LoadMod(Metadata)` → `OnModLoaded()`
- `AddOnMetadata` 필드는 `modName`, `modVersion`, `modAuthor`, `dllFile`, `entryClass` 다섯 개뿐. 다른 필드(`modFile`, `modClass` 등)는 로더가 무시함
- 하위 폴더(`Libs/` 등)의 의존 DLL은 로더가 찾아주지 않음. 의존 DLL은 모드가 직접 해결해야 함
- 언로드: `AddOnLoader.UnloadAll` → `HorayModBase.UnloadMod` → `OnModUnloaded()`. 관찰한 플레이 흐름(타이틀 복귀·재진입)에서는 호출되지 않음
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
| SendMoney | 단일 DLL, 공식 API만. 채팅창 연결 전까지 0.5초마다 `FindObjectOfType` → 게임 월드에서 0.5초마다 20~25ms 끊김 (8장, 측정 확정) | 없음 |

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
| CPU/GPU 병목 | 개발 PC 기준 CPU 쪽 (8장). 이 빌드는 `FrameTimingManager`가 꺼져 있어 CPU/GPU 시간을 직접 못 읽고 renderScale A/B로 판별함 |

## 8. 실행 중 측정 (진단 모드 0.1.0~0.1.3, 1~5차)

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
| p99 | 36~39ms. 게임 월드에 있는 동안 거의 모든 10초 구간에서 같음 |
| SetPass / Draw | 101~195 / 137~346 |

- 평균 처리량은 충분하나 상위 1% 프레임이 평소의 5~6배. 체감 끊김의 1순위 후보 [추론]

2차 측정 (진단 0.1.1, 같은 PC·모드 구성, 마을·초반 던전) [관찰]

| 항목 | 결과 |
|---|---|
| 25ms 넘는 프레임 | 10초당 대체로 19~23개(약 0.5초마다 1번), 전투 집중 구간은 55~70개 |
| 그중 GC가 돈 프레임 | 386개 중 8개 (약 2%) |
| GC 실행 | 10초당 0~5번, 관리 힙 177~255MB |
| 끊김 없는 구간 | 모드 로드 직후 마을 대기, 타이틀 복귀 후 (10초당 0~2개) |
| renderScale 0.5 (픽셀 1/4) | 대기 화면 207fps(1.0) vs 205fps(0.5)로 차이 없음. 전투 구간 FPS는 해상도가 아니라 전투 양에 따라 오르내림 |

- **GC는 끊김의 주원인이 아님** [관찰]
- **이 PC·해상도에서 GPU 병목 아님.** 병목은 CPU 쪽(메인·렌더 스레드)이나 화면 출력 대기 [추론]. 해상도·2D 라이트 텍스처 축소 같은 GPU 최적화는 효과가 작을 것 [추론]. 후반 층·전체 화면 고해상도에서는 다를 수 있음 [미확인]
- 끊김은 게임 월드 안에서 약 0.5초 주기로 반복되는 CPU 작업일 가능성 [추론]

3차 측정 (진단 0.1.2, LXIUM만 설치, 다른 모드 4개는 `AddOnsDisabled`로 이동) [관찰]

| 항목 | 모드 5개 (2차) | LXIUM만 (3차) |
|---|---|---|
| 25ms 넘는 프레임 (10초당) | 대부분 19~23 | 대부분 0. 층 이동·로딩 구간만 1~19 |
| p99 | 30~37ms | 7~10ms |
| 평균 FPS | 147~207 | 131~180 |

- **0.5초 주기 끊김의 원인은 게임이 아니라 다른 모드** [관찰]. 게임 자체는 매끄러움
- 정적 분석 용의자: Sephiria QoL의 `RuntimeHost` 주기 루프 [추론]
  - `WorldLoop`: 작업 20여 개를 12조각으로 나눠 1/6초마다 실행 (`WorldSliceCount=12`, `WorldSliceInterval=0.1666`)
  - `PresentationLoop`: UI·입력 관련 작업 40여 개, `ScanInterval=2`
  - `BindLoop`: `BindInterval=1`
  - 일부 작업이 씬 전체 검색(`FindObjectsByType` 등)을 함
- 다른 모드의 매 프레임 코드(`Update`/`LateUpdate`)에는 주기적 고비용 호출이 없음. Mira의 `FindObjectsByType`는 F2/F3 입력 시에만 실행 [관찰]
- QoL 자체 계측: 채팅 명령 `/qol perf on | show | off` (`RuntimePerf`, `ScheduledWork`별 호출 수·평균·최대 시간). 주기 작업만 측정하고 Harmony 훅은 측정하지 않음 [관찰]
4차 측정 (진단 0.1.3, LXIUM + 모드 1개씩) [관찰]

| 함께 켠 모드 | 25ms 넘는 프레임 (10초당) | p99 |
|---|---|---|
| Sephiria QoL | 대부분 0, 로딩 구간만 1~7 | 7~13ms |
| Mira's Item Mod | 대부분 0, 로딩 구간만 2~10 | 8~13ms |
| CustomSkin | 대부분 0, 로딩 구간만 1~14 | 9~13ms |

- QoL·Mira·CustomSkin은 각각 단독으로는 0.5초 주기 끊김을 만들지 않음 [관찰]. QoL 주기 루프 용의는 해소
- SendMoney는 이 측정에서 빠짐 ("작은 모드"라 우선순위를 낮춘 판단 착오). 이후 정적 분석으로 원인 후보가 됨

**SendMoney 1.0.1 — 0.5초 주기 전체 검색** [관찰: 정적]

- `SendMoneyModRunner.Update`: 채팅 입력창에 연결 전(`isBound`가 false거나 `boundChatInput`이 null)이면 `scanTimer`가 0.5초 지날 때마다 `TryBindChatInputOnce` 호출
- `TryBindChatInputOnce`: `Object.FindObjectOfType<UI_ChatInput>()`. 씬의 모든 활성 오브젝트를 훑는 호출
- 게임 월드의 로드된 오브젝트는 약 36만 개 (`Loaded Objects now: 363184`, Player.log) [관찰]
- 10월 11일 장시간 플레이 로그(모드 4개)에 연결 성공 메시지 `UI_ChatInput 바인딩 완료`가 한 번도 없음 → 플레이 내내 0.5초마다 검색 [관찰]
- 채팅창(`UI_ChatInput : UIBase`)은 닫혀 있으면 비활성이고, `FindObjectOfType<T>()`는 비활성 오브젝트를 제외하므로 채팅을 열기 전에는 찾지 못함 [추론]
- 타이틀은 오브젝트가 적어 검색이 싸므로 끊김이 없었던 것과 맞음 [추론]
5차 측정 (진단 0.1.3, LXIUM + SendMoney만) [관찰]

| 항목 | 값 |
|---|---|
| 25ms 넘는 프레임 | 10초당 18~22, 세션 끝까지 지속 |
| 끊김 간격 p25/p50/p75 | 0.50 / 0.50 / 0.50초 |
| 그중 GC 동반 | 0 |
| p50 / p99 | 5~6ms / 25~28ms |

- **0.5초 주기 끊김의 원인은 SendMoney 1.0.1로 확정** [관찰]. 간격이 `scanTimer` 0.5초와 일치, 다른 모드 없음
- `FindObjectOfType` 1회 비용은 개발 PC·게임 월드(오브젝트 약 36만 개)에서 약 20~25ms [추론: p99 − p50]
- 사용자가 채팅창을 열었다 닫는 절차 후에도 끊김이 계속됐고 연결 성공 로그가 없음 [관찰]. 채팅을 연 시간이 0.5초 미만이었는지, 열어도 못 찾는지 [미확인]
- 게임의 채팅창 열기 경로: `PlayerInputController.HandleChat` → `UIManager.GetElement<UI_ChatInput>()` → `UI_ChatInput.Chat()` → `UIBase.Open` [관찰]
- 원인 제거 방법(모드 쪽): 게임의 `UIManager.GetElement<UI_ChatInput>()` 사용, 또는 `UI_ChatInput.OnOpened`에서 연결, 또는 비활성 포함 검색을 한 번만 수행
- `Player.log`는 실행마다 덮어써지고 `Player-prev.log`도 갱신되지 않을 수 있음 [관찰]. 진단 0.1.3부터 모드 폴더의 `LXIUM_diag.log`에 세션별(로드된 모드 목록 포함)로 이어 씀
- 다른 모드의 단축키: CustomSkin은 F7·F9(설정 가능, `Keyboard[Key]` 인덱서로 읽음), Mira's Item Mod는 F1~F3, QoL은 PageUp/PageDown·방향키·Tab·Enter·Alt 사용 [관찰]. 키 이름 속성(`get_f9Key`)만 검색하면 인덱서 사용을 놓침. LXIUM은 기본 단축키를 두지 않음

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
4. 최적화는 측정으로 확인된 병목만 다룸. 개발 PC 측정상 GPU 병목이 아니므로(8장) Unity 그래픽 설정 변경은 후순위. 코드 패치는 필드·설정 변경으로 안 될 때만 Harmony 사용
5. 다른 모드의 로드 순서나 패치를 전제하지 않음
6. 로그 접두사 `[LXIUM]`. 출력은 Unity 로그(`Player.log`), 진단은 모드 폴더 `LXIUM_diag.log`에도 기록
7. 설정은 모드 폴더 `config.json`. 없으면 실행 중 기본값으로 생성 (`docs/ai/addon.md` 5장). 키: `diagnostics`, `sendMoneyScanFix` (기본 모두 true)
8. 다른 모드 호환 패치 규칙
   - 대상 모드 이름·버전이 정확히 일치할 때만 적용 (`AddOnLoader.LoadedMods`의 `modName`, `modVersion`)
   - 필요한 타입·필드·속성을 리플렉션으로 확인하고 하나라도 다르면 적용하지 않고 로그만 남김
   - 판단에 필요한 게임 상태를 얻지 못하면 대상 모드의 원래 동작을 그대로 둠
   - 대상 모드의 기능은 유지하고 낭비만 막음. 기능을 대신 구현하지 않음
   - `config.json` 키로 끌 수 있어야 함
   - 현재 적용: SendMoney 1.0.1 (`src/SendMoneyScanFix.cs`). 채팅창이 UI 목록에 있고 비활성인 동안 `SendMoneyModRunner.scanTimer`를 매 프레임 0으로 되돌려 `FindObjectOfType` 검색을 막음. 채팅창이 열리면 손대지 않아 SendMoney가 원래대로 연결함

## 11. 분석 방법

- REA(`inspect_managed_artifact`)로 `Assembly-CSharp.dll`을 관리 PE로 분류하고 메타데이터를 확인함. REA `inspect_managed_members`는 64MB 고정 한도 초과로 본체와 QoL DLL에서 실패
- 대체로 .NET `System.Reflection.Metadata`로 IL을 읽기만 하는 덤프를 만들어 타입·메서드·호출·문자열을 추출. 타입·메서드 수가 REA 수치와 일치함
- `Player.log`(이전 실행 기록)로 로드 순서·그래픽 API를 확인

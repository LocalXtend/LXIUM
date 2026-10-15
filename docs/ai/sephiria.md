# 세피리아 분석 (AI용)

- 사람용 문서: `../human/sephiria.md`
- 렌더 설계: `sephiria-render.md` (이 문서의 사실 위에서 LXIUM 렌더를 설계)
- 대상: Sephiria (Steam 2436940), Unity 6000.3.21f1, Mono 백엔드. `Assembly-CSharp.dll` sha256 `c57a0dae…cf1510`
- 표기: [관찰] 바이너리·로그에서 직접 본 것, [추론] 관찰에서 이끈 것, [미확인] 아직 근거 없음
- 2026-10-15 새로 시작한 분석. 예전 LXIUM_sephiria 문서·커밋은 참고하지 않음

## 0. 분석 방법

| 도구 | 쓴 곳 | 근거 ID |
|---|---|---|
| REA CLI `rea-agents@6.5.0` (MCP 서버가 연결 시간 초과라 CLI로) | 관리 어셈블리 식별 | `ev_a3baafc1633b…` (inspect_managed_artifact, Assembly-CSharp) |
| 〃 `inspect-managed-members --type` | 모드 API 10개 타입 (메서드 99, 호출 312) | `ev_3510eae66b8b…` |
| 〃 | 렌더 관련 게임 타입 14개 (메서드 153, 호출 1039) | `ev_45e63c2aed01…` |
| 〃 | URP 2D `Renderer2D`·`Renderer2DData`·`LayerUtility` (메서드 73, 호출 639) | `ev_b8904d5947bd…` |
| Mono.Cecil IL 덤프 (작업용 임시 도구, 저장소에 없음) | 메서드 본문 읽기, 호출 지점 검색 | — |
| 에셋 파일 문자열 | 빌드에 든 셰이더 이름 | — |
| `Player.log` (엔진이 쓰는 줄) | GPU·그래픽 API·모드 로드 순서 | — |

- REA로 어셈블리 전체를 한 번에 보면 메타데이터 한도(64 MB) 초과로 거부됨 [관찰]. 타입을 지정해 나눠 봄
- 이 문서의 호출 관계는 REA 호출 간선과 IL 덤프가 둘 다 같은 것만 적음

## 1. 게임 구성

| 항목 | 값 |
|---|---|
| 폴더 | `Sephiria_Data/Managed/` (Mono), `MonoBleedingEdge/`, `D3D12/D3D12Core.dll`, `AddOns/` (모드), `AddOnsDisabled/` (사용자가 끈 모드를 옮겨 두는 곳. 로더는 안 읽음) |
| `boot.config` | `gfx-threading-mode=6`, `gc-max-time-slice=3`(점진 GC), `hdr-display-enabled=0` [관찰] |
| 그래픽 (이 PC) | Direct3D 11.0 (FL 11.1), Intel Iris Xe (i5-1235U), 창 1437x1000 [관찰: Player.log] |
| 주요 라이브러리 | URP 2D, TextMeshPro, Febucci TextAnimator, Mirror(네트워크), FMOD, UniTask, xNode, Visual Scripting, Steamworks |
| 개발 잔여물 | `MCPForUnity.Runtime.dll`, `OpenAI.Runtime.dll`, `Unity.Recorder.dll` [관찰]. 쓰임은 [미확인] |

## 2. 공식 모드 API (HorayModAPI)

게임 안에 모드 로더가 들어 있다. BepInEx·MelonLoader가 필요 없다 [관찰].

### 2.1 로드 순서

시점: 타이틀 화면에서는 아직 안 돌고, 이어하기·새 게임으로 게임 장면에 들어갈 때 처음 돎 (`Player-prev.log`에서 세이브 불러오기·서버 시작 뒤에 `[AddOnLoader]`) [관찰]. 그래서 모드의 계측도 그때부터 시작한다.

`GameDataLoader.Awake` [관찰]

1. `InternalMod.Register()` — 게임 자신도 모드 API를 씀: `OnPlayerEvade`에 회피 시 `EVASIONCOOLDOWN` 마법 충전 효과를 등록
2. `AddOnLoader.LoadAll()` (예외는 잡아서 로그)
3. 데이터베이스 `Initialize` 26개 + `MiniscriptGlobal.Initialize`. 그중 8개가 끝에서 `HorayModAPI.LoadMod<이름>Database()` 호출 (아래 표)
4. `HorayModAPI.NotifyAllModDatabasesReady()`
5. 풀(오브젝트 풀링) 준비 등

### 2.2 AddOnLoader

| 단계 | 동작 |
|---|---|
| 경로 | `Path.GetFullPath(Application.dataPath + "/../AddOns")`. 없거나 비면 로그만 남기고 끝 |
| 폴더마다 `TryLoadMod` | ① `metadata.json`을 `JsonUtility`로 읽음 (`modName`, `modVersion`, `modAuthor`, `dllFile`, `entryClass`). 없으면 경고 후 폴더 이름을 `modName`으로 ② DLL: `dllFile`, 비면 폴더의 `*.dll` 중 첫 번째(여럿이면 경고) ③ `Assembly.LoadFrom` ④ 타입: `entryClass`를 `GetType`, `HorayModBase` 하위가 아니면 경고 후 자동 탐색(추상 아닌 첫 하위 클래스) ⑤ `Activator.CreateInstance` → `LoadMod(Metadata)` → `OnModLoaded()` |
| 끝 | 로드 수 로그. 1개 이상이면 `ModEnvironment.NotifyModDetected("AddOns xN")` |
| 해제 | `UnloadAll` → 각 모드 `UnloadMod` → `OnModUnloaded` |

- 각 단계 실패는 로그 후 그 모드만 건너뜀 [관찰]
- 다른 모드의 `metadata.json`에 있는 `modFile`·`modClass`는 로더가 읽지 않음. 그 모드들 자체 기반 DLL(HarmonyBase, MiraModBase)이 읽는 것으로 보임 [추론]
- 의존 DLL(`Libs/0Harmony.dll` 등)을 찾아 주는 코드는 로더에 없음 [관찰]. 각 모드가 스스로 해결 [추론]

### 2.3 HorayModBase

| 멤버 | 내용 |
|---|---|
| `protected virtual void OnModLoaded()` / `OnModUnloaded()` | 모드가 재정의 |
| `public void LoadMod(Metadata)` / `UnloadMod()` | 로더가 부름 |
| `Metadata` | `modName`, `modVersion`, `modAuthor` |

### 2.4 HorayModAPI (정적 이벤트)

`WORK_IN_PROGRESS_README` = "The MOD System is still under development. Most features are not work properly yet." / `https://teamhoray.com/mod-api` [관찰]. 이벤트는 `SafeInvoke*`로 감싸 예외를 이벤트별로 잡음.

| 이벤트 | 부르는 곳 [관찰] |
|---|---|
| `OnLoadMiracle/Weapon/Status/Item/Keyword/Passive/Costume/PropDatabase` | 각 `<이름>Database.Initialize` 끝 |
| `OnAllDatabasesReady` | `GameDataLoader.Awake` |
| `OnLocalizationReady(HorayModLocalizationContext)` | `GameDataLoader` (`AddText(언어, 키, 값)` → `LocalizationManager.AddModText`) |
| `OnStartGameServerside/Clientside` | `HorayNetworkManager.OnStartServer/OnStartClient` |
| `OnStartSessionServerside/Clientside(bool isSaved)` | `HorayNetworkManager.NewGame`, `DungeonManager.InvokeStartSessionClientside` |
| `OnFloorAllocatedServerside/Clientside(guid, floorName, FloorGenerator)` | `DungeonManager.FloorAlloc`, `FloorGenerator.Generate` 코루틴 |
| `OnPlayerEvade(PlayerAvatar, DamageInstance)` | `PlayerAvatar.HandlePlayerEvade` |
| `OnGetDebuff` (Func, 디버프 프리팹 교체) | `UnitAvatar` |
| `GridInventoryStart/EndPermission` | `GridInventory.GetPermission/ReleasePermission` |
| `RegisterNetworkPrefab(s)` / `UnregisterNetworkPrefab` | 모드가 부름. `NetworkIdentity` 필수, `NetworkManager` 초기화 뒤에만 |

**렌더링 관련 훅은 없다** [관찰]. 렌더 최적화는 엔진 공개 API(URP 에셋·플레이어 루프 등)나 런타임 패치(Harmony)로 해야 한다.

### 2.5 ModEnvironment (모드 감지)

- 시작 시와 어셈블리 로드 때 이름이 `MelonLoader`, `BepInEx`, `UnityModManager`, `0Harmony`, `SPMod`로 시작하는 어셈블리를 찾음 [관찰]
- 찾거나 AddOn이 1개 이상 로드되면 `IsModded = true`, `CrashReportHandler.enableCaptureExceptions = false` (Cloud Diagnostics 예외 업로드만 끔, 네이티브 크래시 수집은 유지) [관찰]
- 즉 AddOn을 쓰는 것만으로 이미 "모드 사용"으로 표시됨. Harmony를 안 써도 달라지지 않음 [추론]

### 2.6 이 PC에 설치된 모드

| 모드 | 버전 | 방식 | 단축키 [관찰: IL] |
|---|---|---|---|
| CustomSkinMod (Taehyun) | 1.1.9 | HarmonyBase + Harmony·MonoMod | 키 인덱서 사용 (키는 설정값) |
| Mira's Item Mod (Mira) | 1.1.5 | MiraModBase + Harmony | F1, F2, F3 |
| Sephiria QoL (Xetsumei) | 1.55.0 | Harmony | PageUp, PageDown, 방향키, Alt, Tab, Enter |
| SendMoney (Taehyun) | 1.0.1 | `AddOnsDisabled/`에 있어 로드 안 됨 | — |

- 모두 입력은 `UnityEngine.InputSystem.Keyboard.current` 사용 [관찰]. LXIUM도 같은 방식, 기본 F9·F10
- 게임 자체 키보드 직접 참조: 방향키, Enter, Esc, Tab, Space, Shift, Ctrl, E, Q, `[` `]` `;` `'` [관찰]. F키는 없음

## 3. 렌더링 시스템

### 3.1 파이프라인

- URP **2D 렌더러** (`Renderer2D`). Unity 6.3이라 RenderGraph 경로만 있음 [관찰: URP 2D IL]
- 2D 조명 텍스처 크기 = 카메라 대상 크기(렌더 배율 적용 후) × `Renderer2DData.m_LightRenderTextureScale`, 최소 1 [관찰: `Renderer2D.CreateResources`]. 노멀·그림자 텍스처도 같은 크기
- 조명 텍스처는 정렬 레이어를 묶은 "레이어 배치"마다 만듦 (`LayerUtility.CalculateBatches`, 조명이 같은 레이어끼리 묶음) [관찰]. 배치 수는 [미확인] → 0.1.0 probe
- 렌더 배율은 URP 공통 경로(`UniversalCameraData.cameraTargetDescriptor`)로 2D에도 적용되고 조명 텍스처도 같이 줄어듦 [관찰·추론]. 화면 공간 오버레이 UI는 렌더 배율과 무관 [추론: 오버레이 UI는 파이프라인 밖에서 그림. HDR 출력일 때만 URP가 그림 (`rendersOverlayUI`)]
- 런타임 설정값 (이전 실행 `Player-prev.log`에 남은 값 [관찰]. 0.1.0 probe로 다시 확인)

| 대상 | 값 |
|---|---|
| URP 에셋 | HDR 켬, MSAA 끔, `renderScale` 1, 업스케일 필터 Auto, SRP Batcher 켬, 동적 배칭 끔, 볼륨 갱신 EveryFrame, 색 보정 LDR LUT 32 |
| Renderer2DData | 조명 텍스처 배율 **1** (URP 기본 0.5), 블렌드 스타일 4, 조명 텍스처 최대 16, 깊이·스텐실 켬, 투명 정렬 CustomAxis |
| 타이틀 | Main Camera(직교 5.63, HDR, 후처리) + WaterCamera(1920x640 RT, MSAA) |

- 셰이더 객체 86개, 모두 에셋 파일 안(번들 없음), 플랫폼은 D3D11 하나 [관찰: UnityPy, `sephiria-render.md` C장]
- 빌드에 든 셰이더 [관찰: 에셋 문자열]
  - URP 후처리: Bloom, UberPost, FinalPost, LutBuilderLdr/Hdr, Edge Adaptive Spatial Upsampling(FSR1 EASU), Scaling Setup, TemporalAA, SMAA, 피사계 심도 등
  - 2D: `Universal Render Pipeline/2D/Sprite-Lit-Default`, `Sprite-Unlit-Default`, `Sprites/Default`, `Sprites/ScreenBased`, Particles/Unlit
  - Shader Graph: `Sprite-Lit-OutlineHDR`(+Animate), `Sprite-Lit-EmissionMask`, `Sprite-Lit-HDRColorOverlap5`, `Sprite-Lit-OneColored`, `Sprite-Lit-SpriteWithFog`, `Sprite-Unlit-*HDR*` 계열, `Sprite-Additive`, `Sprite-Premultiply5`, `Sprite-Hide`, `Sprite-ArkShrineLight`, `FS-Distortion`, `FS-Glitch`, `Fog`, `TitleWaterReflection`
  - TMP: Distance Field, Mobile/Distance Field, Sol/Bitmap, Sprite
  - HDR 색 셰이더가 많아 HDR + 블룸으로 빛 번짐을 내는 구조 [추론]

### 3.2 카메라와 월드

| 클래스 | 하는 일 [관찰] |
|---|---|
| `GameCamera` (싱글턴) | 메인 `Camera`, `Volume`(기본·세피아 프로필 교체), 카메라 주변광 `Light2D`, 안개·눈·비 제어, `FireSparkSpawner`, 분위기 스프라이트, `ParallaxScrollLayer[]`, `TargetTracker`(추적·줌), 흔들림, 화면 효과(방사 왜곡·글리치·검은 화면) |
| `DayCycle.LateUpdate` | 전역 `Light2D` 색 = 하루 그라데이션 × 사용자 색(`MoveTowards`), 배경 별·안개 스프라이트 색, 안개 재질, **`backgroundCamera`** 배경색 → 카메라가 둘 이상 [관찰: 필드]. 배경 카메라의 종류(Base/Overlay)는 [미확인] |
| `ParallaxScrollLayer.LateUpdate` → `Refresh` | 카메라 영역을 덮도록 배경 스프라이트 격자를 만들고 지움(`Background` 정렬 레이어). 매 호출 `HashSet` 새로 할당 [관찰: REA newobj] |
| `FogControl.Update` | 안개 스프라이트에 `MaterialPropertyBlock`(`_CameraPos`, `_Alpha`, `_FogSpeed`) 매 프레임 설정 |
| `RainImage.Update` | 재질 `_Speed` 매 프레임 설정 |
| `RainControl`/`SnowControl` | 날씨 오브젝트 켜고 끔 + `DayCycle` 외부 색·배경색 덮어쓰기 + FMOD 환경음 |
| `SunAndMoon.Update` | 해·달 회전, 렌즈 플레어 스프라이트 알파 |

### 3.3 캐릭터·이펙트

| 클래스 | 하는 일 [관찰] |
|---|---|
| `Animator2D_Basic` | 자체 스프라이트 플립북 애니메이터. 오브젝트마다 `Update`에서 타이머·상태 전이·프레임 이벤트·FMOD 사운드, 프레임이 바뀌면 `SetSprite` |
| `Animator2D_Light` | 위의 변형. 스프라이트 대신 `Light2D.lightCookieSprite`를 바꿈 (픽셀 아트 쿠키 조명) |
| `SpriteFx` (풀) | 이펙트. `Update`에서 부모 따라가기, 생성 시 `LightAnimator` 초기화 |
| `LightAnimator` / `WavingLight2D` / `SimpleAnimator2D_Light` | 조명 색 그라데이션 / 바깥 반경 흔들기 / 쿠키 플립북 — 모두 매 프레임 `Light2D` 속성 변경 |
| 유닛·탄 | `SpriteRenderer` 직접 조작 299곳, `SortingGroup` 36곳, `BulletRenderer` 하위 5종 |
| 파티클 | `ParticleSystem` 51곳, VFX Graph는 2종(`DarkCloudObject`, `VfxFadeDestroy`) |
| 타일 | `Tilemap` 154곳 |

게임 코드에 `RenderTexture`·`CommandBuffer`·`Graphics.Blit`·렌더러 기능 직접 사용은 없음 [관찰: 0건]. 렌더는 전부 URP 2D 기본 경로에 맡김.

### 3.4 UI·텍스트

- uGUI + TextMeshPro: `TMP_Text.text` 설정 588곳, TextMeshPro 참조 375곳 [관찰]. Febucci TextAnimator 사용
- `TMPGlitchEffect`는 TMP 메시를 직접 고침 (`Mesh.Set*`)
- HUD 일부가 `Update`에서 `String.Format`으로 문자열을 만듦 (`UI_MpBar`, `UI_HUDMapViewer`, `UI_TreeShopPanel` 등) → 매 프레임 할당 [관찰]. TMP는 같은 문자열이면 메시를 다시 만들지 않음 [추론: TMP 일반 동작]
- 캔버스 렌더 모드·개수는 [미확인] → probe
- 아티팩트(장비 아이템)는 `GridInventory`의 `Charm_*` 객체이고 화면에는 UI 아이콘으로 그려짐 [추론]

### 3.5 스크립트 규모

- `Update`를 가진 타입 646, `LateUpdate` 99, `FixedUpdate` 0 [관찰]. 물리는 자체 `TopdownRigidbody`·`TopdownSpatialHash` [관찰: 초기화 호출]
- `Update`/`LateUpdate` 안 할당 지점이 많은 타입: `WeaponSimple_SwordAndShield`(문자열 결합 11곳), `UI_TreeShopPanel`, `UI_CharacterStatusPanel`, `Unit_RootDemon` 등 [관찰]

### 3.6 화면 설정

- `DisplayManager.SetVSyncAndLimitFPS(vsync, limitFPS)` → `QualitySettings.vSyncCount`, `Application.targetFrameRate = limitFPSValues[limitFPS]` [관찰]
- `DisplayManager.SetScreenResolution` → `Screen.SetResolution`

## 4. 측정

| 실행 | 장면 | 결과 [관찰] |
|---|---|---|
| 이전 실행 (`Player-prev.log`, 1437x1000 창, VSync 끔, 제한 없음) | MainWorld | 180.9 fps, p50 4.6 ms / p99 12.8 ms, draw 155, SetPass 105 |
| 〃 | Title | 125.7 fps, draw 196, SetPass 133 |

- 이 PC(Iris Xe)에서는 이미 높다. 목표 기기(UHD 620, EU 24개 ≈ Iris Xe의 1/3~1/4)에서는 GPU 비중이 커짐 [추론]
- 0.1.0부터 단계별 메인 스레드 시간을 같이 기록 (`sephiria-render.md` 3장)

## 5. 아직 모르는 것 (0.1.0 probe로 확인)

- 실제 플레이 장면의 카메라 수·종류, 직교 크기, 스프라이트 PPU (텍셀 격자 렌더 B1의 배율 결정)
- 2D 조명 수·종류·대상 레이어, 레이어 배치 수
- 볼륨 프로필 구성 (블룸 강도·임계값·다운스케일)
- 캔버스 렌더 모드 (오버레이면 렌더 배율에서 UI가 원래 해상도로 남음)
- 프레임 시간이 GPU 대기(TimeUpdate)와 스크립트(Update/LateUpdate)·렌더 제출(PostLateUpdate) 중 어디에 쌓이는지

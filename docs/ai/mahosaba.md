# 마법소녀의 마녀재판 분석 (AI용)

- 사람용 문서: `../human/mahosaba.md`
- 렌더링 설계: `mahosaba-render.md`
- 대상 프로젝트: `optimizeMods/LXIUM_mahosaba/`
- 이 문서의 사실은 2장의 빌드를 정적 분석(실행 없음)해서 얻음. 게임 업데이트 후에는 2장의 식별값을 먼저 비교하고, 다르면 해당 사실을 다시 확인함
- 표기: **[관찰]** 바이너리·에셋에서 직접 확인 / **[추론]** 관찰에서 이끌어 낸 판단 / **[미확인]** 아직 증거 없음
- `ev_…`는 REA 근거 ID(앞 16자리). 11장 참고

## 1. 결정 사항

| 항목 | 결정 |
|---|---|
| 로더 | BepInEx 6 IL2CPP (be.785 이상). 애드온 변형 `bepinex6`, 경로 `BepInEx/plugins/LXIUM_mahosaba/` |
| 패치 방식 | 런타임만 (Harmony + Unity API + 엔진 내부 호출). 에셋 번들 교체 금지 (한글 패치가 번들을 교체함, 3장) |
| 게임 로직 | 스크립트 진행·세이브·선택지에 관여하지 않음. 렌더 경로와 프레임 제한만 바꿈 |
| 패치 대상 | IL2CPP가 같은 기계어를 여러 메서드가 나눠 쓰게 합침(ICF). **주소가 한 메서드에만 쓰이는 것만 패치** (9.2) |
| 기본값 | 화면이 바뀌지 않는 최적화만 켬. 화질이 바뀌는 것은 설정으로만 |

## 2. 분석 대상 식별값

| 항목 | 값 |
|---|---|
| 게임 | 魔法少女ノ魔女裁判 (Magical Girl Witch Trials), Steam app 3101040, buildid 21793620 |
| 엔진 | Unity 6000.0.48f1, IL2CPP, 64비트, 메타데이터 v31 |
| `boot.config` build-guid | `1ddf986c56ce4bc4947b3b9c2c7de488` |
| `GameAssembly.dll` SHA-256 | `d19a9d98abe1f5d120588f23f7fe0645b8b94a2777517f90338d640e0e934506` |
| `global-metadata.dat` SHA-256 | `2ad884295e77d94d40dfe134392ab69fd8b6a94ca6bbd3b27e84cd3aa2f90fe5` |
| 그래픽 API | 목록 D3D11, D3D12, Vulkan, OpenGLCore (첫 항목 D3D11이 기본) [관찰: BuildSettings] |
| `boot.config` | `gfx-enable-gfx-jobs=1`, `gfx-threading-mode=6` |
| 렌더 파이프라인 | URP (Universal Renderer, Forward). 2D Renderer 아님 [관찰] |
| 노벨 엔진 | Naninovel 1.20 (빌드 2025-04-26) [관찰: `EngineVersion`] |
| 개발사 확장 | `GigaCreation.NaninovelExtender.*` (ExtendedActors, Cameras, Effects …), 게임 코드 `WitchTrials.*` (Assembly-CSharp) |
| 기타 | UniTask, LitMotion, TextMeshPro, LeTai TrueShadow, MPUIKit, 2D Animation(SpriteSkin), Timeline, Addressables |
| 개발 PC | Intel Iris Xe (i5-1235U), 1920x1080 60Hz |

## 3. 한글 패치와 호환 [관찰: 파일 시각]

- 설치일(2026-10-14)과 다른 2026-02-13 시각의 번들 28개가 한글 패치 파일
  - `general-localization-zhhans-*` (scripts 23, text 1): 중국어 간체 자리에 한국어 텍스트
  - `general-data`, `general-sprites`(52 MB), `naninovel-spawn`(1 GB), `naninovel-ui`(1.8 MB)
- 이 문서의 UI·연출 수치(7장)는 패치된 번들에서 읽은 값
- 글꼴: `UIConfiguration`에 `zh-Hans` → `SourceHanSansKR` 매핑. TMP 글꼴은 전부 Dynamic 아틀라스 (SourceHanSansKR 1024² 단일 아틀라스, multi-atlas 꺼짐) [관찰]
- LXIUM은 번들을 건드리지 않으므로 패치와 충돌하지 않음 [추론]. 게임 업데이트로 패치 파일이 덮이면 한글이 사라지는 것은 패치 쪽 문제

## 4. URP 설정: 3D 기본 템플릿을 그대로 씀 [관찰: globalgamemanagers.assets]

| 항목 | 값 | 이 게임에서의 의미 |
|---|---|---|
| 에셋 이름 | `URP-HighFidelity` / `URP-HighFidelity-Renderer` | Unity 3D 템플릿 기본 에셋 이름 그대로 |
| MSAA | 4x | 대상 RT가 없는 카메라(메인)는 4x. RT 카메라는 RT의 값(무대 2x, 캐릭터 1x) [추론: URP 동작] |
| HDR | 꺼짐 | — |
| Render Scale / 업스케일 | 1.0 / Auto | — |
| 렌더러 기능 | **SSAO** (DepthNormals 소스, 다운샘플 없음, 블러 High, 강도 0.5) | 렌더러를 쓰는 **모든 카메라**가 매번 실행 |
| 중간 텍스처 | **Always** | 필요 없을 때도 중간 RT + 복사 |
| 그림자 | 메인 라이트 4096, 캐스케이드 4, 소프트 / 추가 라이트 4096 | 그림자 켠 라이트가 없어 실제로는 거의 안 씀 [추론: 6장] |
| SRP Batcher / GPU Resident Drawer | 켜짐 / 꺼짐 | — |
| 볼륨 | 기본 프로필의 모든 효과 0(Bloom 0, Tonemapping None 등). 씬·번들에 Volume 컴포넌트 없음 | 포스트 프로세싱을 켜도 보이는 효과는 FXAA뿐 [추론] |

- 품질 단계 6개: VSync는 Very Low·Low·Ultra 0, Medium·High·Very High 1. 단계별 파이프라인 에셋 교체 없음 [관찰]
- 결론: 3D 템플릿 기본값(MSAA 4x, SSAO, 중간 텍스처 Always)을 2D 노벨 화면에 그대로 씀 [추론]. 이 비용은 **카메라 수만큼 곱해짐** (5·6장)

## 5. 캐릭터 렌더링

### 5.1 액터 종류 [관찰: `CharactersConfiguration`]

| 구현 | 수 | 대상 |
|---|---|---|
| `LayeredCharacterExtended` (레이어 합성) | 13 | 주연: Ema, Hiro, Meruru, Hanna, Sherry, AnAn, Leia, Coco, Miria, Margo, Noah, Nanoka, Alisa |
| `DicedSpriteCharacterExtended` (다이스 스프라이트) | 18 | Yuki, Warden, Jailer A~C, Creature* 13 |
| `NarratorCharacter` (이름만) | 10 | Unknown, EmaFake 등 |

배경은 `WitchTrials.Views.WitchTrialsBackground : SpriteBackgroundExtended` (스프라이트 액터) [관찰].

### 5.2 레이어 캐릭터 프리팹 [관찰: `naninovel-characters/*.bundle`]

| 항목 | 값 |
|---|---|
| 렌더 방식 | `LayeredCharacterBehaviour.renderCamera` 지정 → **카메라 모드**. Naninovel 툴팁: "Less optimized" |
| `animated` | **1** (13명 전부). 툴팁: "Whether the actor should be rendered every frame" |
| `RenderCanvas.Size` | 15 x 30 유닛 → PPU 100이면 **1500 x 3000 px** (4.5 MP, 1080p 화면의 2.2배) |
| SpriteRenderer | 43~126개, 기본 표정에서 켜진 것 9~17개 |
| 아틀라스 | 1장. 4096x8192~8192x8192 BC7, AnAn 8192x16384. **밉맵 없음**. 8192² BC7 = 64 MB |
| 카메라 | 직교, 컬링 마스크 0(실행 중 전용 레이어로 바뀜), `UniversalAdditionalCameraData` 포스트 꺼짐·렌더러 기본(-1 = HighFidelity) |
| 애니메이션 | Animator 없음. 말하기 이벤트(`onStartedSpeaking` 등) 비어 있음. 표정·포즈는 `compositionMap`(67~136개 키) 조합 |
| 재질 | `Naninovel Extender/Default`, `/Multiply`, 스텐실 마스크 변형(`#Masked_Ref1`, `#Mask_Ref1_CutoffLow`) |
| `LayerModifier` | 레이어 렌더러 목록을 들고 `ModifyLayers(layer)`로 GameObject 레이어를 카메라 컬링 레이어로 바꿈 [관찰: ev_7d6dcc76fdef656c] |

### 5.3 그리는 경로 [관찰: REA Ghidra 디컴파일]

1. `LayeredActor.RenderAppearance` — `perceivedVisibility`(+0xB1)가 참일 때만, `Behaviour.Render(메타데이터 PPU, appearanceTexture)` 호출. 반환 RT가 바뀌면 이전 RT를 `RenderTexture.ReleaseTemporary` 하고 `TransitionalRenderer.MainTexture`에 새 RT를 넣음 (ev_396046bcba1f543a)
2. `LayeredActorBehaviour.Render` → `Drawer.DrawLayers` (ev_a3e3f623f6f7abd1)
3. `LayeredDrawer.DrawWithCamera` — 카메라를 **끈 상태로** 두고, RT를 준비해 `targetTexture`에 넣은 뒤 엔진 내부 호출 `Camera::Render_Injected`로 **직접 렌더** (ev_9fe04d4d38a61ec2)
4. `LayeredDrawer.PrepareRenderTexture` — 크기가 같으면 재사용, 아니면 `RenderTexture.GetTemporary(w, h, 24)` (깊이·스텐실 24, MSAA 없음) (ev_5ba4089c7240b185)
5. `TransitionalSpriteRenderer.Update` — **매 프레임**: `ShouldRender()` → 스프라이트 재질 불투명도 설정 → `PrepareRenderTexture` → `RenderToTexture(renderTexture)` → `Graphics.DrawMesh` (깊이 패스가 켜져 있으면 한 번 더) (ev_650744b26b6f65ea)
6. `TransitionalRenderer.ShouldRender` — "메인 텍스처가 있고 직전 프레임 불투명도 > 0"만 검사. **바뀌었는지는 보지 않음** (ev_6cf33aa86fef8d65)
7. `TransitionalRenderer.RenderToTexture` — 전환 셰이더(`Naninovel/TransitionalTexture`, 키워드 29개)로 메인·전환 텍스처를 섞어 RT에 그림. 블러 강도가 있으면 `BlurFilter.BlurTexture` 추가 (ev_7aca6fcccbb640e6)
8. `TransitionalSpriteRenderer.PrepareRenderTexture` — 렌더 크기(메인 텍스처 기준)가 같으면 재사용, 아니면 `GetTemporary(w, h, 0)` 후 재질에 연결·메시 재생성 (ev_b73807cb15d72912)

### 5.4 캐릭터 하나가 벅찬 이유

캐릭터 하나가 보이는 동안 프레임마다 생기는 일:

| 단계 | 크기 | 근거 |
|---|---|---|
| (a) URP 카메라 1회 전체 렌더: 컬링 + 렌더러 준비 + **SSAO** (DepthNormals 프리패스 + AO + 블러) + **중간 텍스처 복사** | 1500x3000 | 경로 [관찰], SSAO·중간 텍스처가 이 카메라에도 걸림 [추론: 4장 설정 + 카메라 렌더러 -1] |
| (b) 전환 셰이더로 RT 복사 | 1500x3000 | 매 프레임 [관찰: 5.3-5,6] |
| (c) 화면에 쿼드 그리기 | 화면 크기 | 메인 카메라, 재판 중에는 무대 카메라 2대도 (6.2) |
| (d) CPU: `Camera.Render`마다 SRP 컬링·렌더 그래프 준비 | — | [추론] |

- (a)가 매 프레임인지: `animated=1`이 그 의미이고 툴팁도 그렇게 말함 [관찰]. 이 플래그를 읽어 매 프레임 `RenderAppearance`를 부르는 호출자는 가상 호출이라 정적으로 찾지 못했으나, 2차 측정에서 프레임당 11회 합성으로 확인 [관찰: 10장]. 카메라 밖 단상의 캐릭터도 합성됨
- (a)+(b)만으로 4.5 MP 화면 밖 패스가 최소 2~3번(SSAO 블러 포함 시 그 이상). 1080p 화면 하나(2.07 MP)를 몇 번 다시 그리는 것과 같음 [추론]
- 아틀라스가 8K BC7에 밉맵이 없어, 처음 등장할 때 64~128 MB 업로드, 공유 메모리를 쓰는 내장 GPU에서 메모리 대역 압박 [추론]
- 얼굴·옷이 실제로 바뀌는 순간은 드묾 (Animator·립싱크 없음, 조합 변경으로만 바뀜). **결과가 같은 렌더를 매 프레임 반복**하는 것이 핵심 낭비 [추론]

### 5.5 다이스 캐릭터·배경

- `DicedSpriteActor.RenderToTexture(Sprite)` — 외형이 바뀔 때만 다이스 메시를 RT에 그림 (ev_1b591d73f29e3a9d). 아틀라스는 1020x6868~3264x4012 DXT5 Crunched, 밉 없음 [관찰]
- 그래도 (b) 전환 셰이더 RT 복사는 매 프레임 [관찰: 5.3-5]
- 배경: `naninovel-backgrounds` 253장 대부분 4096x2048 DXT1 Crunched(정지 화면 172장 포함), 밉 없음. 1장은 RGB24 [관찰]. 스프라이트 액터도 `TransitionalSpriteRenderer`를 쓰므로 **4096x2048(8.4 MP) 복사가 매 프레임** [관찰: 경로 / 추론: RT 크기 = 텍스처 크기]

## 6. 게임 자체 렌더링 (두 번째 의심 원인)

### 6.1 Boot 씬 구성 [관찰: level0]

```
MainCamera (직교, 마스크 Default|UI, 포스트 켬, FXAA, MSAA 허용, 그림자 켬) → 화면
PerspectiveStage
├─ ScreenCanvas (Screen Space-Camera, order -1)
│   └─ StageScreenContainer_1/2 (CanvasGroup, Quadrilateral 마스크) → RawImage(RenderTexture_1/2)
└─ Stage/Cameras (StageCameraController)
    └─ CameraContainer_1/2 → Camera (원근 FOV 30, 마스크 Default, HDR, 포스트 켬, FXAA, 그림자 켬)
                              → RenderTexture_1/2 (1920x1080, MSAA 2x, D24S8, 밉 없음)
```

### 6.2 재판 3D 무대

- 법정 프리팹 `Court`, `Court_Final` [관찰: general-prefabs]
  - URP Lit 재질 (바닥·계단: 노멀·메탈릭 맵, 2048² / 벽: 알파 테스트) — 빛 계산하는 3D 재질
  - 스포트라이트 2개, 강도 2000, 그림자 없음
  - 불 연출: `FireContainer` 12개, 셰이더 `SH_Vefects_VFX_URP_Fire_Flames_01`(패스 4, 노이즈 텍스처 여러 장), 파티클 시스템 25개 (초당 1개, 수명 1.01초, 크기 20~25, prewarm) → 입자 수는 적고 **큰 쿼드의 픽셀 셰이더 비용**이 핵심 [추론]
  - 불 셰이더 화면 패스(D3D11 ps_5_0 디스어셈블) [관찰]: 픽셀마다 텍스처 8회 = 깊이(`_CameraDepthTexture`, 깊이 페이드) + 왜곡 노이즈 + 노이즈 2장 + 마스크 + `_TextureSample2` + 비어 있는 `_ColorGradientMask`·`_DistortionMaskTexture`(기본 흰색이라 늘 1). 알파 블렌드(SrcAlpha, OneMinusSrcAlpha), 깊이 쓰기 없음
  - 식: `t = 시간 × _WindSpeed`, `d = 왜곡노이즈 × _DistortionAmount × 0.1`, `noise = sat(pow(n1·n2, _Noises_Power) × _Noises_Multiply) × _NoisesOpacityBoost`, `mask = sat(pow(마스크(uv + d), _Mask_Power) × _Mask_Multiply)`, `a = mask − noise × 그라데이션(v + 0.4)`, 알파 = `sat(sat(깊이페이드 × (a − _Dissolve)) × 정점알파)`, 색 = `_ColorTop × 정점색 × a × _EmissiveIntensity(55)`
  - `_TextureSample2` = `T_VFX_Gradient_Up_Down_01`: 256², 클램프·점 필터, 가로로 일정한 S자 곡선 (`0.4v + 0.6·smoothstep(v)`와 오차 0.01 안팎). 마스크 `T_VFX_Directional_Dust_01`는 54%가 0 [관찰]
  - 2·3차 측정 장면(대화 중 법정)의 스크린샷에는 불 효과가 없음. 화면의 불꽃 무늬는 스테인드글라스 텍스처 [관찰]. 불은 연출 때만 켜짐 [추론: `ChangeFireFxActivity`]
  - 단상 `Court_Stand` 13~14개: 투명 Lit (premultiply)
  - `Court.ChangeFireFxActivity`: 불 오브젝트 SetActive + 스포트라이트 색을 불빛 색(1, 0.4, 0.2)으로 (ev_336c2b33c95f2a46)
- `StageCameraController.SetCameraEnabled(bool)`이 **두 카메라를 함께** 켜고 끔. `Present`/`Dismiss`가 이것만 부름 (ev_1c598cc193990d13)
- 화면 분할·표시는 `StageScreenController`가 화면 컨테이너의 CanvasGroup 알파와 Quadrilateral 모양으로만 바꿈 [관찰: ISIL] → **한 화면만 보일 때도 두 카메라가 모두 1920x1080을 그림** [관찰: 10장 2차 `stageSkip`]
- 무대 카메라 컬링 마스크가 Default라 액터 레이어(0)도 포함 → 캐릭터 쿼드가 무대 카메라에서도 그려짐 [관찰: 마스크 / 추론: 실제 배치]

### 6.3 결론

- 재판 중 프레임마다 URP 전체 렌더가 **메인 1 + 무대 2 + 보이는 레이어 캐릭터 수**만큼 돌고, 각각 SSAO·중간 텍스처(4장)를 실행 [추론]
- 무대 카메라는 Lit 셰이딩 + 불 셰이더 + SSAO + MSAA 2x + FXAA를 1920x1080 두 장에 함 → **게임 자체 렌더 구성도 큰 원인** [추론]
- 입자(파티클)는 수가 적어 원인이 아님 (사용자 판단과 같음). 단 불 셰이더의 픽셀 비용은 별개 [추론]

## 7. GUI·텍스트

| 항목 | 내용 |
|---|---|
| UI 카메라 | `UseUICamera=0`. UI 캔버스는 Screen Space-Camera로 **메인 카메라**에 그려짐 [관찰] → UI도 메인 카메라의 MSAA 4x·SSAO·중간 텍스처·FXAA 비용을 같이 냄 [추론] |
| 캔버스 수 | UI 번들 88개 (renderMode 값: Overlay 14, Camera 19, World 55), 연출 번들 13개 [관찰]. World 값은 대부분 상위 캔버스를 따르는 중첩 캔버스로 봄 [추론] |
| TrueShadow | UI 번들 390개 (켜짐 278). 257개는 메뉴 라벨(`PauseOrUnpause`+TMP), 83개는 `Animation` 붙은 TMP 라벨, 블러 크기 20 [관찰] |
| TrueShadow 동작 | 그림자 텍스처는 내용이 바뀔 때만 다시 만듦 [추론: 라이브러리 구조]. 다만 `ShadowRenderer.LateUpdate`가 그림자마다 매 프레임 돎. 화면을 숨길 때 캔버스만 끄면(GameObject는 켜짐) 계속 돎 [추론] → CPU 비용 [미확인] |
| 대사 출력 | `RevealableTextModified`(TMP) 1개 + `RevealFader`/`RevealClipper`. 그림자는 대사창 배경 Image에만(정적) [관찰] |
| 글꼴 | TMP Dynamic 아틀라스 → 처음 나오는 글자마다 래스터화·텍스처 업로드 [관찰: 모드 / 추론: 순간 끊김] |
| FXAA | 메인 카메라 포스트의 FXAA가 글자 가장자리를 약간 흐림 [추론] |

- 결론: GUI·텍스트가 **불필요하게 매 프레임 다시 그려지는 증거는 없음**. 비용은 UI가 3D 템플릿 설정의 메인 카메라에 실려 있는 데서 나옴 [추론]

## 8. 프레임 제한·해상도

- 설정 `CameraConfigurationExtended`: `TargetFpsValues [60, 30]`, 기본 `TargetFpsLevel 0`(60), 해상도 목록 2560x1440~1280x720, 기본 `ResolutionLevel 0`, 전체 화면, 포스트 프로세싱 켬 [관찰]
- `CameraManagerExtended.SetTargetFpsLevel` → 엔진 내부 호출로 `Application.targetFrameRate` 설정 (ev_02d73111d1d7b816). `set_TargetFpsLevel`과 같은 기계어 [관찰: ICF]
- VSync는 품질 단계가 정함. 게임 코드에서 `QualitySettings.set_vSyncCount`가 빌드에서 제거됨 → 게임은 VSync를 직접 바꾸지 않음 [관찰]
- `SetResolutionLevel` → `Screen.SetResolution` (ev_cbe7b0c5b592f7eb). 목록은 `IsWithinResolutionRange`로 모니터 해상도 이하만 남김 [관찰: 호출 / 추론: 의미]
- Naninovel 설정: 기준 해상도 2560x1440, 기준 PPU 100, 저장 썸네일 480x270 [관찰]

## 9. IL2CPP 모드 제약

### 9.1 빌드에서 제거된 API (대체 수단)

| 제거됨 | 대체 |
|---|---|
| `QualitySettings.set_vSyncCount`/`get_vSyncCount` | `IL2CPP.ResolveICall("UnityEngine.QualitySettings::set_vSyncCount")` |
| `RenderTexture.IsCreated`, `set_antiAliasing` | `ResolveICall`. `set_antiAliasing`은 BepInEx interop이 icall로 복원해 그대로 호출됨 [관찰: 0.2.0 실행] |
| `ScreenCapture.CaptureScreenshot`, `ImageConversion.EncodeToPNG` | `CaptureScreenshotAsTexture` + `EncodeToJPG` |
| `Object.GetInstanceID` | interop 객체의 `Pointer` |

엔진 내부 호출(icall)은 UnityPlayer.dll에 남아 있어 관리 코드가 제거돼도 쓸 수 있음.

### 9.2 ICF (코드 합침) 주의

| 메서드 | 같은 기계어를 쓰는 다른 메서드 | 대응 |
|---|---|---|
| `LayeredDrawer.Dispose` | Linq 식 getter 등 4개 | `ClearLayers`를 패치 (Dispose·재구성 모두 거침) |
| `StageCameraContainer.SetCameraEnabled` | `CreditRoll.SetCanvasEnabled` | `StageCameraController.SetCameraEnabled`를 패치 |
| `LayeredActorBehaviour.get_Animated` | getter 45개 | 패치 금지 |
| `LayeredCharacter..ctor` | `LayeredCharacterExtended..ctor` | 생성자 대신 `TransitionalRenderer.CreateFor` 패치 |

Harmony는 기계어 주소를 가로채므로, 공유 주소를 패치하면 다른 메서드까지 바뀜.

### 9.3 빌드 환경

- 컴파일용 interop 어셈블리: BepInEx가 첫 실행 때 `BepInEx/interop`에 만듦. 설치 전에는 Cpp2IL 2022.1.0-dev.1452 + Il2CppInterop.Generator 1.5.3(be.785와 같은 버전)으로 직접 생성해 `-p:InteropDir=`로 지정
- 대상 프레임워크 net6.0 (BepInEx 6 IL2CPP 런타임)

## 10. 실행 중 측정

### 1차 (LXIUM 0.1.0, BepInEx 6.0.0-be.788, 개발 PC Iris Xe)

- 사용자 체감: 재판 장면이 원래 10fps 미만 → 0.1.0 기본 설정에서 **60fps 고정 유지** [관찰: 사용자 보고]
  - 기본 `TargetFps=0`(모니터 60Hz + VSync)이라 60이 상한. 실제 여유는 `-1`로 재야 함 [미확인]
  - 기능별 기여도는 모름. 렌더 실험실은 설정 `RenderLab=false`라 F10이 동작하지 않았고 `lab.txt` 없음
- `BepInEx/LogOutput.log`로 확인한 정적 분석 값 [관찰: 실행 로그]
  - URP 에셋 `URP-HighFidelity`, MSAA 4, 중간 텍스처 Always, SSAO 기능 있음 (4장과 같음)
  - 무대 카메라 2대, RT 1920x1080 (6.1과 같음)
  - 레이어 캐릭터 PPU 100 → 53.33: 액터 생성 시점 화면 높이가 768 (1366x768로 실행 중) → 합성 RT 800x1600
- `TransitionalRenderer.CreateFor` 패치가 레이어 캐릭터에서 39번 불림 (한 세션) → 액터가 장면마다 다시 만들어짐 [추론]. 배율은 생성 때마다 그 시점 화면 높이로 다시 계산되므로, 해상도를 바꾸면 이후 생성되는 캐릭터부터 반영됨 [추론: 코드]
- 오류·예외 로그 없음 [관찰]

### 2차 (LXIUM 0.1.0 렌더 실험실, 재판 장면)

조건: 1366x768 창 모드, D3D11, Iris Xe, 품질 단계 0(Very Low), `TargetFps=-1`(제한·VSync 없음). 화면에는 캐릭터 2명(에마·메루루)이 보이는 법정 대화 [관찰: lab.txt·스크린샷]

| 프리셋 | fps | 평균 ms | p95 | p99 | 카메라/프레임 | layerDraw | copyDraw | stageSkip |
|---|---|---|---|---|---|---|---|---|
| 0_original | 22.5 | 44.42 | 49.92 | 52.55 | 14.00 | 11.00 | 12.00 | 0 |
| 1_msaa | 22.8 | 43.88 | 53.83 | 56.88 | 14.00 | 11.00 | 12.00 | 0 |
| 2_ssao | 30.2 | 33.06 | 35.72 | 42.58 | 14.00 | 11.00 | 12.00 | 0 |
| 3_intermediate | 30.4 | 32.91 | 34.50 | 36.91 | 14.00 | 11.00 | 12.00 | 0 |
| 4_changeOnly | 41.7 | 23.99 | 26.46 | 31.45 | 3.26 | 0.26 | 0.29 | 0 |
| 5_hiddenSkip | **72.3** | **13.83** | 15.88 | 22.85 | 2.15 | 0.15 | 0.17 | 1.00 |

- 전체: 44.4 → 13.8 ms (3.2배). 단, 캐릭터 합성 해상도 맞춤(PPU 53.3)은 실험실에서 끌 수 없어 0_original에도 적용된 상태 → 게임 원래 상태는 더 느림 [관찰: 설정 / 추론]
- 단계별 절감: SSAO 무대 전용 **-10.8 ms**, 변경분만 합성 **-8.9 ms**, 숨은 무대 카메라 끄기 **-10.2 ms**, MSAA -0.5 ms(오차 수준, 768p라 메인 카메라가 작음), 중간 텍스처 -0.2 ms
- 5.4 [미확인] 해소: 원래 상태에서 `layerDraw` = 프레임당 11 → **레이어 캐릭터는 매 프레임 다시 합성됨** [관찰]
  - 화면에 보이는 캐릭터는 2명인데 11명이 합성됨 → 법정 단상에 선 다른 참가자(카메라 밖)도 `perceivedVisibility`가 참이라 매 프레임 합성 [관찰: 수치 / 추론: 원인]
  - 카메라/프레임 14 = 캐릭터 11 + 메인 1 + 무대 2 [추론]. SSAO가 이 14대 모두에서 돌아 -10.8 ms가 나옴
- 6.2 [미확인] 해소: `stageSkip` = 프레임당 1 → **한 화면만 보일 때 두 번째 무대 카메라도 1920x1080을 그리고 있었음** [관찰]
- 4_changeOnly 이후의 `layerDraw` 0.26 ≈ 11명 / 41.7fps → 1초 안전 갱신만 일어남. 측정 중 실제 변경은 없었음 [추론]
- 5_hiddenSkip의 p99 22.85 ms: 11명의 안전 갱신이 같은 프레임에 몰린 것으로 봄 [추론] → 0.1.1에서 프레임당 1개로 분산
- 스크린샷 6장을 0_original과 화소 비교: 평균 차이 0.05~0.11/255, 차이 15 초과 화소 0.1% 미만 (불·스테인드글라스 움직임과 JPEG 잡음 수준) → **A·B 기법의 화면 변화 없음 확인** [관찰]
- 남은 비용: 메인 카메라(1366x768) + 무대 카메라 1대가 1920x1080(창보다 큼) + Lit·불·SSAO·MSAA 2x·FXAA [추론] → 0.1.1에서 무대 RT를 화면 높이에 맞춤

### 3차 (LXIUM 0.1.1 렌더 실험실, 같은 재판 장면)

조건은 2차와 같음 (1366x768 창 모드, D3D11, Iris Xe, 품질 0, 제한·VSync 없음) [관찰: lab.txt]

| 프리셋 | fps | 평균 ms | p95 | p99 | max | 카메라/프레임 | stageSkip |
|---|---|---|---|---|---|---|---|
| 0_original | 23.4 | 42.74 | 46.40 | 49.42 | 49.48 | 14.00 | 0 |
| 1_msaa | 23.5 | 42.56 | 48.65 | 50.13 | 60.70 | 14.00 | 0 |
| 2_ssao | 31.3 | 31.94 | 33.88 | 36.65 | 36.81 | 14.00 | 0 |
| 3_intermediate | 31.2 | 32.08 | 34.71 | 37.69 | 38.38 | 14.00 | 0 |
| 4_changeOnly | 43.6 | 22.96 | 25.42 | 27.44 | 29.38 | 3.25 | 0 |
| 5_hiddenSkip | 72.3 | 13.83 | 16.38 | 17.74 | 19.58 | 2.15 | 1.00 |
| 6_stageRes | **115.1** | **8.69** | 11.06 | 13.01 | 15.59 | 2.10 | 1.00 |

- 0~5단계는 2차와 거의 같음 (재현성 확인) [관찰]
- 무대 RT 화면 맞춤(1920x1080 → 1365x768): **-5.1 ms**, 72.3 → 115.1 fps. 전체 23.4 → 115.1 fps (4.9배) [관찰]
- 안전 갱신 분산(0.1.1): 5_hiddenSkip의 p99 22.85 → 17.74 ms, max 25.80 → 19.58 ms (2차 대비) [관찰]
- 화면 비교 (0_original 대비, JPEG): 5_hiddenSkip 평균 차이 0.05/255, 6_stageRes 평균 2.54/255·차이 15 초과 화소 2.6% [관찰]
  - 6_stageRes의 차이는 벽지 무늬·가장자리 같은 세밀한 부분. 원래는 1920x1080을 창 크기로 줄여 보여 줘서 약 1.4배 슈퍼샘플링이 됐던 것이 빠진 것 [추론]. 확대해 봐야 보이는 수준
- 남은 8.7 ms: 메인 카메라(768p, FXAA·후처리) + 무대 카메라 1대(768p, Lit·불·SSAO·MSAA 2x·FXAA) [추론]

### 0.2.0 실행 확인 (타이틀 화면)

- `FireLite = true`로 실행: 내장 번들 로드, `LXIUM/FireLite supported=True` (D3D11), 오류 없음. Player.log의 셰이더 오류는 URP `DBufferClear` 하나(원래 있던 것) [관찰]
- 무대 카메라 추가 데이터: 후처리 켬, RT MSAA 2 [관찰: 로그]

### 4차 (LXIUM 0.2.0 렌더 실험실, 같은 재판 장면)

조건: 1366x768 창 모드, D3D11, Iris Xe, 품질 0, 제한·VSync 없음, `frameTiming=True`이지만 `gpuFrameTime`이 0이라 CPU·GPU 값은 기록 안 됨 [관찰: lab.txt]. 이 세션은 캐릭터가 화면 높이 1080일 때 생성돼 PPU 75(합성 1125x2250)였음 → 3차(PPU 53.3)보다 `00_original`이 무거움 [관찰: 로그]

각 실험은 기준(`01_current` = 0.1.1 기본값) 위에 하나만 더함. `01`→`13` 사이 기준이 8.99 → 9.40 ms로 느려져(발열 추정) 아래 Δ는 두 기준을 시간에 따라 직선 보간해 뺀 값

| 프리셋 | fps | 평균 ms | p99 | Δ ms | 화면 차이 (법정 가운데, 01 대비) |
|---|---|---|---|---|---|
| 00_original | 19.3 | 51.89 | 69.90 | — | — |
| 01_current | 111.2 | 8.99 | 12.40 | 기준 | — |
| 02_ssaoLite | 128.5 | 7.78 | 11.39 | **-1.24** | 0 (max 0) |
| 03_ssaoOff (깊이 유지) | 120.1 | 8.33 | 12.89 | -0.73 | 0 (max 0) |
| 04_stageMsaaOff | 125.7 | 7.96 | 11.16 | **-1.13** | 0 (max 3) |
| 05_depthPriming (+MSAA 끔) | 124.8 | 8.02 | 11.12 | -1.11 (04와 같음) | 0 |
| 06_stagePostOff | 117.6 | 8.50 | 11.17 | **-0.66** | 평균 1.73/255 (FXAA 한 번 빠짐) |
| 07_mainPostOff | 117.5 | 8.51 | 11.67 | -0.69 | 평균 1.74/255 |
| 08_stageHalfRate | 138.5 | 7.22 | 11.89 | -2.01 | 0 (정지 장면). p50 8.09 > 평균 → 프레임 시간이 번갈아 짧고 김 |
| 09_stageRes75 | 141.3 | 7.08 | 11.01 | -2.18 | 평균 3.05/255, 법정·캐릭터가 부드러워짐 |
| 10_fireOn (원본 불, 12개 모두 보임) | 97.8 | 10.23 | 15.49 | +0.93 | — |
| 11_fireLite | 104.8 | 9.54 | 12.85 | **+0.21** | 원본과 같은 색·모양 (불은 움직여서 화소 비교 불가) |
| 12_combo (02+04+05+06+C3) | **154.2** | **6.49** | 9.67 | **-2.88** | 평균 1.87/255 |
| 13_current | 106.4 | 9.40 | 12.94 | 기준 | 평균 1.11/255 (UI 장식·캐릭터 움직임) |

- **SSAO는 이 법정에서 화면에 아무 영향이 없음**: 가볍게·끄기 모두 법정 영역 화소가 기준과 완전히 같음 [관찰]. 법정 조명이 스포트라이트뿐이라 AO가 깎을 간접광이 없는 것으로 봄 [추론]
- `03_ssaoOff`가 `02_ssaoLite`보다 느림: 원본 불 셰이더용으로 요청한 깊이 텍스처(MSAA 2x RT)가 SSAO 프리패스 못지않은 비용 [추론] → 0.2.1에서 FireLite를 쓰면 깊이를 요청하지 않음
- 무대 MSAA 2x도 화면 영향 없음 (FXAA가 두 번 걸리던 상태라 차이가 묻힘) [관찰]
- 깊이 프라이밍은 MSAA 끔 위에서 추가 이득 0 → 0.2.1에서 제거. 이 법정은 겹쳐 그려지는 불투명 면이 적음 [추론]. URP가 실제로 프라이밍을 썼는지는 확인 못 함
- 불: 원본 0.93 ms → FireLite 0.21 ms (**-77%**), p99 15.49 → 12.85 ms. `fire=12 fireVisible=12`: 이 화면에서 불 12개가 모두 보이고 화면 대부분을 덮음 [관찰: 스크린샷]
- 조합은 개별 절감의 합(-3.03)과 거의 같음(-2.88) → 서로 독립 [관찰]
- 결론 (0.2.1 기본값): `Ssao = StageLite`, `Stage.MsaaOff = true`, `Stage.PostOff = true`, `Stage.FireLite = true`. 반 프레임·해상도 75%는 화면이 달라지거나 프레임 간격이 고르지 않아 선택으로 둠
- 전체: 원래 19.3 fps → 0.1.1 기본 111.2 → 0.2.1 기본(조합) 154.2 fps (8.0배) [관찰: 768p 창 모드]

### 5차 (LXIUM 0.2.1 렌더 실험실, 같은 재판 장면, **1080p 전체 화면**)

조건: 1920x1080 FullScreenWindow, D3D11, Iris Xe, 품질 0, 제한·VSync 없음. 기준 `01` 8.05 ms → `10` 8.01 ms로 발열 영향 없음 → Δ는 두 기준 평균(8.03 ms) 대비 [관찰: lab.txt]. 무대 RT 1920x1080 (화면 맞춤 = 원래 크기)

| 프리셋 | fps | 평균 ms | p99 | Δ ms | 화면 차이 (법정 가운데) |
|---|---|---|---|---|---|
| 00_original | 16.7 | 60.03 | 63.52 | — | 평균 1.32/255 (MSAA 2x·FXAA 두 번) |
| 01_current (0.2.1 기본) | 124.2 | 8.05 | 9.46 | 기준 | — |
| 02_noStage (무대 카메라 정지) | 264.7 | 3.78 | 5.59 | **-4.25** = 무대 카메라 몫 | 0 (정지 그림) |
| 03_ssaoOff | 160.5 | 6.23 | 8.11 | **-1.80** | **0 (max 0)** |
| 04_ssaoFull (StageOnly) | 88.1 | 11.35 | 12.96 | +3.32 | 0 (max 0) |
| 05_stageHalfRate | 158.1 | 6.33 | 9.50 | -1.70 | 0. p50 7.59 > 평균 (번갈아 짧고 김) |
| 06_stageRes75 | 158.6 | 6.30 | 8.25 | -1.73 | 평균 2.76/255 |
| 07_mainPostOff | 143.8 | 6.96 | 8.84 | -1.07 | 평균 2.13/255. 무대 MSAA·FXAA가 이미 꺼져 있어 법정에 AA가 하나도 없게 됨 |
| 08_fireOn (FireLite) | 117.7 | 8.50 | 12.32 | +0.47 | 같은 색·모양. 원본에 보이던 계단 모양 경계(깊이 페이드)가 없음 |
| 09_fireOriginal | 112.0 | 8.93 | 10.69 | +0.90 | — |
| 10_current | 124.8 | 8.01 | 9.67 | 기준 | 0 |

- 1080p 전체 화면: 원래 16.7 fps → 0.2.1 기본 124.2 fps (7.4배) → SSAO 끔 160.5 fps (9.6배) [관찰]
- **SSAO는 원래 품질이면 1080p에서 5.1 ms** (가볍게 1.8 ms). 4차에 이어 1080p에서도 끈 것·원래 품질·가볍게의 법정 화소가 완전히 같음 → 0.2.2 기본 `Off` [관찰]
- 0.2.1 기준 프레임 8.0 ms = 무대 카메라 4.25 (그중 SSAO 가볍게 1.8) + 나머지 3.8 (메인 카메라 UI·대사·무대 화면 붙이기·후처리 1.07 + CPU) [관찰: Δ / 추론: 나눔]
- FireLite: 불 비용 0.90 → 0.47 ms (-48%). 4차(768p)는 -77%. 평균은 두 번 모두 줄었으나 p99는 이번에 FireLite 쪽이 높음 (12.32 vs 10.69, 표본 약 7프레임, `max 21.26` 한 번 포함) [관찰] → 잡음인지 다음 측정에서 확인
- 메인 카메라 후처리를 끄면(게임 옵션과 같은 효과) 법정에 AA가 전혀 없어짐 → 0.2.2에서 메인 후처리가 꺼져 있으면 무대 후처리(FXAA)를 살림
- `FrameTimingManager`: 켜짐으로 나오지만 4·5차 모두 값이 오지 않음 → 0.2.2 실험실에서 뺌 [관찰]

## 11. 분석 방법

| 도구 | 용도 |
|---|---|
| REA 6.4.0 + Ghidra 12.1.3 | `GameAssembly.dll` 디컴파일. Cpp2IL 덤프에서 만든 함수 시드 94,842개(주소·이름)를 분석 전에 넣어 이름 있는 의사코드를 얻음. 전체 분석 약 27분. MCP stdio 세션 하나로 39개 함수 `analyze_function` |
| REA `inspect-managed-members` | `GigaCreation.NaninovelExtender.ExtendedActors` 더미 어셈블리 멤버 목록 (ev_161a58b13b809c6d) |
| Cpp2IL 2022.1.0-dev.1452 | 메타데이터 → 더미 DLL·시그니처(RVA)·ISIL(명령 수준) |
| UnityPy 1.25.4 + TypeTreeGeneratorAPI | 번들·씬·설정 에셋 읽기. IL2CPP라 MonoBehaviour 타입트리는 더미 DLL에서 생성 |
| d3dcompiler_47.dll (`D3DDisassemble`) | 셰이더 블롭(LZ4)에서 DXBC를 꺼내 디스어셈블. 텍스처·상수 이름은 `m_ParsedForm`의 `m_CommonParameters`·`m_NameIndices`에서 매핑 |

- REA 기본 Ghidra 시간 제한(330초)으로는 54 MB 바이너리 분석이 끝나지 않음 → `REA_GHIDRA_STARTUP_TIMEOUT_MS`를 늘리고 `REA_GHIDRA_SEED_FILE`로 시드 지정
- 한계: 정적 분석만 함. 가상·인터페이스 호출의 호출자는 정적으로 못 찾음 (5.4 `animated` 소비자). GPU 비용 수치는 모두 [추론]

### 근거 ID (REA analyze_function)

| 주소 | 메서드 | 근거 |
|---|---|---|
| 0x18084ED10 | LayeredActor.RenderAppearance | ev_396046bcba1f543a |
| 0x180464030 | LayeredActorBehaviour.Render | ev_a3e3f623f6f7abd1 |
| 0x180464210 | LayeredActorBehaviour.CreateDrawer | ev_dfd1ed6fa6d07fba |
| 0x180468BF0 | LayeredDrawer.DrawLayers | ev_c553aa36a57971a8 |
| 0x1804697F0 | LayeredDrawer.DrawWithCamera | ev_9fe04d4d38a61ec2 |
| 0x18046A9A0 | LayeredDrawer.PrepareRenderTexture | ev_5ba4089c7240b185 |
| 0x18046BCE0 | LayeredDrawer.HoldCameraLayer | ev_af83d8de164c2b70 |
| 0x1805F7970 | TransitionalSpriteRenderer.Update | ev_650744b26b6f65ea |
| 0x1805F85B0 | TransitionalSpriteRenderer.PrepareRenderTexture | ev_b73807cb15d72912 |
| 0x1805F28B0 | TransitionalRenderer.ShouldRender | ev_6cf33aa86fef8d65 |
| 0x1805F2A70 | TransitionalRenderer.RenderToTexture | ev_7aca6fcccbb640e6 |
| 0x1805F3600 | TransitionalRenderer.LateUpdate | ev_30de49797ec8a9d7 |
| 0x180C5B320 | DicedSpriteActor.RenderToTexture | ev_1b591d73f29e3a9d |
| 0x180D25820 | LayerModifier.ModifyLayers | ev_7d6dcc76fdef656c |
| 0x180389D20 | StageCameraController.SetCameraEnabled | ev_1c598cc193990d13 |
| 0x180385AD0 | Court.ChangeFireFxActivity | ev_336c2b33c95f2a46 |
| 0x180CFB8E0 | CameraManagerExtended.SetTargetFpsLevel | ev_02d73111d1d7b816 |
| 0x180CFCCF0 | CameraManagerExtended.SetResolutionLevel | ev_cbe7b0c5b592f7eb |

## 12. 미확인

- 메인 카메라 몫 3.8 ms(1080p) 가운데 UI(TrueShadow·TMP·대사창) 몫 (0.2.2 실험실 `03_noUI`로 측정 예정)
- TrueShadow `LateUpdate`의 CPU 비용
- 재현 연출(`SpawnableReenactment`, SpriteSkin 343개)의 변형 비용
- 실제 불 연출(조명 색이 불빛으로 바뀌는 장면)에서 FireLite 화질

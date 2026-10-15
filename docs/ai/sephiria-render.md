# 세피리아 렌더링 최적화 설계 (AI용)

- 사람용 문서: `../human/sephiria-render.md`
- 기반 사실: `sephiria.md`. 이 문서는 그 위에서 LXIUM 고유 렌더를 설계함
- 목표: 내장 GPU(Intel UHD 620 ~ Iris Xe)에서 전투 장면 120 fps 이상. HUD·GUI·글자는 원래 해상도 유지
- 표기: [관찰] [추론] [미확인]. 효과 수치는 측정 전까지 모두 [미확인]

## 1. 출발점

세피리아는 직교 카메라 하나가 픽셀 아트 월드를 URP 2D 조명·HDR·블룸으로 그리고, 그 위에 uGUI가 얹히는 구조다 (`sephiria.md` 3장). 비용 후보:

| 비용 후보 | 위치 | 근거 | 확인 방법 |
|---|---|---|---|
| 2D 조명 텍스처를 화면 해상도 그대로, 레이어 배치마다 | `Renderer2DData` 조명 배율 1, 최대 16장 | 3.1 [관찰] | probe 조명·레이어, lab `light0.5` |
| HDR 색 버퍼 + 블룸·우버 후처리 전체 해상도 | URP 에셋 HDR, 볼륨 | 3.1 [관찰], 블룸 설정 [미확인] | probe 볼륨 |
| 화면 전체를 덮는 반투명 층: 안개, 분위기, 비·눈, 패럴랙스 배경, 배경 카메라 | `GameCamera`, `DayCycle`, `ParallaxScrollLayer` | 3.2 [관찰] | probe 렌더러·카메라 |
| 오브젝트마다 `Update` (애니메이터·이펙트·조명 애니메이터·HUD) | 646개 타입 | 3.3·3.5 [관찰] | 단계별 시간 `Update`, probe 스크립트 수 |
| 매 프레임 GC 할당 | `ParallaxScrollLayer.Refresh`, HUD `String.Format` | 3.2·3.4 [관찰] | `gcKB/frame` |

이 PC(Iris Xe, 1437x1000)는 이미 약 180 fps라 [관찰: `sephiria.md` 4장] 어디가 병목인지 먼저 잰다. 0.1.0이 그 계측이다.

## 2. 기법

### A. 엔진 기능 (설정만, 화면 구조 변화 없음)

| ID | 이름 | 방법 | 화면 변화 | 상태 |
|---|---|---|---|---|
| A1 | 조명 텍스처 배율 | `Renderer2DData.m_LightRenderTextureScale` (반사로 씀. 매 프레임 `CreateResources`가 읽어 바로 적용 [관찰]) | 화면 평균 밝기 같음 [관찰] | 구현 0.1.0 `LightScale`. 0.5: +4~8% (3차 123 vs 114~119 fps) |
| A2 | 월드 렌더 배율 | URP `renderScale` + `upscalingFilter`. 화면 공간 오버레이 HUD는 원래 해상도 [관찰: 스크린샷] | URP `Point`는 실제로는 흐림(쌍선형처럼 보임), `FSR`은 픽셀 모서리가 둥글어짐 [관찰: 3배 확대] | 구현 0.1.0. Point 0.5: 151 fps, FSR 0.5: 128 fps |
| A3 | 볼륨 갱신 줄이기 | `volumeFrameworkUpdateMode = ViaScripting` + 프로필·가중치가 바뀔 때만 `VolumeManager.Update` | 없음 (바뀜 감지가 맞으면) | 후보. 볼륨 수·CPU 비중 확인 후 |
| A4 | 블룸 가볍게 | 볼륨 Bloom의 `downscale`(Half → Quarter), `maxIterations` 줄이기 | 번짐 범위가 약간 달라짐 | 후보. probe로 블룸 값 확인 후 |

### B. LXIUM 자체 렌더 경로

힌트(Spatial Resolution, Temporal Reprojection, Opaque Ice, Render Fast, Async)를 2D 직교 픽셀 아트에 맞게 다시 만든다.

| ID | 이름 | 방법 | 힌트 | 상태 |
|---|---|---|---|---|
| B1 | 텍셀 격자 렌더 (TGR) | 월드를 "아트 텍셀 격자 × k" 해상도로 그림. 직교 높이 × PPU = 화면 세로 텍셀 수 (예: 5.63 × 2 × 16 = 180) → k = 2~3이면 360~540줄. 조명·블룸·안개는 원래 저주파라 이 해상도로 충분. 화면으로는 C1(선명 쌍선형)로 올림. 오버레이 UI는 원래 해상도 | Spatial Resolution. 픽셀 아트는 텍셀보다 촘촘히 그려도 정보가 늘지 않으므로 손실이 거의 없는 판 [추론] | 1단계 구현 0.4.0 `TexelMultiple`: `renderScale = round(k × 2 × 직교 크기 × ArtPPU / camera.pixelHeight, 0.01)`, 0.1~1. `Camera.main` 직교 크기·높이가 바뀌면 매 프레임 따라감(0.005 넘게 바뀔 때만 설정). 4차: ×2 → 0.36 149.5 fps, ×3 → 0.54 138.3 fps. 다음: 카메라를 렌더 픽셀 격자에 맞추기(떨림) |
| B2 | 직교 이동 재투영 (OSR) | 실제(REAL) 프레임은 여백을 조금 더 그려 RT에 남기고, 생성(GEN) 프레임은 월드 카메라를 끄고 지난 RT를 카메라 이동량만큼 밀어 그림. 직교 카메라의 평행 이동은 정지한 물체에 대해 정확함. UI는 매 프레임 실제로 그림. 움직이는 적·탄·조명 애니메이션은 반 프레임률. GPU가 병목일 때만 켜는 적응형 | Temporal Frame Reprojection | 후보. 위험: 패럴랙스 층(비율이 다름)·화면 흔들림·회전 효과. 밀기는 `Hidden/Universal/CoreBlit`의 scaleBias로 가능해 셰이더가 없어도 시작 가능 [추론] |
| B3 | 정적 층 캐시 (지연 2D) | 방이 바뀔 때 바닥 타일맵·배경의 알베도를 텍셀 해상도 RT에 한 번 그리고, 매 프레임은 "알베도 × 조명 텍스처" 전체 화면 1패스로 대체. 타일 수천 개의 그리기·겹침이 1장으로 | Opaque Ice (반투명 층 더미 → 불투명 바닥 1장) | 후보. 타일맵 수·그리기 비중 probe 필요. 카메라 이동은 B2와 같은 밀기 |
| B4 | 화면 층 합치기 | 안개(`FogControl`), 분위기 스프라이트, 비·눈 이미지, 배경 안개를 한 패스로 (저해상도) | Render Fast | 후보. C3 필요 |
| B5 | 조명 반 프레임 | 조명 텍스처를 두 프레임에 한 번 그리고 사이에는 카메라 이동만큼 밀어 씀 (조명은 저주파) | Temporal Reprojection | 후보. URP 2D 내부 패스 교체가 필요해 위험 큼 |
| B6 | 보이는 것만 갱신 | 화면 밖 `Animator2D_Basic`·`SpriteFx`·조명 애니메이터의 스프라이트·조명 변경을 건너뛰고 화면에 들어올 때 따라잡음. 또는 관리자 하나가 묶어서 돌림(호출 전환 비용 감소) | Async (Unity API는 메인 스레드 전용이라 병렬 대신 일 줄이기) | 후보. `Update` 단계 비중이 클 때 |
| B7 | 할당 없는 경로 | `ParallaxScrollLayer.Refresh`의 `HashSet` 재사용, HUD `String.Format`을 값이 바뀔 때만 | — | 후보. `gcKB/frame` 확인 후 (Harmony 필요) |
| B8 | 해상도 조절기 | 프레임 시간 목표(예: 8.3 ms)를 넘으면 B1의 k(또는 A2 배율)를 내리고, 여유가 있으면 올림 | Spatial Resolution | 후보. B1 뒤 |

### C. 자체 셰이더

셰이더는 실행 중 컴파일이 안 된다. 이 PC의 Unity 에디터(6000.5.7f1)는 게임(6000.3.21f1)과 버전이 달라 그 번들은 게임에서 못 읽을 가능성이 높다 [추론]. 그래서 에디터 없이 만든다.

**빌드 도구** (`optimizeMods/LXIUM_sephiria/shaders/build_shaders.py`, UnityPy 1.25.4 + `d3dcompiler_47.dll`, 0.2.0)

1. 게임 셰이더 86개는 번들이 아니라 `globalgamemanagers.assets`·`sharedassets0~2.assets`·`resources.assets` 안에 있고, 플랫폼은 **D3D11(4) 하나뿐** [관찰: UnityPy]. 플레이어 빌드라 타입 트리가 없어서 UnityPy TPK의 6000.3.21f1 타입 트리로 읽음 (86개 모두 읽힘 = 트리가 맞음)
2. 원본 셰이더 객체를 틀로: 첫 패스만 남기고, 규칙이 있는 단계는 일치하는 변형의 DXBC만 LXIUM HLSL 컴파일 결과(`d3dcompiler_47`, O3)로 바꿈. 서브프로그램 구조(버전 202012090, 종류, 통계, 키워드, 코드 = 길이 + Unity 헤더 38바이트 + DXBC)와 블롭 표 구조는 마법소녀의 마녀재판 도구와 같음 [관찰]
3. 세피리아에는 에셋 번들이 없어 그릇으로 쓸 게임 번들이 없음 → 이 저장소 `LXIUM_mahosaba/res/lxium_mahosaba.bundle`(6000.0.48f1, 셰이더·AssetBundle 객체만 있음)을 그릇으로 빌려 엔진 버전을 6000.3.21f1로, 타입 트리를 TPK 6000.3.21f1 것으로 바꿈. TPK 노드에 없는 값은 에디터 번들과 같은 규칙(배열 `m_TypeFlags` 1, 그 밖 0, `m_RefTypeHash` 0)으로 채움
4. 타입 해시: Shader는 게임 파일의 해시(`57f0dd2d…`)를 넣어 엔진이 같은 구조로 보게 하고, AssetBundle은 0으로 비워 타입 트리로 읽게 함 [추론: 해시가 다르면 엔진이 타입 트리 변환 경로를 씀]
5. 결과 `res/lxium_sephiria.bundle`(17.7 KB)을 DLL에 넣음. 실행 중 `AssetBundle.LoadFromMemory` → `LoadAllAssets<Shader>` → `DontUnloadUnusedAsset`. 내부 파일 `CAB-<md5("LXIUM_sephiria")>`
6. 다시 읽기 확인: UnityPy로 번들을 다시 열어 셰이더 이름·변형 수·컨테이너 확인, 픽셀 코드 디스어셈블(`ps_5_0`, `CB0[139]`, `s0`, `t0`, 입력 `v1.xy`) [관찰]. 게임 로드: `shader 'LXIUM/TexelUpscale' supported=True passes=1` [관찰: Player.log, 0.3.0]

**C1이 들어가는 자리**: URP FSR 경로 = 입력 준비(`Scaling Setup`, FSR이면 늘 `_GAMMA_20`) → `PostProcessMaterialLibrary.easu` 재질로 `Blitter.BlitTexture` → `FinalPost`(선명도 > 0일 때만 `_RCAS`) [관찰: `PostProcessPassRenderGraph.RenderFinalSetup`·`RenderFinalFSRScale`·`RenderFinalBlit`]. EASU 셰이더(`Hidden/Universal Render Pipeline/Edge Adaptive Spatial Upsampling`)는 변형 1개, 바인딩 `_BlitTexture` t0 + 인라인 선형 클램프 샘플러 s0 + `$Globals` b0(`_ScreenParams` 368, `_FsrEasuConstants0~3` 2192~2240), 끝에서 sRGB→선형 변환 + 알파 1 [관찰: 디스어셈블]. 같은 바인딩으로 픽셀 코드만 바꾸면 그 재질의 `shader`만 바꿔 URP가 우리 확대를 씀

**RCAS와 같이 못 쓰는 이유** (0.3.1~0.3.2에서 측정): 우리 셰이더가 색을 그대로 넘기면 RCAS 경로에서 화면이 2.6배 밝아지고(중앙 평균 112 vs 42), 원본처럼 sRGB→선형을 하면 RCAS 없는 경로에서 6배 어두워짐(6 vs 42) [관찰: 스크린샷 평균]. 이 단계 뒤가 기대하는 부호화가 `_RCAS` 유무로 다르고, 셰이더는 그걸 알 수 없음. 그래서 `Texel`은 선명도를 0으로 고정(RCAS 없는 경로)하고 색을 그대로 넘김 → 밝기 같음 [관찰]. 선명화가 필요하면 C1 안에 직접 넣음

| ID | 셰이더 | 내용 | 쓰는 곳 | 상태 |
|---|---|---|---|---|
| C1 | `LXIUM/TexelUpscale` | 선명 쌍선형: 입력 픽셀 좌표 = `uv / con1.xy`, 가장 가까운 텍셀 경계 주변 출력 1픽셀 폭만 보간(`clamp((x - 경계) / con0.x, ±0.5)`), 나머지는 텍셀 색 그대로. 텍스처 읽기 1회 (EASU는 12회) | A2를 `Upscale = Texel`로 (FSR 필터 + EASU 재질 셰이더 교체, 선명도 0), 이후 B1·B2 | 구현 0.2.0, 0.3.2에서 확인: 0.5 배율 145.7 fps(+25%, 1% low 104 = 가장 고름), 0.36 배율 151.7 fps(+30%). 3배 확대에서 원본과 거의 같은 또렷함 [관찰] |
| C2 | `LXIUM/SpriteLitLite` | `Sprite-Lit-Default`(3패스)에서 실제로 쓰는 블렌드 스타일·마스크 채널만 남긴 판 | 대부분의 월드 스프라이트 (probe 셰이더 수 확인 후) | 후보 |
| C3 | `LXIUM/OverlayFused` | 안개 노이즈 + 분위기 색 + 비·눈 스크롤을 한 패스로 | B4 | 후보 |
| C4 | `LXIUM/Deferred2D` | 정적 층 알베도 × 조명 텍스처 (블렌드 스타일 합성 규칙은 URP 2D와 같게) | B3 | 후보 |
| C5 | `LXIUM/LightLite` | 점 조명을 텍셀 해상도에서 한 번에 누적 (쿠키 포함). 원본 `Hidden/Light2D`는 변형이 많은 큰 셰이더(블롭 106 KB) [관찰] | B5 또는 B1의 조명 단계 | 후보 |

- 셰이더를 더 넣으려면 `SHADERS`에 항목을 더함. 그릇 번들의 셰이더 자리가 2개라 3개부터는 객체 복제를 추가해야 함 (코드에 표시)

### 채택하지 않는 것

| 힌트 | 이유 |
|---|---|
| Async (엔티티 병렬 처리) 그대로 | 게임 로직이 Unity API·Mirror 네트워크를 메인 스레드에서 씀. 렌더 스레드는 이미 분리(`gfx-threading-mode=6`) [관찰]. B6으로 대신 |
| MSAA 끄기 | 이미 꺼져 있음 [관찰] |

## 3. 검증 절차 (0.1.0부터 계측)

0.5.0부터 `Frame`이 모든 플레이어 루프 하위 시스템 앞에 표식을 넣어 하위 시스템별 시간도 모은다(`Frame.DetailReport`). 표식은 약 120개, 프레임당 비용은 수십 µs [추론]. F9 덤프는 같은 이름의 스크린샷도 남긴다

게임 폴더 `AddOns/LXIUM_sephiria/` 아래에 남음.

| 기능 | 키·설정 | 결과 |
|---|---|---|
| 단계별 프레임 통계 | 10초마다 (`StatsIntervalSec`) | `Player.log`에 `[LXIUM] frame scene=… fps … phase-ms TimeUpdate=… Update=… PreLateUpdate=… PostLateUpdate=… draws setpass batches gcKB/frame` |
| 렌더 상태 덤프 | F9, 장면이 바뀌면 3초 뒤 자동 (`AutoProbe`) | `logs/probe-<장면>-<시각>.txt`: 파이프라인 필드 전체, 블렌드 스타일, 정렬 레이어, 카메라, 2D 조명 목록, 볼륨 프로필 값, 보이는 렌더러(종류·셰이더·PPU·정렬 레이어), 타일맵, 파티클, 캔버스, 스크립트 수(Update/LateUpdate 여부) |
| 설정 비교 (lab) | F10 (다시 누르면 중단) | 프리셋마다 적용 → 2초 예열 → 6초 측정 → 스크린샷. `logs/lab-<시각>/results.txt` + png. 끝나면 `Render` 설정으로 복귀 |

- 단계 읽는 법 [추론]: `TimeUpdate`에 지난 프레임 표시 대기(`WaitForLastPresentation`)가 들어가므로 VSync·프레임 제한이 꺼진 상태에서 이 값이 크면 GPU 병목. `Update`·`PreLateUpdate`가 크면 스크립트, `PostLateUpdate`가 크면 렌더 기록·제출·UI 캔버스
- 비교는 같은 자리에 가만히 서서 (카메라가 움직이지 않게) 한다. 프리셋 `game`과 `game2`(같은 설정 반복)의 차이가 측정 흔들림
- 진단에 쓸 장면: 타이틀, 마을(MainWorld), 던전 일반 전투, 보스, 날씨(비·눈)

### 자동 측정 (0.3.0)

게임 내장 로더는 게임에 들어갈 때 모드를 불러서 타이틀을 못 본다. MelonLoader 변형은 시작 직후(`OnLateInitializeMelon`) 불리므로 타이틀부터 돌고, 아래 `Auto*` 설정으로 사람 없이 "타이틀 → 게임 시작 → 세이브 위치 → 비교 → 종료"를 한 번에 한다. 측정할 때만 개발 PC의 `UserData/LXIUM_sephiria/settings.json`에 `AutoStartSec 6, AutoLabSec 20, QuitAfterLab true`를 넣고, 끝나면 0·false로 되돌림. 던전 이동은 자동으로 못 함 (개발자 명령창은 치트라 세이브를 바꿔 쓰지 않음). 원격 화면(RustDesk 등)으로 보는 중에는 측정이 흔들리므로 보지 않을 때 돌림

### 측정 기록

| 차수 | 버전 | 결과 [관찰] |
|---|---|---|
| 1차 | 0.3.0 | 원격 화면으로 보는 중이라 무효. texel+RCAS 화면이 밝게 뜨는 문제 발견 |
| 2차 | 0.3.1 | 기본 프리셋 9개 (settings.json의 목록이 안 읽혀서 — JsonUtility 문제, 0.3.2에서 고침). texel 0.5 145 fps. RCAS 없는 texel이 어두워짐 발견 |
| 3차 | 0.3.2 | 아래 표. 밝기 모두 같음 |
| 4차 | 0.4.0 | 기본 `Render` = texel0.5가 시작 때 적용됨(`render texel0.5 → scale 0.5`). 자동 배율 ×2 → 0.36, ×3 → 0.54로 계산됨. game 122.7 / game2 117.3, texel0.5 146.1 (1% low 101), texelx2 149.5 (101), texelx3 138.3 (79), texelx2+light0.5 148.4 (102). 자동 종료 때 게임 `UIManager.DisconnectPlayerObject` NullReferenceException 32회 (게임 코드, 종료 순서) |

3차 (전투 방 `Moleland_HardBattle_2_Jail`, 1437x1000 창, Iris Xe, 2초 예열 + 8초 측정):

| 프리셋 | fps | 1% low | p50 ms | Update | PostLateUpdate | TimeUpdate |
|---|---|---|---|---|---|---|
| game / game2 | 113.9 / 119.3 | 66 / 81 | 8.41 / 8.32 | 2.50 / 2.36 | 4.10 / 4.01 | 0.80 / 0.74 |
| light0.5 | 123.2 | 79 | 7.99 | 2.21 | 3.92 | 0.73 |
| point0.5 | 150.8 | 100 | 6.58 | 1.72 | 3.23 | 0.45 |
| fsr0.5 (RCAS 0.92) | 128.1 | 74 | 7.47 | 2.28 | 3.60 | 0.65 |
| texel0.5 | 145.7 | 104 | 6.85 | 1.74 | 3.32 | 0.46 |
| texel0.36 (아트 텍셀 ×2) | 151.7 | 107 | 6.53 | 1.79 | 3.17 | 0.41 |
| point0.36 | 160.8 | 114 | 6.12 | 1.65 | 3.06 | 0.38 |
| texel0.54 (아트 텍셀 ×3) | 137.9 | 75 | 7.03 | 1.84 | 3.48 | 0.50 |
| texel0.5 + light0.5 | 144.6 | 83 | 6.75 | 1.77 | 3.35 | 0.43 |

- Texel이 Point보다 5~9 fps 느린 것은 FSR 경로의 패스 수(입력 준비 + 확대 + 마지막) 때문 [추론]. Point는 마지막 패스 하나에서 확대. Texel을 마지막 패스(`FinalPost`)에 넣으면 같은 비용이 될 것 [추론]
- 배율을 낮추면 Update·PostLateUpdate도 줄어듦 → 메인 스레드가 렌더 스레드·GPU를 기다리는 몫이 그 안에 있음 [추론]
- 0.36 이하에서는 메인 스레드 약 6 ms가 남아 이 PC는 CPU가 먼저 막힘. UHD 620에서는 GPU 몫이 3배 안팎이라 배율 효과가 더 클 것 [추론]

### CPU 내역 (5차, 0.5.0 `Profile`, 전투 방, texel0.5)

1단계(패치 없음, 8초, p50 5.69 ms) 하위 시스템 프레임당 평균 [관찰]:

| ms | 하위 시스템 | 내용 |
|---|---|---|
| 2.934 | `PostLateUpdate.FinishFrameRendering` | URP 렌더 (메인 스레드) |
| 1.483 | `Update.ScriptRunBehaviourUpdate` | MonoBehaviour.Update |
| 0.434 | `PreLateUpdate.ScriptRunBehaviourLateUpdate` | MonoBehaviour.LateUpdate |
| 0.221 | `PostLateUpdate.PlayerUpdateCanvases` | UI 캔버스 |
| 0.218 | `TimeUpdate.WaitForLastPresentationAndUpdateTime` | 표시 대기 (GPU) |
| 0.181 / 0.069 | `PreLateUpdate.DirectorUpdateAnimationBegin/End` | Animator |
| 0.144 | `EarlyUpdate.UpdateCanvasRectTransform` | UI RectTransform |
| 0.089 / 0.077 | `PostLateUpdate.UpdateAllRenderers` / `SortingGroupsUpdate` | 렌더러 경계·정렬 그룹 |
| 0.053 / 0.017 | `PreLateUpdate.NetworkLoop` / `EarlyUpdate.NetworkLoop` | Mirror |

2단계(Update·LateUpdate 860개 + 엔진 메서드 패치, 감싸는 비용 포함) 큰 것 [관찰]:

| ms/frame | 호출/frame | 이름 |
|---|---|---|
| 2.503 | 1 | `UniversalRenderPipeline.Render` (포함) |
| 2.391 | 1 | `UniversalRenderPipeline.RenderSingleCamera` (포함) |
| 0.271 / 0.264 | 1 | `RenderGraph.EndRecordingAndExecute` / `Renderer2D.OnRecordRenderGraph` |
| 0.199 | 136 | `Animator2D_SpriteRenderer.Update` |
| 0.166 / 0.063 | 104 | Mirror `NetworkTransformReliable.LateUpdate` / `Update` |
| 0.142 | 1 | `EventSystem.Update` |
| 0.116 | 81+81 | `BreakableProp.Update` (두 형식) |
| 0.091 | 8 | `TopdownRigidbody.Update` |
| 0.066 / 0.041 | 53 / 32 | `FireSpark.Update` / `Firefly.Update` |
| 0.064 | 69 | `Shadow.LateUpdate` |
| 0.026 / 0.024 | 1 | `LayerUtility.CalculateBatches` / `VolumeManager.Update` |

- `RenderSingleCamera` 2.39 ms 중 RenderGraph 기록·실행은 약 0.53 ms. 나머지 약 1.9 ms는 아직 안 쪼갬 (컬링·그리기 제출·조명 준비 후보) [추론] → 다음 측정에 `ScriptableRenderContext.Cull/Submit` 등 추가
- 스크립트는 형식 하나가 큰 게 아니라 작은 것이 많음. 가장 큰 `Animator2D_SpriteRenderer`도 0.2 ms
- 패치 전후 스크린샷이 같아 측정 패치가 그림을 바꾸지 않음 [관찰]

### 화면이 안 보인다는 보고 (2026-10-15, 재현 안 됨)

사용자: "라이트가 줄어드니까 메인화면이 아무것도 안 보이고 맵도 안 보임" (자동 측정을 원격 화면으로 보던 중). 확인한 것 [관찰]:
- 타이틀: 같은 시각에 game·texel0.5를 각각 4장 → 평균 밝기 186.0 / 184.3, 그림 같음. 장면 로드 3초 뒤는 타이틀 자체의 조명이 켜지는 중이라 어두움 (두 경우 모두)
- 전투 방: game·texel0.5·패치 전후 모두 같음. 4×4 구역 밝기도 같음
- 타이틀 배경은 세이브(승리 여부)로 정해짐, 무작위 아님
- 미확인: 월드 맵 화면(열지 못함), 밤·어두운 층, 다른 장면. 그동안 이 PC의 settings.json은 `Render: game`

## 4. 설정 (`settings.json`, 실행 중 생성. 내장 로더: `AddOns/LXIUM_sephiria/`, MelonLoader: `UserData/LXIUM_sephiria/`)

| 키 | 기본 | 뜻 |
|---|---|---|
| `ProbeKey` / `LabKey` | `F9` / `F10` | InputSystem `Key` 이름. 빈 값 = 끔 |
| `AutoProbe` | true | 장면 로드 3초 뒤 덤프 |
| `StatsIntervalSec` | 10 | 프레임 통계 간격. 0 = 끔 |
| `Render` | `texel0.5` (`RenderScale` 0.5, `Upscale` Texel) — 0.4.0부터 | 시작할 때 적용하는 렌더 설정. `LightScale`, `RenderScale`(0 이하 = 게임 값), `TexelMultiple`(0보다 크면 `RenderScale` 대신 아트 텍셀 × k 자동, `Upscale` 비면 Texel), `Upscale`(빈 값 = 게임 값, `Auto`·`Linear`·`Point`·`FSR`·`STP`·`Texel`), `Sharpness`(FSR의 RCAS 0~1, 음수 = 게임 값, Texel은 늘 0). 원래 게임으로는 `{"Name": "game"}` |
| `ArtPPU` | 16 | `TexelMultiple`이 쓰는 아트 PPU |
| `AutoStartSec` | 0 (끔) | 타이틀 로드 n초 뒤 `UI_TitleLobby.NetworkHost()`("게임 시작" 버튼과 같음, 선택된 세이브로 호스트 시작). 장면이 안 바뀌면 10초마다 최대 3번 |
| `AutoLabSec` / `AutoLabScene` | 0 (끔) / `MainWorld` | 그 장면 로드 n초 뒤 비교 실행 (실행마다 한 번) |
| `QuitAfterLab` | false | 자동 비교(와 `AutoProfile`)가 끝나면 `Application.Quit()` |
| `ProfileKey` / `ProfileSec` | `F11` / 8 | CPU 내역 측정 (MelonLoader 실행 때만). 단계마다 n초. 결과 `logs/profile-시각.txt` + 단계별 스크린샷 |
| `AutoProfile` | false | 자동 비교 뒤 이어서 CPU 내역 측정 |
| `ProfileMethods` | URP Render·RenderSingleCamera, Renderer2D.OnRecordRenderGraph, LayerUtility.CalculateBatches, RenderGraph 4개, VolumeManager.Update, CanvasUpdateRegistry.PerformUpdate, TMP_UpdateManager.DoRebuilds | 2단계에서 감쌀 엔진 메서드 `형식:메서드` (포함 시간) |
| `LabWarmupSec` / `LabMeasureSec` | 2 / 8 | 비교 예열·측정 시간 |
| `LabPresets` | game, texel0.5, texelx2, texelx3, texelx2light0.5, game2 | 비교할 설정 목록 (약 60초). 파일에 목록이 있으면 그 목록만 씀 |

- 예전 버전이 쓴 settings.json에 `Render`가 있으면 그 값을 계속 씀 (0.3.2가 쓴 파일은 `game`). 새 기본값을 받으려면 `Render` 항목을 지움

- 0.3.2부터 Newtonsoft.Json(게임에 든 13.0.0.0)으로 읽고 씀. 0.3.1까지는 Unity `JsonUtility`가 실행 중 불러온 어셈블리의 중첩 클래스(`Render`, `LabPresets`)를 건너뛰어 **사용자의 `Render` 설정이 적용되지 않았음** [관찰: 쓰인 settings.json에 두 항목이 없었음]

## 5. 다음 단계

1. 기본을 `TexelMultiple`(×2~3)로 바꿀지: 0.5 고정은 1080p에서 정확히 ×3(540줄), 1000줄에서 ×2.78. 화면 크기와 상관없이 같은 품질·비용을 원하면 자동이 맞음
2. B1 2단계: 카메라 위치를 렌더 픽셀 격자에 맞춰(텍셀 경계 떨림 제거) 진짜 "텍셀 격자" 렌더로
3. C1을 `FinalPost` 자리로 옮겨 Point와 같은 패스 수로 (Texel이 Point보다 느린 몫 제거)
4. 이 PC는 CPU가 먼저 막힘 → PostLateUpdate(렌더 기록·UI) 내역부터 재고 B6·B7·A3
5. 마을·보스·날씨 장면 측정 (이동이 필요해 사람이 하거나 화면 조작 허용 필요)

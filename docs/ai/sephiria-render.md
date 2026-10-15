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
| A1 | 조명 텍스처 배율 | `Renderer2DData.m_LightRenderTextureScale` (반사로 씀. 매 프레임 `CreateResources`가 읽어 바로 적용 [관찰]) | 조명 경계가 부드러워짐. 쿠키(픽셀 아트 조명) 경계가 흐려질 수 있음 [추론] | 구현 0.1.0 `LightScale`, lab 비교 |
| A2 | 월드 렌더 배율 | URP `renderScale` + `upscalingFilter`(Point·FSR 등). 화면 공간 오버레이 UI는 영향 없음 [추론] | Point: 픽셀이 굵어짐, FSR: 픽셀 아트가 뭉개질 수 있음 | 구현 0.1.0 `RenderScale`/`Upscale`, lab 비교 |
| A3 | 볼륨 갱신 줄이기 | `volumeFrameworkUpdateMode = ViaScripting` + 프로필·가중치가 바뀔 때만 `VolumeManager.Update` | 없음 (바뀜 감지가 맞으면) | 후보. 볼륨 수·CPU 비중 확인 후 |
| A4 | 블룸 가볍게 | 볼륨 Bloom의 `downscale`(Half → Quarter), `maxIterations` 줄이기 | 번짐 범위가 약간 달라짐 | 후보. probe로 블룸 값 확인 후 |

### B. LXIUM 자체 렌더 경로

힌트(Spatial Resolution, Temporal Reprojection, Opaque Ice, Render Fast, Async)를 2D 직교 픽셀 아트에 맞게 다시 만든다.

| ID | 이름 | 방법 | 힌트 | 상태 |
|---|---|---|---|---|
| B1 | 텍셀 격자 렌더 (TGR) | 월드를 "아트 텍셀 격자 × k" 해상도로 그림. 직교 높이 × PPU = 화면 세로 텍셀 수 (예: 5.63 × 2 × 16 = 180) → k = 2~3이면 360~540줄. 조명·블룸·안개는 원래 저주파라 이 해상도로 충분. 화면으로는 C1(선명 쌍선형)로 올림. 오버레이 UI는 원래 해상도 | Spatial Resolution. 픽셀 아트는 텍셀보다 촘촘히 그려도 정보가 늘지 않으므로 손실이 거의 없는 판 [추론] | 후보. PPU·직교 크기 probe 필요. 첫 단계는 A2를 정수배에 맞추기 |
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
6. 다시 읽기 확인: UnityPy로 번들을 다시 열어 셰이더 이름·변형 수·컨테이너 확인, 픽셀 코드 디스어셈블(`ps_5_0`, `CB0[139]`, `s0`, `t0`, 입력 `v1.xy`) [관찰]. 게임 로드는 [미확인]

**C1이 들어가는 자리**: URP FSR 경로 = 입력 준비(`Scaling Setup`) → `PostProcessMaterialLibrary.easu` 재질로 `Blitter.BlitTexture` → `FinalPost`의 RCAS 선명화 [관찰: `PostProcessPassRenderGraph.RenderFinalFSRScale`]. EASU 셰이더(`Hidden/Universal Render Pipeline/Edge Adaptive Spatial Upsampling`)는 변형 1개, 바인딩 `_BlitTexture` t0 + 인라인 선형 클램프 샘플러 s0 + `$Globals` b0(`_ScreenParams` 368, `_FsrEasuConstants0~3` 2192~2240) [관찰]. 같은 바인딩으로 픽셀 코드만 바꾸면 그 재질의 `shader`만 바꿔 URP가 우리 확대를 쓰고, 뒤의 RCAS가 적응형 선명화가 됨 (Spatial Resolution 힌트의 "적응형 선명화 + 업스케일"을 픽셀 아트용으로)

| ID | 셰이더 | 내용 | 쓰는 곳 | 상태 |
|---|---|---|---|---|
| C1 | `LXIUM/TexelUpscale` | 선명 쌍선형: 입력 픽셀 좌표 = `uv / con1.xy`, 가장 가까운 텍셀 경계 주변 출력 1픽셀 폭만 보간(`clamp((x - 경계) / con0.x, ±0.5)`), 나머지는 텍셀 색 그대로. 텍스처 읽기 1회 (EASU는 12회) | A2를 `Upscale = Texel`로 (FSR 필터 + EASU 재질 셰이더 교체), 이후 B1·B2 | 구현 0.2.0, 게임 확인 [미확인] |
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

게임 폴더 `AddOns/LXIUM_sephiria/` 아래에 남음.

| 기능 | 키·설정 | 결과 |
|---|---|---|
| 단계별 프레임 통계 | 10초마다 (`StatsIntervalSec`) | `Player.log`에 `[LXIUM] frame scene=… fps … phase-ms TimeUpdate=… Update=… PreLateUpdate=… PostLateUpdate=… draws setpass batches gcKB/frame` |
| 렌더 상태 덤프 | F9, 장면이 바뀌면 3초 뒤 자동 (`AutoProbe`) | `logs/probe-<장면>-<시각>.txt`: 파이프라인 필드 전체, 블렌드 스타일, 정렬 레이어, 카메라, 2D 조명 목록, 볼륨 프로필 값, 보이는 렌더러(종류·셰이더·PPU·정렬 레이어), 타일맵, 파티클, 캔버스, 스크립트 수(Update/LateUpdate 여부) |
| 설정 비교 (lab) | F10 (다시 누르면 중단) | 프리셋마다 적용 → 2초 예열 → 6초 측정 → 스크린샷. `logs/lab-<시각>/results.txt` + png. 끝나면 `Render` 설정으로 복귀 |

- 단계 읽는 법 [추론]: `TimeUpdate`에 지난 프레임 표시 대기(`WaitForLastPresentation`)가 들어가므로 VSync·프레임 제한이 꺼진 상태에서 이 값이 크면 GPU 병목. `Update`·`PreLateUpdate`가 크면 스크립트, `PostLateUpdate`가 크면 렌더 기록·제출·UI 캔버스
- 비교는 같은 자리에 가만히 서서 (카메라가 움직이지 않게) 한다. 프리셋 `game`과 `game2`(같은 설정 반복)의 차이가 측정 흔들림
- 진단에 쓸 장면: 타이틀, 마을(MainWorld), 던전 일반 전투, 보스, 날씨(비·눈)

## 4. 설정 (`AddOns/LXIUM_sephiria/settings.json`, 실행 중 생성)

| 키 | 기본 | 뜻 |
|---|---|---|
| `ProbeKey` / `LabKey` | `F9` / `F10` | InputSystem `Key` 이름. 빈 값 = 끔 |
| `AutoProbe` | true | 장면 로드 3초 뒤 덤프 |
| `StatsIntervalSec` | 10 | 프레임 통계 간격. 0 = 끔 |
| `Render` | 모두 게임 값 | 시작할 때 적용하는 렌더 설정. `LightScale`, `RenderScale`(0 이하 = 게임 값), `Upscale`(빈 값 = 게임 값, `Auto`·`Linear`·`Point`·`FSR`·`STP`·`Texel`), `Sharpness`(RCAS 0~1, 음수 = 게임 값) |
| `LabWarmupSec` / `LabMeasureSec` | 2 / 6 | 비교 예열·측정 시간 |
| `LabPresets` | game, light0.5, light0.25, point0.5, fsr0.5, texel0.5(선명화 0), texel0.5rcas(게임 선명화), texel0.75, game2 | 비교할 설정 목록 (약 75초). 0.1.0에서 만든 settings.json은 목록이 예전 것이라 지우면 새 기본값으로 다시 만들어짐 |

## 5. 다음 단계

1. 0.2.0을 위 장면들에서 실행 → `shader 'LXIUM/TexelUpscale' supported=True` 로그와 lab의 texel 스크린샷으로 C1 확인, probe·lab·frame 로그 수집
2. 병목에 따라: GPU면 A1·A2(+C1) 기본값 결정 → B1 → B2 / CPU면 B6·B7·A3
3. C2 이후 셰이더는 같은 도구로 (원본 셰이더의 변형·바인딩부터 디스어셈블로 확인)

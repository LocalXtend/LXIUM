# 마법소녀의 마녀재판 렌더링 최적화 설계 (AI용)

- 사람용 문서: `../human/mahosaba-render.md`
- 기반 사실: `mahosaba.md` (구조·근거). 이 문서는 그 위에서 LXIUM 고유 렌더링을 설계함
- 목표: 내장 GPU(Intel UHD 620 ~ Iris Xe)에서 체감 프레임 향상. 대사·UI는 원래 해상도 유지
- 표기: [관찰] [추론] [미확인]. 효과 수치는 측정 전까지 모두 [미확인]

## 1. 출발점

| 비용 | 위치 | 근거 |
|---|---|---|
| 레이어 캐릭터마다 URP 카메라 전체 렌더 (1500x3000) | `LayeredDrawer.DrawWithCamera` | mahosaba.md 5.3-3 |
| 모든 액터(캐릭터·배경)의 전환 셰이더 RT 복사, 매 프레임 | `TransitionalSpriteRenderer.Update` | 5.3-5 |
| 3D 템플릿 설정(MSAA 4x, SSAO, 중간 텍스처 Always)이 모든 카메라에 | URP 에셋 | 4장 |
| 무대 카메라 2대가 화면 분할과 무관하게 함께 렌더 | `StageCameraController` | 6.2 |
| 프레임 상한 60/30, VSync는 품질 단계 | `CameraManagerExtended` | 8장 |

화면에 실제로 보이는 것(배경 1장 + 캐릭터 몇 명 + UI)은 가볍고, 비용 대부분은 **결과가 같은 화면 밖 렌더를 매 프레임 반복**하는 데서 나온다 [추론].

## 2. 기법

### A. 설정 교정 (화면 변화 없음, 구현됨)

| ID | 이름 | 방법 | 화면 변화 | 설정 |
|---|---|---|---|---|
| A1 | 2D 카메라 MSAA 끄기 | `beginCameraRendering`에서 직교 카메라만 `allowMSAA=false`. 스프라이트·UI는 가장자리가 투명이라 MSAA 효과 없음. 무대 카메라는 RT의 2x 유지 | 없음 [추론] | `Urp.MsaaOff` |
| A2 | SSAO 무대 전용 | 같은 콜백에서 SSAO 기능을 원근(무대) 카메라일 때만 `SetActive(true)`. 2D 카메라는 불투명 물체가 없어 AO가 나오지 않음 | 없음 [추론] | `Urp.Ssao` |
| A3 | 중간 텍스처 Auto | `UniversalRendererData.m_IntermediateTextureMode = Auto` + `SetDirty()`. 캐릭터 합성 카메라처럼 후처리 없는 카메라는 RT에 바로 그림 | 없음 [추론] | `Urp.IntermediateAuto` |
| A4 | 프레임 제한 해제 | 매 프레임 `targetFrameRate`·VSync를 비교해 다를 때만 설정. VSync는 icall | 설정에 따름 | `FrameRate.TargetFps` |

### B. LXIUM 자체 렌더 경로

| ID | 이름 | 방법 | 대응 힌트 | 상태 |
|---|---|---|---|---|
| B1 | 변경분만 합성 | `LayeredDrawer.DrawLayers` 앞에서 레이어 상태 해시(켜짐·스프라이트·색·재질·레이어·PPU)를 비교, 같으면 이전 RT 그대로 반환 | Temporal Reprojection을 노벨에 맞게: 생성(GEN) 프레임 대신 **같은 결과를 재사용**하므로 아티팩트 없음 | 구현 `Character.ChangeOnly` |
| B2 | 전환 복사 생략 | `TransitionalRenderer.RenderToTexture` 앞에서 입력 해시(메인·전환 텍스처, 진행도, 색, 블러, 뒤집기, 대상 RT)가 같으면 생략. 메인 텍스처가 B1이 그린 RT면 그 버전 번호를 해시에 넣음. 출처 모르는 RT(다이스·영상)는 항상 그림 | 같음 | 구현 (B1과 같은 설정) |
| B3 | 액터 원래 해상도 | 레이어 캐릭터 메타데이터 PPU를 `화면 높이 / 1440`배로(1080p: 0.75 → 1125x2250, 픽셀 56%). 합성 RT와 화면 쿼드가 같이 줄어 화면상 크기 그대로 | Spatial Resolution을 "월드 = 화면 밖 액터 합성"에만 적용. UI·대사는 메인 카메라라 영향 없음 | 구현 `Character.Resolution` |
| B4 | 무대 가시성 컬링 | 게임이 무대를 띄운 동안에도, RawImage의 상속 알파가 0인 화면의 카메라는 끔 | — | 구현 `Stage.HiddenSkip` (2차: -10.2 ms) |
| B5 | 무대 해상도 | `RenderTexture_1/2`를 화면 높이에 맞춰 줄임(원래 화면과 무관하게 1920x1080). RawImage가 화면 크기로 보여 주므로 화면보다 큰 RT는 낭비. 해상도를 바꾸면 따라감 | Spatial Resolution | 구현 `Stage.Resolution` (기본 0 = 화면 맞춤, 0.1.1) |
| B6 | 무대 교차 갱신 | 두 무대 카메라를 프레임마다 번갈아 그림(각 30Hz). 불·카메라 흔들기가 반 프레임 늦음 | Temporal Frame Reprojection | 후보 |
| B7 | 제로 카피 액터 | `TransitionalSpriteRenderer`의 RT 단계를 없애고 `DrawMesh`에서 전환 셰이더로 화면에 바로 그림 (전환·색·블러를 한 패스로) | Render Fast | 후보 (C1 필요할 수 있음) |
| B8 | 절차적 레이어 합성 | 캐릭터의 `renderCamera`를 비워 Naninovel 절차적 경로(CommandBuffer)로. URP 카메라 1회가 DrawMesh 몇 개로 바뀜. 스텐실 마스크 재질 호환 확인 필요 | Render Fast | 후보 |
| B9 | 숨은 UI 정지 | 캔버스만 꺼진 화면의 TrueShadow·애니메이션 컴포넌트를 끔 | — | 후보 (CPU 측정 후) |

B1·B2가 켜지면 대사만 넘기는 장면에서 화면 밖 렌더는 거의 0이 되고, 남는 것은 메인 카메라의 화면 그리기뿐이다 (2차: 재판에서 합성 11회/프레임 → 1초 안전 갱신만, -8.9 ms) [관찰: mahosaba.md 10장]. 표정이 바뀌는 프레임에만 B1 1회 + B2 1회가 생긴다. 안전장치로 입력이 같아도 1초마다 한 번은 다시 그리되, 여러 액터의 안전 갱신이 한 프레임에 몰리지 않게 프레임당 B1·B2 각 1개로 제한한다 (0.1.1).

### C. 자체 셰이더 (준비 필요)

- 셰이더는 실행 중 컴파일이 안 되므로 Unity 에디터에서 AssetBundle로 빌드해야 함. **같은 6000.0.x 에디터**가 필요 (이 PC의 6000.5.7은 번들 호환이 보장되지 않음) [추론]. D3D11·D3D12·Vulkan 변형 포함
- 게임 환경(색조 전환, 불빛, 블러, 페이드)과 맞물리도록, 게임 재질의 프로퍼티를 그대로 받는 "융합" 셰이더로 설계

| ID | 셰이더 | 내용 | 게임 연동 |
|---|---|---|---|
| C1 | `LXIUM/ActorDirect` | 전환(Naninovel `TransitionalTexture`의 전환 키워드 중 실제 쓰는 것만) + 색조 + 불투명도 + 선명화를 한 패스로 화면에 | `TransitionalMaterial` 프로퍼티(메인·전환 텍스처, 진행도, 색), 깊이 패스 컷오프 |
| C2 | `LXIUM/Sharpen` | B3·B5로 줄인 RT를 화면에 그릴 때 RCAS(적응형 선명화) | B3·B5 배율을 보고 강도 조절 |
| C3 | `LXIUM/FireLite` | 법정 불 쿼드를 노이즈 1장 + 플립북으로 | `Court.ChangeFireFxActivity`의 켜짐·불빛 색 |
| C4 | `LXIUM/CourtLite` | 법정 Lit 재질을 스포트라이트 2개 고정 조명용 단순 셰이딩으로 | 스포트라이트 색(불빛 색 전환) |

UHD 620에서 120fps: A+B1+B2 뒤 대화 장면의 매 프레임 일은 1080p 화면에 쿼드 몇 장 + UI라서 가능성이 높다 [추론]. 재판 장면은 Iris Xe 768p 창 모드에서 A+B1+B2+B4로 72fps (mahosaba.md 10장 2차). 남은 무대 카메라 1대(Lit + 불 + SSAO)를 B5·B6·C3·C4로 줄여야 한다 [추론].

### 채택하지 않는 것

| 힌트 | 이유 |
|---|---|
| Async (병렬 처리) | 노벨 진행·Unity API는 메인 스레드 전용. 렌더는 이미 gfx-jobs(`gfx-threading-mode=6`) [관찰] |
| Opaque Ice (투명 → 불투명) | B1·B2 뒤에는 투명 쿼드 몇 장만 남아 이득이 작음 [추론] |
| 메인 카메라 Render Scale + FSR | 메인 카메라에 UI·대사가 같이 그려져 글자가 흐려짐 (mahosaba.md 7장). B3이 같은 효과를 월드에만 줌 |

## 3. 검증 절차 (렌더 실험실)

1. 설정 `Debug.RenderLab = true`
2. 캐릭터가 나오는 장면(대화) 또는 재판 장면에서 **F10**
3. 프리셋 7개를 8초씩(안정 2초 + 측정 6초): `0_original` → `1_msaa` → `2_ssao` → `3_intermediate` → `4_changeOnly` → `5_hiddenSkip` → `6_stageRes` (앞 단계에 하나씩 더함. 0~5는 무대 RT 원래 크기)
4. 측정 중에는 프레임 제한·VSync를 끔. 결과는 `BepInEx/LXIUM_mahosaba/<시각>/lab.txt`와 프리셋별 스크린샷(JPG)
5. 기록 값: fps, 프레임 시간 avg·p50·p95·p99·max, 프레임당 카메라 렌더 수, `layerDraw`/`layerSkip`(B1), `copyDraw`/`copySkip`(B2), `stageSkip`(B4)
6. `0_original`의 `layerDraw`가 프레임당 보이는 캐릭터 수와 같으면 mahosaba.md 5.4의 [미확인]이 확인됨
7. 스크린샷을 비교해 A·B의 "화면 변화 없음"을 확인
8. `Character.Resolution`은 액터 생성 때 적용되므로 실험실 안에서 바뀌지 않음. 값을 바꾸고 다시 실행해 비교

## 4. 설정 (`BepInEx/config/LXIUM_mahosaba.cfg`)

| 키 | 기본값 | 화면 변화 |
|---|---|---|
| `FrameRate.TargetFps` | 0 (모니터 주사율 + VSync) | -1 = 제한 없음(VSync 끔), N = N fps(VSync 끔) |
| `Urp.MsaaOff` | true | 없음 |
| `Urp.Ssao` | StageOnly | 없음. `Off`면 법정 구석 음영이 약간 밝아짐 |
| `Urp.IntermediateAuto` | true | 없음 |
| `Character.ChangeOnly` | true | 없음 |
| `Character.Resolution` | 0 (화면 맞춤) | 1440p 미만에서 캐릭터를 확대하는 연출은 약간 부드러워짐 |
| `Stage.Resolution` | 0 (화면 맞춤) | 1080p 이상에서는 원래와 같음. 그보다 낮은 화면에서는 원래 RT를 줄여 보이던 것을 화면 크기로 바로 그림. 0보다 큰 값은 고정 배율 |
| `Stage.HiddenSkip` | true | 없음 |
| `Debug.RenderLab` | false | — |

게임 옵션의 "포스트 프로세싱"을 끄면 메인 카메라에서 빠지는 것은 FXAA뿐이다 (볼륨 효과가 모두 0, mahosaba.md 4장). 글자가 약간 선명해지고 후처리 패스가 줄어든다 [추론].

## 5. 미확인

- 각 기법의 실제 효과 (Iris Xe 측정 전)
- B2의 해시가 놓치는 입력이 있는지 (전환 중 시간 기반 효과 등). 놓쳐도 1초 안에 다시 그림
- B3 적용 시 확대 연출의 화질
- C 셰이더용 6000.0.x 에디터 설치 여부

# Sephiria 렌더링 최적화 설계 (AI용)

- 사람용 문서: `../human/sephiria-render.md`
- 기반 사실: `sephiria.md` (구조·측정). 이 문서는 그 위에서 LXIUM 고유 렌더링 기능을 설계함
- 목표: 저사양 내장 GPU(Intel UHD 620급)에서 체감 프레임 향상. HUD·글자는 원래 해상도 유지
- 표기: [관찰] [추론] [미확인], 효과 수치는 측정 전까지 모두 [미확인]

## 1. 전제

| 항목 | 내용 |
|---|---|
| 렌더러 | URP 2D (`Renderer2DData`), 카메라 1대(`Main Camera`, 직교), 타이틀에만 `WaterCamera` 추가 [관찰] |
| 픽셀아트 해상도 | 기본 `orthographicSize` 5.625 = 360/64 → 32 PPU 기준 세로 360px(640×360) [추론]. 줌은 `TargetTracker`가 바꿈 [관찰] |
| HUD | 일부 UI가 `Canvas.worldCamera` 사용 → Screen Space-Camera 캔버스가 있을 수 있음 [관찰: 정적]. 렌더 스케일을 낮추면 이런 캔버스도 같이 흐려질 수 있음 [추론] |
| 런타임 변경 가능 | `renderScale`, `upscalingFilter`(Auto/Linear/Point/FSR/STP), `fsrSharpness`, `supportsHDR`, `msaaSampleCount`, 카메라 `allowHDR`·`renderPostProcessing`, `Light2D.shadowsEnabled` (공개 API) / `Renderer2DData.m_LightRenderTextureScale` (리플렉션 + `SetDirty()`) [관찰] |
| 그래픽 API | 기본 D3D11, D3D12 동봉 [관찰]. 아래 A·B 단계는 Unity 추상화만 쓰므로 API 무관 [추론] |
| 로더 | 모든 단계가 AddOns 로더 안에서 가능. BepInEx 등은 타이틀 이전 적용이 필요할 때만 검토 [추론] |
| 개발 PC 측정 | 창 1437×1000에서는 GPU 병목 아님(`sephiria.md` 8장). 저사양 GPU·전체 화면 고해상도에서는 다를 수 있음 [미확인] |

## 2. 제안 기법

### A. URP 기능 조합 (위험 낮음, API 무관)

| ID | 이름 | 방법 | 대응 힌트 |
|---|---|---|---|
| A1 | 공간 해상도 | `renderScale` < 1 + `upscalingFilter=FSR`(EASU 업스케일 + RCAS 적응형 선명화) | Spatial Resolution |
| A2 | 픽셀 정렬 해상도 | 월드를 픽셀아트 원본 해상도의 정수배(360·720)로 렌더 후 `Point` 업스케일. 1080p에서 360은 정확히 3배 → 픽셀아트는 거의 무손실, 픽셀 수 1/9 [추론] | Spatial Resolution의 픽셀아트 전용판 |
| A3 | 조명 해상도 | `m_LightRenderTextureScale` 1 → 0.5/0.25. 2D 조명은 부드러운 그라데이션이라 저해상도가 잘 안 보임 [추론] | — |
| A4 | 덮개 레이어 정리 | 화면 대부분을 덮는 반투명 스프라이트(안개·분위기·암전·날씨)를 찾아, 알파가 거의 0이면 그리지 않고 저사양에서는 날씨 파티클 축소 | Opaque Ice(투명 레이어 비용) |
| A5 | 후처리 단계 | HDR 끄기(색 보정이 이미 LDR), 저사양에서 후처리 끄기, `volumeFrameworkUpdateMode` 조정 | — |
| A6 | 프레임 페이싱 | 노트북 발열 스로틀링 방지용 프레임 상한·`maxQueuedFrames` | — |

### B. LXIUM 자체 렌더 경로 (위험 중간)

| ID | 이름 | 방법 | 대응 힌트 |
|---|---|---|---|
| B1 | 월드/HUD 분리 | 메인 카메라를 저해상도 RenderTexture에 그리고, HUD 아래의 전체 화면 RawImage로 표시. HUD 캔버스가 카메라 공간이어도 HUD는 원래 해상도 유지 | Spatial Resolution의 "HUD 제외" |
| B2 | 2D 카메라 재투영 | 월드를 격 프레임만 렌더(REAL), 나머지 프레임(GEN)은 직전 월드 이미지를 카메라 이동량만큼 평행이동(RawImage `uvRect`). 직교 2D 카메라라 배경은 정확히 맞고, 움직이는 적·탄환만 반프레임 늦음(아티팩트). HUD는 매 프레임 렌더 | Temporal Frame Reprojection |

### C. 자체 셰이더 (위험 높음, 준비 필요)

- 픽셀아트용 선명화, 저비용 조명 셰이더 등. Unity 6000.3.21f1 에디터로 같은 URP 버전 AssetBundle을 빌드해야 하고, 그래픽 API별 변형 포함 필요. A·B 결과를 본 뒤 결정

### 채택하지 않는 것

| 힌트 | 이유 |
|---|---|
| Render Fast (드로우 일괄 처리) | SetPass 100~200, Draw 140~350에 SRP Batcher 이미 켜짐 [관찰]. 줄일 여지 작음 [추론] |
| Async (엔티티 병렬 처리) | Unity API·Mirror 네트워크 상태는 메인 스레드 전용 → 충돌·동기화 위험. Burst는 빌드 시점 컴파일이라 모드에서 추가 불가 [추론] |

## 3. 검증 절차

1. 렌더 실험실(`src/RenderLab.cs`, `config.json`의 `renderLab`)로 측정
   - 인벤토리: 캔버스 렌더 모드, 스프라이트 PPU·필터, 화면 25% 이상 덮는 렌더러, Light2D 구성
   - 벤치: 게임 월드 진입 20초 뒤 프리셋을 8초씩 차례로 적용, 프레임 통계·스크린샷·에러 로그 기록 후 원복
2. 전체 화면 원래 해상도에서 측정 (창 모드 1437×1000은 GPU 부담이 작음)
3. 스크린샷으로 HUD 선명도·픽셀아트 품질 확인
4. 효과 있는 것만 기능으로 만들고 `config.json`으로 켜고 끔. 기본값은 화질 손실 없는 것만

## 4. 미확인

- FSR·STP 셰이더가 게임 빌드에 포함됐는지 (URP 코드는 있음 [관찰])
- HUD 캔버스 렌더 모드
- 저사양 GPU 실기 측정 (UHD 620 장비 없음)

// LXIUM/TexelUpscale — 픽셀 아트 확대 (선명 쌍선형). URP FSR 경로의 EASU 자리에 들어간다.
// 텍셀 안쪽은 텍셀 색 그대로, 텍셀 경계에서만 출력 픽셀 1개 폭으로 보간 → 배율이 정수가 아니어도
// 픽셀 폭이 고르고(최근접의 들쭉날쭉 없음) 흐려지지 않으며(쌍선형·FSR의 번짐 없음), 카메라가 텍셀 이하로 움직여도 떨림이 적다.
// 바인딩은 원본 EASU 메타데이터와 같아야 함: _BlitTexture t0, 선형 클램프 샘플러 s0, $Globals b0.
// FSR 상수 (FSRUtils.SetEasuConstants): con0.xy = 입력 뷰포트 / 출력 크기, con1.xy = 1 / 입력 텍스처 크기.

cbuffer _Globals : register(b0)
{
    float4 _FsrEasuConstants0 : packoffset(c137);
    float4 _FsrEasuConstants1 : packoffset(c138);
};

Texture2D _BlitTexture : register(t0);
SamplerState sampler_LinearClamp : register(s0);

float4 frag(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_Target
{
    float2 inPx = uv / _FsrEasuConstants1.xy;            // 입력 픽셀 좌표 (정수 = 텍셀 경계)
    float2 outPerIn = 1.0 / _FsrEasuConstants0.xy;        // 입력 텍셀 하나가 덮는 출력 픽셀 수
    float2 seam = floor(inPx + 0.5);                      // 가장 가까운 텍셀 경계
    float2 d = clamp((inPx - seam) * outPerIn, -0.5, 0.5);
    return _BlitTexture.SampleLevel(sampler_LinearClamp, (seam + d) * _FsrEasuConstants1.xy, 0);
}

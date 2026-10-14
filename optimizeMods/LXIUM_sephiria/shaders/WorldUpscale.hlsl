// Serialized bindings match Unity 6000.3's fullscreen pass; build_world_upscale.py replaces its GPU code.
cbuffer Globals : register(b0)
{
    float _FlipY : packoffset(c133.x);
};
cbuffer UnityPerMaterial : register(b1)
{
    float _Intensity : packoffset(c1.x);
};
Texture2D<float4> _BlitTexture : register(t2);

struct Varyings
{
    float4 position : SV_POSITION;
    float4 uv : INTERP0;
    float4 unused : INTERP1;
};

Varyings vert(uint id : SV_VertexID)
{
    Varyings o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.position = float4(uv * 2 - 1, 1, 1);
    o.uv = float4(uv.x, _FlipY < 0.5 ? 1 - uv.y : uv.y, 1, 1);
    o.unused = 0;
    return o;
}

float4 LoadClamped(int2 p, int2 size)
{
    return _BlitTexture.Load(int3(clamp(p, int2(0, 0), size - 1), 0));
}

float3 StraightColor(float4 sample, float3 fallback)
{
    return sample.a > 1e-6 ? sample.rgb / sample.a : fallback;
}

float4 frag(Varyings i) : SV_Target
{
    uint width, height;
    _BlitTexture.GetDimensions(width, height);
    int2 size = int2(width, height);
    float2 position = i.uv.xy * size - 0.5;
    int2 origin = int2(floor(position));
    float2 fraction = frac(position);
    // Interpolate premultiplied color before sharpening to avoid dark transparent borders.
    float4 center = lerp(
        lerp(LoadClamped(origin, size), LoadClamped(origin + int2(1, 0), size), fraction.x),
        lerp(LoadClamped(origin + int2(0, 1), size), LoadClamped(origin + int2(1, 1), size), fraction.x), fraction.y);
    if (center.a <= 1e-6) return float4(0, 0, 0, center.a);
    if (_Intensity <= 0) return center;
    float3 color = center.rgb / center.a;
    int2 nearest = int2(floor(position + 0.5));
    float3 north = StraightColor(LoadClamped(nearest + int2(0, -1), size), color);
    float3 south = StraightColor(LoadClamped(nearest + int2(0, 1), size), color);
    float3 east = StraightColor(LoadClamped(nearest + int2(1, 0), size), color);
    float3 west = StraightColor(LoadClamped(nearest + int2(-1, 0), size), color);
    float3 low = min(color, min(min(north, south), min(east, west)));
    float3 high = max(color, max(max(north, south), max(east, west)));
    float3 luma = float3(0.2126, 0.7152, 0.0722);
    float contrast = dot(high - low, luma) / max(dot(high, luma), 1e-4);
    float weight = saturate(_Intensity) * 0.25 * (1 - saturate(contrast));
    color = clamp(color + weight * (4 * color - north - south - east - west), max(low, 0), high);
    return float4(color * center.a, center.a);
}

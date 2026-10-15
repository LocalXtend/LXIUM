"""LXIUM 세피리아 셰이더 번들 생성 (docs/ai/sephiria-render.md C장)

Unity 에디터 없이 만든다 (이 PC의 에디터는 게임과 버전이 다름). 게임 에셋의 원본 셰이더 객체를 틀로 쓰고,
고른 D3D11 변형의 픽셀 코드만 LXIUM HLSL을 d3dcompiler_47로 컴파일한 것으로 바꾼다. 속성·바인딩 메타데이터는
원본 그대로라 엔진이 원본과 같은 자리에 텍스처·상수를 넣는다. 세피리아 빌드는 D3D11 셰이더만 들어 있다.

번들 그릇은 이 저장소의 LXIUM_mahosaba 번들(6000.0.48f1)을 빌려 게임 버전(6000.3.21f1)으로 바꾼다.
세피리아에는 에셋 번들이 없어서 그릇으로 쓸 게임 번들이 없다. 타입 트리는 UnityPy TPK의 6000.3.21f1 것을 넣는다.

사용 (Windows, Python 3 + UnityPy, d3dcompiler_47.dll):
    python build_shaders.py <게임 폴더> <출력 번들 경로>
"""
import ctypes
import hashlib
import os
import struct
import sys

import lz4.block
import UnityPy
from UnityPy.helpers import Tpk
from UnityPy.helpers.UnityVersion import UnityVersion

GAME_VERSION = "6000.3.21f1"
D3D11 = 4                  # ShaderCompilerPlatform.D3D
UNITY_HEADER = 38          # Unity가 DXBC 앞에 붙이는 헤더 길이
TEMPLATE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "LXIUM_mahosaba", "res", "lxium_mahosaba.bundle")

# GPU 프로그램 종류: 16 = DX11 정점 SM5, 18 = 픽셀 SM5
SHADERS = [
    {
        # URP FSR 경로의 EASU 자리에 넣는 픽셀 아트 확대. 바인딩: _BlitTexture t0, 선형 클램프 s0, $Globals b0
        "file": "globalgamemanagers.assets",
        "source": "Hidden/Universal Render Pipeline/Edge Adaptive Spatial Upsampling",
        "name": "LXIUM/TexelUpscale",
        "hlsl": "TexelUpscale.hlsl",
        # (종류, 남길 변형의 키워드 집합, 정의, 대상, 진입점). 규칙이 없는 단계는 원본 코드 유지
        "fragment": [(18, set(), [], "ps_5_0", "frag")],
    },
]


def blob_bytes(blob):
    vt = ctypes.cast(ctypes.cast(blob, ctypes.POINTER(ctypes.c_void_p))[0], ctypes.POINTER(ctypes.c_void_p))
    ptr = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(vt[3])(blob)
    size = ctypes.WINFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(vt[4])(blob)
    return ctypes.string_at(ptr, size)


class Macro(ctypes.Structure):
    _fields_ = [("name", ctypes.c_char_p), ("definition", ctypes.c_char_p)]


def compile_hlsl(src, name, entry, target, defines):
    d3d = ctypes.WinDLL("d3dcompiler_47.dll")
    f = d3d.D3DCompile
    f.argtypes = [ctypes.c_char_p, ctypes.c_size_t, ctypes.c_char_p, ctypes.c_void_p, ctypes.c_void_p,
                  ctypes.c_char_p, ctypes.c_char_p, ctypes.c_uint, ctypes.c_uint,
                  ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p)]
    macros = (Macro * (len(defines) + 1))(*[Macro(d.encode(), b"1") for d in defines], Macro(None, None))
    code, err = ctypes.c_void_p(), ctypes.c_void_p()
    hr = f(src, len(src), name.encode(), ctypes.cast(macros, ctypes.c_void_p), None, entry.encode(), target.encode(),
           1 << 15, 0, ctypes.byref(code), ctypes.byref(err))  # D3DCOMPILE_OPTIMIZATION_LEVEL3
    if hr != 0:
        raise SystemExit(f"{name} {defines}: " + (blob_bytes(err).decode(errors="replace") if err else hex(hr & 0xFFFFFFFF)))
    return blob_bytes(code)


def u32(b, p):
    return struct.unpack_from("<I", b, p)[0]


def align4(n):
    return (n + 3) & ~3


def split_entries(raw):
    """플랫폼 블롭 = 개수 + (오프셋, 길이, 세그먼트) 표 + 항목들 (서브프로그램·파라미터 블롭)"""
    n = u32(raw, 0)
    return [raw[o:o + l] for o, l, _ in (struct.unpack_from("<III", raw, 4 + 12 * i) for i in range(n))]


def join_entries(entries):
    head, body = struct.pack("<I", len(entries)), b""
    base = 4 + 12 * len(entries)
    for e in entries:
        head += struct.pack("<III", base + len(body), len(e), 0)
        body += e
    return head + body


def replace_code(entry, dxbc):
    """서브프로그램: 버전, 종류, 통계 4개, 키워드 목록, 코드(길이 + Unity 헤더 + DXBC, 4바이트 정렬), 나머지"""
    p = 24
    count = u32(entry, p)
    p += 4
    for _ in range(count):
        p += 4 + align4(u32(entry, p))
    length = u32(entry, p)
    code = entry[p + 4:p + 4 + length]
    if code[UNITY_HEADER:UNITY_HEADER + 4] != b"DXBC":
        raise SystemExit("unexpected subprogram layout")
    rest = entry[p + 4 + align4(length):]
    new = code[:UNITY_HEADER] + dxbc
    return entry[:p] + struct.pack("<I", len(new)) + new + b"\0" * (align4(len(new)) - len(new)) + rest


def make_shader(t, spec, src_dir):
    """원본 셰이더 타입 트리를 spec대로 바꾼다. 첫 패스만 남기고, 규칙이 있는 단계는 일치하는 변형만 LXIUM 코드로"""
    src = open(os.path.join(src_dir, spec["hlsl"]), "rb").read()
    pf = t["m_ParsedForm"]
    names = pf["m_KeywordNames"]
    p = pf["m_SubShaders"][0]["m_Passes"][0]

    if t["platforms"] != [D3D11]:
        raise SystemExit(f"{spec['source']}: 플랫폼 {t['platforms']} (D3D11만 예상)")
    blob = bytes(t["compressedBlob"])
    old = b""
    for off, clen, dlen in zip(t["offsets"][0], t["compressedLengths"][0], t["decompressedLengths"][0]):
        old += lz4.block.decompress(blob[off:off + clen], uncompressed_size=dlen)
    old = split_entries(old)

    entries, moved = [], {}

    def take(index, data=None):
        # 같은 항목(파라미터 블롭 등)을 여러 변형이 함께 쓰면 한 번만 옮김
        if data is None and index in moved:
            return moved[index]
        entries.append(old[index] if data is None else data)
        if data is None:
            moved[index] = len(entries) - 1
        return len(entries) - 1

    for stage in ("vertex", "fragment"):
        prog = p["progVertex" if stage == "vertex" else "progFragment"]
        rules = spec.get(stage, [])
        used = [0] * len(rules)
        for i, lst in enumerate(prog["m_PlayerSubPrograms"]):
            params = prog["m_ParameterBlobIndices"][i]
            keep_s, keep_p = [], []
            for j, s in enumerate(lst):
                if not rules:
                    keep_p.append(take(params[j]))
                    s["m_BlobIndex"] = take(s["m_BlobIndex"])
                    keep_s.append(s)
                    continue
                kw = {names[x] for x in s["m_KeywordIndices"]}
                for r, (kind, keywords, defines, target, entry) in enumerate(rules):
                    if s["m_GpuProgramType"] != kind or kw != keywords:
                        continue
                    dxbc = compile_hlsl(src, spec["hlsl"], entry, target, defines)
                    keep_p.append(take(params[j]))
                    s["m_BlobIndex"] = take(None, replace_code(old[s["m_BlobIndex"]], dxbc))
                    keep_s.append(s)
                    used[r] += 1
                    break
            prog["m_PlayerSubPrograms"][i] = keep_s
            prog["m_ParameterBlobIndices"][i] = keep_p
        # 게임이 업데이트돼 변형 구성이 바뀌었으면 멈춤
        if any(u != 1 for u in used):
            raise SystemExit(f"{spec['source']} {stage}: 변형 일치 수 {used}")

    raw = join_entries(entries)
    packed = lz4.block.compress(raw, mode="high_compression", store_size=False)
    pf["m_Name"] = spec["name"]
    pf["m_SubShaders"] = [pf["m_SubShaders"][0]]
    pf["m_SubShaders"][0]["m_Passes"] = [p]
    pf["m_FallbackName"] = ""
    pf["m_CustomEditorName"] = ""
    pf["m_Dependencies"] = []
    t["m_Name"] = spec["name"]
    t["offsets"], t["compressedLengths"], t["decompressedLengths"] = [[0]], [[len(packed)]], [[len(raw)]]
    t["compressedBlob"] = list(packed)
    t["m_Dependencies"] = []
    t["m_NonModifiableTextures"] = []
    guid = hashlib.md5(spec["name"].encode()).digest()
    t["m_AssetGUID"] = {f"data[{i}]": u32(guid, 4 * i) for i in range(4)}
    return len(entries)


def main():
    game, out = sys.argv[1], sys.argv[2]
    data_dir = os.path.join(game, "Sephiria_Data")
    src_dir = os.path.dirname(os.path.abspath(__file__))

    trees = []
    shader_hash = None
    for spec in SHADERS:
        env = UnityPy.load(os.path.join(data_dir, spec["file"]))
        shader_hash = shader_hash or next(t.old_type_hash for t in env.file.types if t.class_id == 48)
        obj = next(o for o in env.objects if o.type.name == "Shader" and o.read_typetree()["m_ParsedForm"]["m_Name"] == spec["source"])
        if env.file.unity_version != GAME_VERSION:
            raise SystemExit(f"게임 버전 {env.file.unity_version} != {GAME_VERSION}: 타입 트리 확인 후 GAME_VERSION 갱신")
        t = obj.read_typetree()
        n = make_shader(t, spec, src_dir)
        trees.append((spec["name"], t))
        print(f"{spec['name']}: {n} entries")

    bundle = UnityPy.load(TEMPLATE).file
    sf = next(f for f in bundle.files.values() if hasattr(f, "objects"))
    version = UnityVersion.from_str(GAME_VERSION)
    bundle.version_engine = GAME_VERSION
    sf.unity_version = GAME_VERSION
    # 게임 버전의 타입 트리. Shader는 게임 파일의 타입 해시를 써서 엔진이 같은 구조로 보고 바로 읽게 하고,
    # AssetBundle은 해시를 비워 엔진이 타입 트리로 읽게 함
    for typ in sf.types:
        typ.node = Tpk.get_typetree_node(typ.class_id, version)
        typ.old_type_hash = shader_hash if typ.class_id == 48 else bytes(16)
        # TPK 노드에는 없는 값: 배열 표시(1)와 참조 타입 해시(0). 에디터 번들과 같은 규칙
        for node in typ.node.traverse():
            node.m_TypeFlags = 1 if node.m_Type == "Array" else 0
            node.m_RefTypeHash = 0

    slots = [o for o in sf.objects.values() if o.type.name == "Shader"]
    ab = next(o for o in sf.objects.values() if o.type.name == "AssetBundle")
    # ponytail: 그릇 번들의 셰이더 자리(2개)만큼만 넣음. 더 필요하면 ObjectReader 복제 추가
    if len(trees) > len(slots):
        raise SystemExit(f"셰이더 {len(trees)}개 > 그릇 자리 {len(slots)}개")
    kept = []
    for (name, t), slot in zip(trees, slots):
        slot.save_typetree(t)
        kept.append((name, slot))

    a = ab.read_typetree()
    a["m_Name"] = a["m_AssetBundleName"] = "lxium_sephiria"
    a["m_PreloadTable"] = [{"m_FileID": 0, "m_PathID": s.path_id} for _, s in kept]
    a["m_Container"] = [[name.lower(), {"preloadIndex": i, "preloadSize": 1, "asset": {"m_FileID": 0, "m_PathID": s.path_id}}]
                        for i, (name, s) in enumerate(kept)]
    a["m_MainAsset"] = {"preloadIndex": 0, "preloadSize": 0, "asset": {"m_FileID": 0, "m_PathID": 0}}
    a["m_Dependencies"] = []
    a["m_SceneHashes"] = []
    ab.save_typetree(a)

    keep_ids = {ab.path_id} | {s.path_id for _, s in kept}
    for pid in [pid for pid in sf.objects if pid not in keep_ids]:
        del sf.objects[pid]
    sf.script_types, sf.ref_types, sf.externals = [], [], []
    # 다른 번들과 같은 내부 파일 이름이면 Unity가 이미 로드된 번들로 보고 거부하므로 고유하게
    new_cab = "CAB-" + hashlib.md5(b"LXIUM_sephiria").hexdigest()
    sf.name = new_cab
    bundle.files = {new_cab: sf}
    data = bundle.save(packer="lz4")
    with open(out, "wb") as f:
        f.write(data)
    print(f"{out}: {len(data)} bytes")


if __name__ == "__main__":
    main()

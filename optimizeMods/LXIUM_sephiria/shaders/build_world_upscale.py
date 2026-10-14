"""Build the authored Direct3D11 shader bundle without Unity Editor.

Usage: python build_world_upscale.py <game-directory> [output-bundle]
Requires UnityPy, lz4, Windows d3dcompiler_47.dll. No game files are modified.
Only serialization/binding metadata is reused; every GPU program is authored HLSL.
"""
import copy
import ctypes
import hashlib
import re
import struct
import sys
from pathlib import Path

import lz4.block
import UnityPy
from UnityPy.helpers.Tpk import get_typetree_node

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "LXIUM_mahosaba" / "shaders"))
from build_firelite import align4, blob_bytes, compile_hlsl, join_entries, replace_code, split_entries, u32

NAME = "LXIUM/WorldUpscale"
D3D11 = 4


def code_payload(entry):
    pos = 28
    for _ in range(u32(entry, 24)):
        pos += 4 + align4(u32(entry, pos))
    size = u32(entry, pos)
    return entry[pos + 4 + 38:pos + 4 + size]


def disassemble(code):
    function = ctypes.WinDLL("d3dcompiler_47.dll").D3DDisassemble
    function.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_uint, ctypes.c_char_p,
                         ctypes.POINTER(ctypes.c_void_p)]
    result = ctypes.c_void_p()
    status = function(code, len(code), 0, None, ctypes.byref(result))
    if status != 0:
        raise RuntimeError(f"D3DDisassemble failed: {status:#x}")
    return blob_bytes(result).decode(errors="replace")


def shader_entries(tree):
    offset, packed, size = tree["offsets"][0][0], tree["compressedLengths"][0][0], tree["decompressedLengths"][0][0]
    raw = lz4.block.decompress(bytes(tree["compressedBlob"][offset:offset + packed]), uncompressed_size=size)
    return split_entries(raw)


def main():
    if len(sys.argv) not in (2, 3):
        raise SystemExit(__doc__)
    project = Path(__file__).resolve().parents[1]
    game = Path(sys.argv[1])
    output = Path(sys.argv[2]) if len(sys.argv) == 3 else project / "res" / "lxium_sephiria.bundle"
    source = project / "shaders" / "WorldUpscale.hlsl"
    vs, ps = (compile_hlsl(source.read_bytes(), entry, profile) for entry, profile in (("vert", "vs_4_0"), ("frag", "ps_4_0")))
    vertex_asm, pixel_asm = disassemble(vs), disassemble(ps)
    assert "SV_VertexID" in vertex_asm and "vertex_id" in vertex_asm
    assert re.search(r"_FlipY;\s*// Offset:\s*2128", vertex_asm)
    assert re.search(r"_Intensity;\s*// Offset:\s*16", pixel_asm)
    assert re.search(r"_BlitTexture\s+texture.*\bt2\b", pixel_asm)
    assert re.search(r"UnityPerMaterial\s+cbuffer.*\bcb1\b", pixel_asm)
    idle = re.search(r"ge (r\d+\.x), l\(0\.000000\), cb1\[1\]\.x\s+if_nz \1(?P<body>.*?)endif", pixel_asm, re.S)
    assert idle and "ret" in idle["body"]
    assert len(re.findall(r"^\s*ld ", pixel_asm[:idle.start()], re.M)) == 4
    assert len(re.findall(r"^\s*ld ", pixel_asm[idle.end():], re.M)) == 4

    env = UnityPy.load(str(game / "Sephiria_Data" / "globalgamemanagers.assets"))
    sf = env.file
    if str(sf.unity_version) != "6000.3.21f1":
        raise SystemExit("Reverify shader metadata for this Unity version before rebuilding.")
    shader = next(o for o in sf.objects.values() if o.type.name == "Shader"
                  and o.read_typetree()["m_ParsedForm"]["m_Name"] == "Shader Graphs/FS-Distortion")
    tree = shader.read_typetree()
    if tree["platforms"] != [D3D11]:
        raise SystemExit("Expected the verified Direct3D11-only shader template.")
    parsed = tree["m_ParsedForm"]
    render_pass = parsed["m_SubShaders"][0]["m_Passes"][0]
    names = dict(render_pass["m_NameIndices"])
    fragment_parameters = render_pass["progFragment"]["m_CommonParameters"]
    texture = next(t for t in fragment_parameters["m_TextureParams"] if t["m_NameIndex"] == names["_BlitTexture"])
    material = next(c for c in fragment_parameters["m_ConstantBuffers"] if c["m_NameIndex"] == names["UnityPerMaterial"])
    intensity = next(v for v in material["m_VectorParams"] if v["m_NameIndex"] == names["_Intensity"])
    assert texture["m_Index"] == 2 and intensity["m_Index"] == 16 and material["m_Size"] == 144
    old = shader_entries(tree)
    entries = []
    for key, kind, compiled in (("progVertex", 15, vs), ("progFragment", 17, ps)):
        program = render_pass[key]
        original = next((i, j, sub) for i, group in enumerate(program["m_PlayerSubPrograms"])
                        for j, sub in enumerate(group) if sub["m_GpuProgramType"] == kind)
        group, index, sub = original
        parameter = old[program["m_ParameterBlobIndices"][group][index]]
        entries.extend((parameter, replace_code(old[sub["m_BlobIndex"]], compiled)))
        kept = copy.deepcopy(sub)
        kept["m_BlobIndex"] = len(entries) - 1
        program["m_PlayerSubPrograms"] = [[kept] if i == group else [] for i in range(len(program["m_PlayerSubPrograms"]))]
        program["m_ParameterBlobIndices"] = [[len(entries) - 2] if i == group else [] for i in range(len(program["m_ParameterBlobIndices"]))]
        program["m_SubPrograms"] = []
    state = render_pass["m_State"]
    for key in ("srcBlend", "srcBlendAlpha"):
        state["rtBlend0"][key] = {"val": 1.0, "name": "<noninit>"}
    for key in ("destBlend", "destBlendAlpha"):
        state["rtBlend0"][key] = {"val": 10.0, "name": "<noninit>"}
    state["zTest"], state["zWrite"], state["culling"] = ({"val": v, "name": "<noninit>"} for v in (8.0, 0.0, 0.0))
    state["m_Name"] = "WorldComposite"
    parsed["m_SubShaders"] = [parsed["m_SubShaders"][0]]
    parsed["m_SubShaders"][0]["m_Passes"] = [render_pass]
    parsed["m_PropInfo"]["m_Props"] = [p for p in parsed["m_PropInfo"]["m_Props"] if p["m_Name"] == "_Intensity"]
    parsed["m_PropInfo"]["m_Props"][0]["m_DefValue[0]"] = 0.0
    parsed["m_Name"] = tree["m_Name"] = NAME
    parsed["m_FallbackName"] = parsed["m_CustomEditorName"] = ""
    raw = join_entries(entries)
    assert split_entries(raw) == entries
    packed = lz4.block.compress(raw, mode="high_compression", store_size=False)
    tree["offsets"], tree["compressedLengths"], tree["decompressedLengths"] = [[0]], [[len(packed)]], [[len(raw)]]
    tree["compressedBlob"] = list(packed)
    tree["m_Dependencies"] = []
    guid = hashlib.md5(NAME.encode()).digest()
    tree["m_AssetGUID"] = {f"data[{i}]": u32(guid, i * 4) for i in range(4)}
    shader.save_typetree(tree)

    # Reuse the existing bundle container; serialize its AssetBundle record for the target game version.
    template = UnityPy.load(str(project.parent / "LXIUM_mahosaba" / "res" / "lxium_mahosaba.bundle"))
    bundle = template.file
    ab = next(o for o in template.objects if o.type.name == "AssetBundle")
    sf.flags = ab.assets_file.flags
    asset_bundle = ab.read_typetree()
    shader_type = shader.serialized_type
    bundle_type = copy.copy(ab.serialized_type)
    bundle_type.node = get_typetree_node(ab.type, sf.version)
    sf.types = [shader_type, bundle_type]
    shader.type_id = 0
    ab.assets_file, ab.serialized_type, ab.type_id, ab.path_id = sf, bundle_type, 1, 1
    sf.objects = {shader.path_id: shader, ab.path_id: ab}
    pointer = {"m_FileID": 0, "m_PathID": shader.path_id}
    asset_bundle["m_Name"] = asset_bundle["m_AssetBundleName"] = "lxium_sephiria"
    asset_bundle["m_PreloadTable"] = [pointer]
    asset_bundle["m_Container"] = [[NAME.lower(), {"preloadIndex": 0, "preloadSize": 1, "asset": pointer}]]
    asset_bundle["m_MainAsset"] = {"preloadIndex": 0, "preloadSize": 0, "asset": {"m_FileID": 0, "m_PathID": 0}}
    asset_bundle["m_Dependencies"], asset_bundle["m_SceneHashes"] = [], []
    ab.save_typetree(asset_bundle)
    sf.script_types, sf.ref_types, sf.externals = [], [], []
    sf.name = "CAB-" + hashlib.md5(NAME.encode()).hexdigest()
    sf.parent = bundle
    bundle.version_engine = str(sf.unity_version)
    bundle.files = {sf.name: sf}
    data = bundle.save(packer="lz4")
    reloaded = UnityPy.load(data)
    assert sorted(o.type.name for o in reloaded.objects) == ["AssetBundle", "Shader"]
    actual = next(o for o in reloaded.objects if o.type.name == "Shader").read_typetree()
    assert str(next(iter(reloaded.file.files.values())).unity_version) == "6000.3.21f1"
    assert actual["m_ParsedForm"]["m_Name"] == NAME
    assert len(actual["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"]) == 1
    actual_state = actual["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"][0]["m_State"]
    assert [actual_state["rtBlend0"][k]["val"] for k in ("srcBlend", "destBlend", "srcBlendAlpha", "destBlendAlpha")] == [1, 10, 1, 10]
    assert [actual_state[k]["val"] for k in ("zTest", "zWrite", "culling")] == [8, 0, 0]
    actual_bundle = next(o for o in reloaded.objects if o.type.name == "AssetBundle").read_typetree()
    assert actual_bundle["m_Container"][0][0] == NAME.lower() and not actual_bundle["m_Dependencies"]
    actual_entries = shader_entries(actual)
    assert actual_entries == entries
    assert code_payload(actual_entries[1]) == vs and code_payload(actual_entries[3]) == ps
    assert not any(b"DXBC" in p for p in (actual_entries[0], actual_entries[2]))
    assert all(code_payload(entry) not in (vs, ps) for entry in old if b"DXBC" in entry)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(data)
    print(f"{output.name}: {len(data)} bytes, SHA256 {hashlib.sha256(data).hexdigest()}")
    print("Verified shader bindings, premultiplied blend, serialization and authored-only GPU programs. Runtime load remains untested.")


if __name__ == "__main__":
    main()

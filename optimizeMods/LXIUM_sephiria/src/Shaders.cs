using UnityEngine;

namespace LXIUM_sephiria
{
    /// <summary>
    /// DLL에 넣은 셰이더 번들(shaders/build_shaders.py로 만든 res/lxium_sephiria.bundle)을 읽는다.
    /// 번들에는 D3D11 코드만 있으므로 다른 그래픽 API에서는 isSupported가 거짓이 되고 쓰지 않는다.
    /// </summary>
    public static class Shaders
    {
        public static Shader TexelUpscale { get; private set; }

        public static void Load()
        {
            using (var s = typeof(Shaders).Assembly.GetManifestResourceStream("lxium_sephiria.bundle"))
            {
                if (s == null) { Debug.LogWarning("[LXIUM] 셰이더 번들 없음"); return; }
                var bytes = new byte[s.Length];
                s.Read(bytes, 0, bytes.Length);
                var bundle = AssetBundle.LoadFromMemory(bytes);
                if (bundle == null) { Debug.LogWarning("[LXIUM] 셰이더 번들 로드 실패"); return; }
                foreach (var sh in bundle.LoadAllAssets<Shader>())
                {
                    sh.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    Debug.Log($"[LXIUM] shader '{sh.name}' supported={sh.isSupported} passes={sh.passCount}");
                    if (sh.name == "LXIUM/TexelUpscale" && sh.isSupported) TexelUpscale = sh;
                }
                bundle.Unload(false);
            }
        }
    }
}

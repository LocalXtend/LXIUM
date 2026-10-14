using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LXIUM.Sephiria
{
    internal static class ShaderCompatibility
    {
        static readonly HashSet<string> fixedNoDepth;
        static readonly string unity, module;
        static ShaderCompatibility()
        {
            using var stream = typeof(ShaderCompatibility).Assembly.GetManifestResourceStream("depth-safe-shaders.json");
            using var reader = new StreamReader(stream);
            var data = JObject.Parse(reader.ReadToEnd());
            unity = (string)data["unityVersion"]; module = (string)data["gameModule"];
            fixedNoDepth = new HashSet<string>(data["shaderNames"].ToObject<string[]>(), StringComparer.Ordinal);
        }
        public static bool SupportedVersion => Application.unityVersion == unity && typeof(HorayModBase).Module.ModuleVersionId.ToString() == module;
        public static bool NoDepth(Material material, bool propertyBlock)
        {
            if (material == null || material.shader == null) return false;
            string name = material.shader.name;
            if (fixedNoDepth.Contains(name)) return true;
            return !propertyBlock && (name == "Universal Render Pipeline/2D/Sprite-Lit-Default" ||
                name == "Universal Render Pipeline/2D/Sprite-Unlit-Default") && material.HasProperty("_ZWrite") && material.GetFloat("_ZWrite") == 0;
        }
    }
}

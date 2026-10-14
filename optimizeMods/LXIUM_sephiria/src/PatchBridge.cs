using System;
using System.IO;
using System.Reflection;

namespace LXIUM.Sephiria
{
    internal static class PatchBridge
    {
        const string Id = "LocalXtend.LXIUM.sephiria";
        static object harmony;
        static Type methodType;
        static MethodInfo patch;
        public static bool Prefix(MethodBase target, MethodInfo hook) => Add(target, hook, "prefix");
        public static bool Postfix(MethodBase target, MethodInfo hook) => Add(target, hook, "postfix");
        public static bool Finalizer(MethodBase target, MethodInfo hook) => Add(target, hook, "finalizer");
        static bool Add(MethodBase target, MethodInfo hook, string kind)
        {
            if (target == null || hook == null) return false;
            try
            {
                if (harmony == null) Initialize();
                object annotation = Activator.CreateInstance(methodType, new object[] { hook });
                var parameters = patch.GetParameters();
                var args = new object[parameters.Length];
                args[0] = target;
                for (int i = 1; i < args.Length; i++) if (parameters[i].Name == kind) args[i] = annotation;
                patch.Invoke(harmony, args);
                return true;
            }
            catch (Exception e) { Log.Warn("patch " + target.Name + ": " + e.GetBaseException().Message); return false; }
        }
        static void Initialize()
        {
            Assembly found = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetType("HarmonyLib.Harmony", false) != null) { found = assembly; break; }
            found ??= Assembly.LoadFrom(Path.Combine(Host.ModDirectory, "Libs", "0Harmony.dll"));
            Type type = found.GetType("HarmonyLib.Harmony", true);
            methodType = found.GetType("HarmonyLib.HarmonyMethod", true);
            harmony = Activator.CreateInstance(type, new object[] { Id });
            patch = type.GetMethod("Patch", BindingFlags.Public | BindingFlags.Instance);
            Log.Info("Harmony reused/loaded " + found.FullName);
        }
        public static void Dispose()
        {
            if (harmony == null) return;
            try
            {
                harmony.GetType().GetMethod("UnpatchSelf").Invoke(harmony, null);
                Pipeline.ResetHooks(); WorldRender.ResetHooks(); LightCache.ResetHooks();
            }
            catch (Exception e) { Log.Warn("unpatch: " + e.GetBaseException().Message); }
            harmony = null;
        }
    }
}

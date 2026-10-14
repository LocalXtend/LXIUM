using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LXIUM.Sephiria
{
    public sealed class Entry : HorayModBase
    {
        protected override void OnModLoaded()
        {
            if (Host.Instance != null) return;
            var owner = new GameObject("LXIUM Sephiria");
            UnityEngine.Object.DontDestroyOnLoad(owner);
            owner.AddComponent<Host>();
        }
        protected override void OnModUnloaded()
        {
            var host = Host.Instance;
            if (host != null) { host.Detach(); UnityEngine.Object.Destroy(host.gameObject); }
        }
    }
    internal static class Log
    {
        public static void Info(string text) => Debug.Log("[LXIUM Sephiria] " + text);
        public static void Warn(string text) => Debug.LogWarning("[LXIUM Sephiria] " + text);
    }
    public sealed class Host : MonoBehaviour
    {
        public static Host Instance;
        public static readonly string ModDirectory = Path.GetDirectoryName(typeof(Entry).Assembly.Location);
        internal Config Config;
        internal string ConfigPath => Path.Combine(ModDirectory, "config.json");
        internal string Status;
        bool stopped;
        float nextRefresh;
        void Awake()
        {
            Instance = this;
            try { Config = Config.Load(ConfigPath); }
            catch (Exception e)
            {
                Log.Warn("config retained as backup: " + e.Message);
                string backup = ConfigPath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
                try { File.Copy(ConfigPath, backup, false); }
                catch (Exception backupError) { Log.Warn("config backup failed: " + backupError.Message); enabled = false; return; }
                Config = new Config();
            }
            SaveConfig();
            SceneManager.activeSceneChanged += SceneChanged;
            Pipeline.Set(Config.Options);
            WorldRender.Initialize();
            Log.Info("loaded; F10 comparison, press F10 again to cancel");
        }
        void SceneChanged(Scene oldScene, Scene newScene)
        {
            RenderLab.Cancel(this, "scene changed");
            WorldRender.Refresh();
            nextRefresh = 0;
        }
        void Update()
        {
            try
            {
                if (Keyboard.current?.f10Key.wasPressedThisFrame == true && Config.F10Comparison)
                {
                    if (RenderLab.Running) RenderLab.Cancel(this, "F10 cancelled");
                    else RenderLab.Start(this);
                }
                if (Time.unscaledTime >= nextRefresh)
                {
                    nextRefresh = Time.unscaledTime + 1f;
                    Pipeline.Ensure();
                    WorldRender.Refresh();
                }
                Pipeline.Tick();
            }
            catch (Exception e)
            {
                Log.Warn("renderer stopped: " + e.GetBaseException().Message);
                Shutdown();
                enabled = false;
            }
        }
        internal void SaveConfig()
        {
            try { Config.Save(ConfigPath); }
            catch (Exception e) { Log.Warn("config save failed: " + e.Message); }
        }
        void OnGUI()
        {
            if (string.IsNullOrEmpty(Status)) return;
            GUI.Box(new Rect(12, 12, Math.Min(Screen.width - 24, 800), 88), Status);
        }
        void OnApplicationFocus(bool focused) { if (!focused) RenderLab.Cancel(this, "focus lost"); }
        void OnDisable() => Shutdown();
        void OnDestroy() { Shutdown(); if (Instance == this) Instance = null; }
        internal void Detach() { Shutdown(); if (Instance == this) Instance = null; }
        void Shutdown()
        {
            if (stopped) return;
            stopped = true;
            RenderLab.Cancel(this, "controller disabled");
            SceneManager.activeSceneChanged -= SceneChanged;
            LightCache.Dispose();
            SpriteBatch.Dispose();
            WorldRender.Dispose();
            PatchBridge.Dispose();
            Pipeline.Dispose();
        }
    }
}

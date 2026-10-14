using System;
using System.IO;
using UnityEngine;

namespace LXIUM_sephiria
{
    // Player.log는 실행할 때마다 덮어써지므로 모드 폴더의 LXIUM_diag.log에도 이어 쓴다.
    static class Log
    {
        public static readonly string ModDir = Path.GetDirectoryName(typeof(Log).Assembly.Location);
        static readonly string LogPath = Path.Combine(ModDir, "LXIUM_diag.log");
        const long MaxLogBytes = 5 * 1024 * 1024;

        static Log()
        {
            try { if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes) File.Delete(LogPath); }
            catch (IOException) { }
        }

        public static void Write(string line)
        {
            Debug.Log(line);
            try { File.AppendAllText(LogPath, DateTime.Now.ToString("MM-dd HH:mm:ss ") + line + "\n"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

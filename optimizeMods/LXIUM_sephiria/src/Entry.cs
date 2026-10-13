using UnityEngine;

namespace LXIUM_sephiria
{
    // 게임 AddOnLoader가 metadata.json의 entryClass로 찾아 생성하는 진입점.
    // HorayModBase는 MonoBehaviour가 아니므로 매 프레임 동작은 별도 GameObject에 둔다.
    // AddOnLoader.LoadAll은 타이틀에서 게임에 들어갈 때마다 다시 불려 Entry를 새로 만들지만
    // (언로드 없이, 같은 어셈블리 재사용) 호스트는 DontDestroyOnLoad로 남아 있으므로 한 번만 만든다.
    public sealed class Entry : HorayModBase
    {
        static GameObject _host;

        protected override void OnModLoaded()
        {
            if (_host != null) return;
            var config = Config.Load();
            _host = new GameObject("LXIUM_sephiria");
            Object.DontDestroyOnLoad(_host);
            if (config.diagnostics) _host.AddComponent<Diagnostics>();
            if (config.sendMoneyScanFix) _host.AddComponent<SendMoneyScanFix>();
        }

        protected override void OnModUnloaded()
        {
            if (_host != null) Object.Destroy(_host);
        }
    }
}

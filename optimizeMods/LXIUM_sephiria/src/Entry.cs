using UnityEngine;

namespace LXIUM_sephiria
{
    // 게임 AddOnLoader가 metadata.json의 entryClass로 찾아 생성하는 진입점.
    // HorayModBase는 MonoBehaviour가 아니므로 매 프레임 동작은 별도 GameObject에 둔다.
    public sealed class Entry : HorayModBase
    {
        GameObject _host;

        protected override void OnModLoaded()
        {
            _host = new GameObject("LXIUM_sephiria");
            Object.DontDestroyOnLoad(_host);
            _host.AddComponent<Diagnostics>();
        }

        protected override void OnModUnloaded()
        {
            if (_host != null) Object.Destroy(_host);
        }
    }
}

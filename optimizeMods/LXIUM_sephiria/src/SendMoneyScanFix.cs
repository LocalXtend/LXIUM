using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LXIUM_sephiria
{
    // SendMoney 1.0.1 호환 패치.
    // SendMoney는 채팅 입력창에 연결될 때까지 0.5초마다 FindObjectOfType<UI_ChatInput>()으로 씬 전체를 훑는다.
    // 닫힌 채팅창은 비활성(UIBase.Close → SetActive(false))이라 이 검색으로 찾을 수 없어,
    // 채팅을 열기 전까지 게임 월드에서 0.5초마다 20ms 이상 멈춘다 (docs/ai/sephiria.md 8장).
    // 채팅창이 있고 비활성인 동안에만 SendMoney의 scanTimer를 0으로 되돌려 실패할 검색을 막는다.
    // 채팅창이 열리면 손대지 않으므로 SendMoney가 원래 방식대로 찾아 연결한다.
    sealed class SendMoneyScanFix : MonoBehaviour
    {
        const string Tag = "[LXIUM] ";
        const string ModName = "SendMoney Mod";
        const string ModVersion = "1.0.1";
        static readonly object Zero = 0f;

        Func<object> _runner;
        FieldInfo _scanTimer;

        void Start()
        {
            // Start는 AddOnLoader.LoadAll이 끝난 뒤 불리므로 SendMoney가 있다면 이미 로드돼 있다
            var mod = AddOnLoader.LoadedMods.FirstOrDefault(m => m.Metadata.modName == ModName);
            if (mod == null)
            {
                enabled = false;
                return;
            }
            if (mod.Metadata.modVersion != ModVersion)
            {
                Debug.Log(Tag + "SendMoney " + mod.Metadata.modVersion + ": 패치 대상 버전(" + ModVersion + ")이 아니라 적용하지 않음");
                enabled = false;
                return;
            }

            var runnerType = mod.Assembly.GetType("SendMoneyMod.SendMoneyModRunner");
            var instance = runnerType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
            _scanTimer = runnerType?.GetField("scanTimer", BindingFlags.Instance | BindingFlags.NonPublic);
            if (instance == null || _scanTimer == null || _scanTimer.FieldType != typeof(float))
            {
                Debug.Log(Tag + "SendMoney " + ModVersion + ": 예상한 구조가 아니라 적용하지 않음");
                enabled = false;
                return;
            }
            _runner = (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), instance);
            Debug.Log(Tag + "SendMoney " + ModVersion + " 채팅창 검색 패치 적용");
        }

        void Update()
        {
            var ui = UIManager.Instance;
            var chat = ui != null ? ui.GetElement<UI_ChatInput>() : null;
            // 채팅창 정보를 얻지 못하면 SendMoney 원래 동작을 그대로 둔다
            if (chat == null || chat.gameObject.activeInHierarchy) return;

            var runner = _runner();
            if (runner != null) _scanTimer.SetValue(runner, Zero);
        }
    }
}

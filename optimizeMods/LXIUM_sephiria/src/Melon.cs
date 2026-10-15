using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace LXIUM_sephiria
{
    /// <summary>
    /// MelonLoader 진입점 (애드온 변형 melonloader, Mods/LXIUM_sephiria.dll). 게임 시작 직후 불려서 타이틀부터 계측·자동 측정이 된다.
    /// 설정·로그는 UserData/LXIUM_sephiria/. MelonInfo·MelonGame 어셈블리 특성은 csproj가 ver.txt 버전으로 만든다.
    /// 게임 내장 로더로만 쓸 때는 이 형식을 아무도 건드리지 않아 MelonLoader.dll이 없어도 된다.
    /// </summary>
    public sealed class MelonEntry : MelonMod
    {
        // 첫 장면이 뜬 뒤라 GameObject·URP 에셋을 바로 쓸 수 있음
        public override void OnLateInitializeMelon() =>
            Core.Start(Path.Combine(MelonEnvironment.UserDataDirectory, "LXIUM_sephiria"), "MelonLoader");

        public override void OnDeinitializeMelon() => Core.Stop();
    }
}

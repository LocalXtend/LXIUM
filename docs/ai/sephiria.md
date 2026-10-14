# Sephiria fresh baseline / official mod integration

Date: 2026-10-14. This investigation uses current installed artifacts only; previous Sephiria docs, source and Git history were not consulted. No optimization implementation, release or runtime benchmark is included. Paired human document: `../human/sephiria.md`; rendering: `sephiria-render.md`.

## Identity and authority

Paths below are relative to the selected game installation, not development-machine paths.

| Artifact | Identity |
|---|---|
| `Sephiria_Data/Managed/Assembly-CSharp.dll` | SHA256 `c57a0daeab8e8d0af7066a344133eec4c57d8f303fd9e25da9410fbfc4cf1510`; 6,272,512 bytes |
| Module | MVID `ccf97024-28bd-48f1-9666-c768c2bd4b2b` |
| REA classification | Unity Mono, CIL, AnyCPU; metadata complete |
| Host adapter observation | Win32_VideoController reports `Intel Iris Xe Graphics` and a virtual display adapter; actual game adapter selection unobserved |

REA metadata/CIL is static shipped-artifact evidence; metadata tokens are build-local, never native addresses. ILSpy 11.1.0.9782 reconstruction is supplementary analyst interpretation, not original source. All inspection was execution-free. No game files, installed mods, saves or settings were modified. Full local evidence bundles and reconstructed types are analysis artifacts outside the repository; they contain installation paths and target code and must not be published as repository source.

Initial unfiltered member inspection exceeded REA's 67,108,864-byte representation budget. Exact `type_names` selections succeeded. Selected-type coverage does not establish whole-game call coverage. Some #US literals retain readable UTF-16 with terminal-flag diagnostics; runtime acceptance is unknown.

## Official loader contract

- `AddOnLoader.LoadAll` uses `Application.dataPath`'s parent and immediate `AddOns` child directories. Directory enumeration is not sorted.
- Each directory requires `metadata.json`. `AddOnsDisabled` is not traversed by this code path.
- Official `AddOnMetadata` fields: `modName`, `modVersion` (default `1.0.0`), `modAuthor`, `dllFile`, `entryClass`.
- Explicit `dllFile`: load one file with `Assembly.LoadFrom`. Omitted: select first top-level `*.dll`, warn when several exist. No automatic load of every DLL or recursive `Libs` traversal.
- Explicit `entryClass`, otherwise first nonabstract `HorayModBase` subclass. `Activator.CreateInstance` requires parameterless construction.
- `HorayModBase` is a plain class, not MonoBehaviour: `LoadMod(metadata)` -> `OnModLoaded()`; `UnloadMod()` -> `OnModUnloaded()`.
- No explicit dependency DAG/version negotiation/duplicate-mod identity validation or AssemblyResolve hook in this loader.
- `FindModType` calls `Assembly.GetTypes` outside its own exception guard. A reflection type-load failure can abort the remaining `LoadAll` traversal; the enclosing GameDataLoader catch still continues game-data initialization.
- A package should specify DLL/class explicitly; external wrapper fields `modFile`, `modClass`, `modLibs` are not the official schema.

## Bootstrap order and lifetime

`GameDataLoader.Awake` interpretation, supported by CIL calls/anchors:

1. `InternalMod.Register()` then `AddOnLoader.LoadAll()` at IL_0024.
2. Database initialization, including mod database events.
3. `HorayModAPI.NotifyAllModDatabasesReady()`.
4. Spatial hash / physics setup and SpriteFx, ParticleFx and related pool initialization.
5. Localization-ready notification.

Do not equate database-ready with pool/camera/render-resource-ready. Bind the controller after checking actual scene objects; handle scene replacement independently of mod load.

The inspected `GameDataLoader.OnDestroy` does not call `AddOnLoader.UnloadAll` or `HorayModAPI.ClearAll`. This is bounded to that method, not proof no shutdown path calls them. `ClearAll` itself omits clearing `OnLoadPropDatabase`. Own unsubscribe, component destruction, GPU-resource release and duplicate-controller prevention are required for any future implementation.

## HorayModAPI scope

Complete selected type: 67 methods, 20 events.

| Family | Events |
|---|---|
| Databases | `OnLoadMiracleDatabase`, `OnLoadWeaponDatabase`, `OnLoadStatusDatabase`, `OnLoadItemDatabase`, `OnLoadKeywordDatabase`, `OnLoadPassiveDatabase`, `OnLoadCostumeDatabase`, `OnLoadPropDatabase` |
| Readiness | `OnAllDatabasesReady`, `OnLocalizationReady` |
| Game/session | `OnStartGameServerside`, `OnStartGameClientside`, `OnStartSessionServerside`, `OnStartSessionClientside` |
| Floors | `OnFloorAllocatedServerside`, `OnFloorAllocatedClientside` |
| Gameplay | `OnPlayerEvade`, `OnGetDebuff`, `GridInventoryStartPermission`, `GridInventoryEndPermission` |

Network methods: `RegisterNetworkPrefab`, `RegisterNetworkPrefabs`, `UnregisterNetworkPrefab`.

No Update/LateUpdate, camera, render-pass or shader replacement callback exists within this fully selected API type. This does not exclude Unity rendering APIs or other engine extension points. Use official loader entry with an owned MonoBehaviour and Unity rendering extension; narrow patches only where needed.

Action notifications isolate exceptions per subscriber. `OnGetDebuff` invokes the last subscriber only; on failure it removes that handler and returns the original. It is not a composition chain. Optimization should not subscribe to unrelated gameplay hooks.

## Installed environment: static observations

| Folder | metadata version | Selected DLL/class |
|---|---|---|
| CustomSkin | 1.1.9 | `HarmonyBase.dll` / wrapper, then `CustomSkin.dll` |
| MiraItemMod | 1.1.5 | `MiraModBase.dll` / `MiraModBase.Core`, then `MiraItemMod.dll` |
| SendMoney Mod | 1.0.1 | `SendMoney.dll` / `SendMoneyMod` |
| Sephiria QoL | 1.55.0 | `SephiriaQoL-1.55.0.dll` / `SephiriaQoL.QoLMod` |

Additional QoL `SephiriaQoL.dll` is not selected by the official loader because metadata specifies the versioned DLL. The wrappers implement their own library loading.

Harmony file-version observations (do not confuse file version with an authenticated API compatibility guarantee):

- CustomSkin and Mira: `2.10.2.0`, identical SHA256 `9846eb9edf64a5e2473342c75b8c5ab37293fb4c27418bc7a5b9a156aa0d7214`.
- QoL: `2.4.2.0`, SHA256 `7b9e756306fa3d7620e02a857c8927a6ab04973f9bd8a77d3866700a6deac55c`.
- QoL checks loaded assemblies for `HarmonyLib.Harmony` and skips its own preload when found. Unsorted directory traversal and a common AppDomain make load order a compatibility variable. Actual loaded assembly identity and applied patches are unknown.
- Mira wrapper references `HorayModBase.OnModOptionChanged/Loaded`; those methods are absent from the selected current game type. This suggests a different expected API contract; reachability/failure was not observed.
- QoL RuntimeHost contains SpawnScaler, GameSpeed, ScreenZoom, ZoomKeys, WarningColor, PickupFade, BossHpText and HudLog. Freeze their settings in baseline comparisons.

## Implementation boundaries

Scope for the next implementation: official mod entry, scene-owned renderer controller, read-only diagnostics, matching shader/bundle resources, teardown, paired docs and addon package contract. Do not restore deleted old Sephiria code or borrow old behavior without a new justification.

SDK 10 is the repository build-tool requirement, not proof Unity Mono accepts a net10.0 mod. Choose the target framework from the installed engine/reference assemblies before building. Read the current launcher addon contract and copy the sample when creating the actual package; that contract is unrelated to these freshly investigated game internals.

Prefer existing Unity extension points before adding Harmony. If a patch is necessary, validate type/member signature and supported artifact identity and leave unsupported features inactive. Do not bundle an arbitrary second Harmony version. Do not alter gameplay clocks, spawn counts, combat, network authority or save format to inflate rendering benchmarks.

First benchmark: current installed mod environment, LXIUM off/on with settings fixed. A separate clean-game baseline requires an isolated test installation, not moving the user's installed mods.

## REA ledger

IDs below identify full parent evidence unless stated otherwise. Full evidence is retained locally, not embedded into these public docs.

| Subject | Evidence ID |
|---|---|
| Assembly identity/classification | `ev_b2ff80fd487641fed1eca32253a16cab08bd0d6b79d5bd38673c81845959da36` |
| HorayModAPI selected CIL | `ev_1a00d8ebb5f8fd59140211a576934bc7bfd3425e4ab1e00ef50a3c964b3b2684` |
| AddOnLoader, HorayModBase, GameDataLoader | `ev_345182156db9118bb2e276b86e2e6f0ba5d9ff1f1198bfd80fcbed34f34d80a0` |
| AddOnMetadata | `ev_e0a3b65ef9bc629093fcdee441904a49d42798c98839c21fe55ce81f31b0db3d` |
| CustomSkin wrapper | `ev_ec4631e147df8b46a87ed4559a1ed1243be4eff07163e4dab12f0433417bf827` |
| Mira wrapper | `ev_053bbeb5cafe0373a24f324b545ca1d6895ffa3ca49b870ce88a675a2d47ad3f` |
| QoL entry and HarmonyBoot | `ev_a5dee43f5c1ebd3a4448018848deaee7aca9cb63f741b02e8d13bc280cdb92f7` |

Build-local coordinates: GameDataLoader `Awake 0x06000207`, `OnDestroy 0x06000208`; AddOnLoader `LoadAll 0x06002a73`, `UnloadAll 0x06002a74`, `FindModType 0x06002a79`, `CreateAndLoad 0x06002a7a`; HorayModBase `LoadMod 0x06002ac7`, `OnModLoaded 0x06002ac8`; HorayModAPI `GetDebuff 0x06002abf`, `ClearAll 0x06002ac3`.

## Coverage closure

Answered statically: official loader and API contract, bootstrap order, selected installed wrappers, likely integration constraints. Partially answered: lifetime outside the selected shutdown method, actual mod compatibility. Unresolved: runtime load success, DLL binding, applied patches, chosen graphics API/adapter, CPU/GPU timings and FPS. No runtime execution or deployment was performed.

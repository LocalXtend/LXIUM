# Sephiria rendering: fresh evidence and prototype decisions

Baseline investigation: 2026-10-14. Implementation update: 2026-10-15, experimental mod1.0.0. Current installed artifacts only; no old Sephiria docs/source/history used for the baseline. Paired human document: `../human/sephiria-render.md`; official mod integration: `sephiria.md`. The original analysis/design sections below remain the baseline; the following implementation section supersedes their not-implemented status. No FPS or visual acceptance claim exists.

## Implemented experiment and F10 contract (2026-10-15)

Source `optimizeMods/LXIUM_sephiria/`, SDK10 builds netstandard2.1 against installed Mono managed assemblies. Official HorayModBase entry; `none` addon variant contains `AddOns/LXIUM_sephiria/{metadata.json,LXIUM_sephiria.dll,Libs/0Harmony.dll,Libs/Harmony.LICENSE}`. Game SteamAppId2436940 verified from local appmanifest. No original game assembly replacement. Harmony bridge reuses an existing loaded Harmony first, otherwise loads bundled2.4.2/net472; duplicate Harmony versions are not forced.

Files: Entry owns persistent Host/scene/focus hooks and config; Pipeline clones quality asset, each renderer data and renderer feature before modifying light scale/dynamic batching; LightCache intercepts internal DrawLight2DPass.Render; WorldRender intercepts DrawRenderer2DPass.Render; RenderLab handles timing/restoration; FrameStats contains pure statistics; Config atomically persists Options and LastComparison. Modified shared game materials, camera masks, Canvas roles, gameplay clocks and animation callbacks are not required.

### Feature behavior

- LightResolution: private verified Renderer2DData.m_LightRenderTextureScale on owned copies only, default0.5. Renderer resource creation reads it each frame. Main world/native UI dimensions are not changed.
- IdlePasses: prefix on FullScreenPassRendererFeature.AddRenderPasses for main GameCamera only. Exact shader names/pass0/injection600, non-stereo/no targetRT/default viewport/renderScale1, and graphics format without alpha. Original D3D57 Distortion has finiteRGB identity at `_Intensity==0`;58 Glitch at `_GlitchIntensity==0 && _ColorIntensity==0`. Both overwrite alpha1. `_ColorTint` is not a GPU binding; extra Distortion Glitch/Color settings are unused in this compiled shader. Alpha-bearing targets remain native. No epsilon intensity approximation, feature list reordering or blanket postprocess disable.
- StaticLights: only main Base GameCamera and canonicalHidden/Light2D, no cookies/normals/shadows/volumetrics. Full exact snapshot includes camera matrices/descriptor, layer batch/light settings, shape data, materialCRC and texture descriptors. Two consecutive matching frames permit original render followed by GPU CopyTexture into persistent RTHandles. Cache ready is set during graph execution; hits import fresh TextureHandles and replace that frame's lightTextures batch slot before skipping the original light draw. Never retain transient TextureHandles across frames. All changes invalidate same-frame; no approximate float equality. Limit16 entries/64MiB. Snapshot boxing/CRC/copy/VRAM cost is measured, not assumed free.
- SpriteBatch: toggles UniversalRenderPipelineAsset.supportsDynamicBatching on the owned clone, with restoration. Original SRP Batcher remains enabled. Unity preserves sorting/material eligibility; actual draw/batch reductions depend on its shader path, so no fabricated compatible-batch hit counter.
- WorldUpscale: per original sorting-layer batch, not whole-camera renderScale. Main Base GameCamera/non-stereo/no targetRT/intermediate explicit single-sample2D targets. Eligibility scans current active renderer/Canvas/mask state; world Canvas ranges, any active mask, non-default shader/material queue/property-block, non-simple SpriteRenderer, depth-write material and volumetric batches retain native rendering. Visible compatible sprites must exist; empty batches do not count as scaled coverage. Nested SortingGroup sortAtRoot is respected. A camera-wide guard rejects unverified depth writers, including other native batches/Canvas materials, because cleared scratch depth cannot preserve earlier native depth. `res/depth-safe-shaders.json` contains only names of verified fixedZWrite0 shaders; conditional defaultSprite _ZWrite is checked separately and unknown property blocks rejected. Exact baseline Unity/gameModule identity and only validated renderer features are required for that guard. Default SpriteLit/Unlit alpha batches draw into a transparent RGBA16F low-res scratch plus matching depth, then native resource handles are restored and a custom composite pass executes immediately at the original batch boundary. Composite attachment is AccessFlags.ReadWrite so previous native layers survive premult-over blending. Prefix/postfix/finalizer restore frame resources on failure; unknown layouts disable this experiment. UI native sorting/masking/occlusion is retained by keeping its batch native. No new camera/Canvas layer mutation.
- DedicatedShader: authored shaderLXIUM/WorldUpscale, source `shaders/WorldUpscale.hlsl`; rebuilt by `shaders/build_world_upscale.py`. Exact-sized source via _BlitTexturet2, _Intensityb1offset16, _FlipYb0offset2128, VS/PS SM4.0. Premult-over One/OneMinusSrcAlpha, ZWriteOff/ZTestAlways/CullOff. Bilinear interpolation then contrast-adaptive sharpening in straight-color space, alpha unchanged/re-premultiplied and local HDR extrema clamp. Strength0 in world-only section, default0.35 in shader section; strength0 returns before four sharpening-neighbor loads (DXBC assertion verifies branch/return). Direct3D11 only; unsupported API/bundle/shader means native world fallback. Serialization/binding metadata retained but all GPU code is newly compiled HLSL; no extracted game GPU code in shipped bundle. Final bundle3048bytes SHA25628af4a07f0a33998f45547ecb47dccc3b623d8a3e2e3f62e6434acb2d166becc; reserialization/identities/bindings/all-program replacement asserts pass.

### F10 sequence

`F10Comparison=true` default. Press F10 in a ready GameCamera scene, again to cancel. Ten sections:1original,2light-only,3idle-pass-only,4static-light-only,5native-compatible-batch-only,6world-upscale with sharpness0,7world-upscale+authored sharpener,8all,9savedOptions,10original repeat. Only the declared switch(es) are enabled in isolated sections; all use the configured numeric scales. Options are copied, not overwritten by preset cycling.

SectionSeconds default12/clamp10..15; settle2 +sample(SectionSeconds-2), measured by monotonic realtime differences. Frame intervals are measured wall time, not average instantaneous FPS. Low1/0.1 are reciprocal mean of slowest ceil(N*fraction) frame intervals, high1 fastest counterpart; p50/p95/p99 are nearest-rank frame-time percentiles. No-sample result is explicit. Screenshots/JSON writes occur outside measurement; OnGUI status overhead exists equally during samples.

Capture: CPUmain/render and GPU FrameTimingManager readings with timestamp deduplication; unsupported/empty counter values null, never pretend GPU0ms. ProfilerRecorder DrawCalls/Batches/Triangles/GCAllocatedInFrame, GC collections and cacheHits/Misses/Fallbacks, worldScaled/Native, passSkipped. Start/end loaded scene sprites, enabled Light2D counts and camera orthographicSize are sampled outside the measurement loop; counts are not visible draw counts. EffectiveFeatures explains guards. Compare each section to initial baseline and report final baselineDriftPercent; these live-game comparisons are observational, not a deterministic replay. Scene, main camera, screen size, quality change or focus loss aborts. VSync/FPS temporarily0/-1, restored with configured options on finish/cancel/failure/disable. Forced process death cannot execute restoration.

Every completed section persists config.json.LastComparison, outcome running/completed/aborted/failed. Finish writes comparisons/<UTCtimestamp>/results.json; optional PNGs in same directory. Temp+File.Replace with config.json.bak preserves previous file; invalid JSON is backed up before default rewrite. Unknown root config fields survive roundtrip. Saved Options are not automatically chosen from benchmark winners. Public package excludes config/logs/results/screenshots.

### Validation and unresolved runtime

Release package and meaningful .NET10 checks passed: time-weighted FPS, tail definitions/extreme outlier, empty/nonfinite samples, all ten independent presets, bounded config, atomic save/backup/unknown-field/nullable counter roundtrip, seven exact cache snapshot invalidations and hook-state reset after unpatch. Shader compiler/binding/roundtrip/no-original-GPU-code assertions passed. Build0warnings/0errors against shipped references. `tests/LXIUM_sephiria.Tests.csproj` executes these checks; it does not emulate Unity rendering. Official unload synchronously detaches/shuts down Host before deferred Destroy, so same-frame unload/reload is not suppressed; successful Unpatch resets all install flags, while A/B pipeline disposal keeps still-installed hooks.

Installed new AddOns/LXIUM_sephiria files for manual testing; other mods/settings are not overwritten by deployment. Runtime smoke attempt used -batchmode/-nographics, selected process terminated by its own handle at a bounded deadline. REA Windows PTY capture reports capability_unavailable/unsupported_provider (descendant cleanup not implemented), so shell-owned process fallback was used; not a REA controlled-replay Evidence. First attempt lacked SteamAppId; second provided2436940 but headless game startup reported own SwitchManager.GetDestinySwitch / ControlsChangeHandler null exceptions and did not reach an LXIUM load log. Thus loader/runtime GPU/F10 acceptance is unverified, and no screenshot/FPS gain is claimed. The game may perform its normal startup/cloud synchronization during this smoke run; deployment itself does not edit saves.

Additional REA hook CIL evidence: `ev_bbd127122f6943ad491d16d265021e2fb4c0250711ebdf7b734920a99f49d461`. Original effect disassembly and the cache-hook evidence bundle remain local analysis artifacts. Future runtime acceptance must include actual alpha-free identity-pass path, supported authored shader output, native UI ordering, changing lights, resource teardown, mod interactions, cache CPU overhead and nonzero eligible work counters.

## Evidence model

- REA: execution-free managed metadata, method-body anchors, calls and hashes. ILSpy 11.1.0.9782: supplementary reconstruction/control-flow interpretation.
- Serialized assets: UnityPy 1.25.4 + TypeTreeGeneratorAPI 0.0.10. REA does not advertise Unity serialized-asset analysis. These supplemental records are not falsely attributed to a REA Evidence ID.
- Generated MonoBehaviour `m_Enabled` alignment required correction to include the 0x4000 alignment flag. Each decoded script pointer was asserted equal to UnityPy's independently parsed native header; complete object-byte consumption was required. Key pipeline/camera/light/feature records passed these checks.
- Explicit parse gaps: `globalgamemanagers.assets:5905` complex registry (`read_str out of bounds`); `resources.assets:28092,31279` null script pointers. Do not claim complete asset/script semantics.
- Serialized objects/settings do not prove runtime instances, visibility, applied mod changes, GPU workload or active passes.
- Raw interpreted assets, selected reconstructed types and full REA bundles are local analysis artifacts outside the repository; public docs contain identities/findings only.

## Artifact identities

Paths relative to `Sephiria_Data/`.

| File | SHA256 |
|---|---|
| `Managed/Assembly-CSharp.dll` | `c57a0daeab8e8d0af7066a344133eec4c57d8f303fd9e25da9410fbfc4cf1510` |
| `Managed/Unity.RenderPipelines.Universal.2D.Runtime.dll` | `8b9ea091525f3fe061844dc1ade150a938afa60c5774374d3f2192fa7c56b5cf` |
| `Managed/Unity.RenderPipelines.Universal.Runtime.dll` | `4bbd77b3b0637eb4736a12a3d1d25addeb0c84ae59dd364f9935ef5e8f8da07b` |
| `globalgamemanagers` | `8924c809dcf3b93a1169b0dc2017c8543aee17ba03e41b675f7a3205d73549a5` |
| `globalgamemanagers.assets` | `170889448d0ade8f9ed29096de0d170d3764070b61f743d69c8b9f8936661161` |
| `level2` | `22b8eb3c7f8499083152a14a266ddfbde54455ac473fbc7fbe00f673111c933a` |
| `sharedassets0.assets` | `73135788932f461054da76fde2bba1793c64748e77e9dfa3028a1ab6da47ee36` |
| `sharedassets1.assets` | `88e55aa4df8145d55144c1faf116427d72363d7bdbd99675c44db9344d2dabd1` |

Assembly-CSharp MVID `ccf97024-28bd-48f1-9666-c768c2bd4b2b`; URP2D MVID `51db1ed8-8921-4e9e-997e-ac6f59a9b3ef`; URP MVID `94408d01-17af-48d8-8a64-5d63d9ac8883`. Tokens below are build-local.

## Active serialized pipeline selection

All nine inspected serialized files report Unity `6000.3.21f1`.

GraphicsSettings default pipeline is null, but `globalgamemanagers:12` QualitySettings quality0 selects `globalgamemanagers.assets:5903` UniversalRenderPipelineAsset. Its rendererDataList selects `:5904` Renderer2DData. A null GraphicsSettings pointer is not evidence this game uses the built-in renderer.

| Setting | Serialized value |
|---|---|
| URP m_RenderScale | 1.0 |
| m_SupportsHDR / m_HDRColorBufferPrecision | 1 / 0 |
| m_MSAA | 1 sample, disabled |
| m_UseSRPBatcher / m_SupportsDynamicBatching | 1 / 0 |
| m_RequireDepthTexture / m_RequireOpaqueTexture | 0 / 0 |
| m_UpscalingFilter / m_FsrOverrideSharpness | 0 (Automatic) / 0 |
| Renderer2D m_LightRenderTextureScale | 1.0 |
| m_UseDepthStencilBuffer | 1 |
| m_UseCameraSortingLayersTexture | 0 |
| m_MaxLightRenderTextureCount / m_MaxShadowRenderTextureCount | 16 / 1 |
| m_LayerMask | 0xffffffff |
| m_TransparencySortMode / axis | 3 (CustomAxis) / (0,1,0) |

Four configured light blend styles: Multiply, Additive, Multiply with Mask, Additive with Mask; mask-channel values 0/0/1/1. All 953 packaged Light2D records use blendStyleIndex0. Zero enable normal maps; only two GlobalLight scene instances carry enabled shadow/volume flags. This does not mean 953 simultaneous lights, four active blend buffers, zero runtime normal lights, or two useful shadow passes.

`Renderer2DData.lightRenderTextureScale` is internal/read-only and its backing field is serialized private. A future runtime override must validate that contract and renderer recreation/resource sizing; cloning a pipeline asset alone does not clone its referenced renderer data/features/materials. Own clones of every modified shared object; never mutate the game's persistent/shared original blindly.

## Shipped Renderer2D path

Selected managed types: Renderer2D and Renderer2DData. Reconstruction corroborated with REA CIL:

1. Initialize layer batches and render resources; cache/cull visible Light2D state.
2. Normal, shadow and light passes over required layer batches.
3. Sprite/render batches and required volumetric light/shadow passes; optional sorting-layer texture/depth copy.
4. Postprocess; custom passes at the corresponding 2D injection point.
5. Upscale/final-post/final-blit according to camera conditions.
6. Screen-space overlay UI for the appropriate final camera/output mode.

The renderer contains RenderGraph extension points; a future custom pass must implement `RecordRenderGraph` against the shipped API, not rely on a legacy Execute-only assumption. Internal OnRecordRenderGraph is a discovery anchor, not a recommendation to patch the whole method.

`CreateLightTextures` sizes light resources from camera dimensions and the separate light scale. At fixed camera dimensions, 1.0 -> 0.5 reduces each light buffer's pixel count to approximately 25%, not total frame cost to 25%.

## Cameras and UI composition

`level2:6639` Main Camera: orthographic size5.625, depth-1, cullingMask0xffffffff, HDRtrue, MSAAfalse, dynamicResolutionfalse, no targetRT. `level2:21790` UniversalAdditionalCameraData: Base0, no camera stack, postprocess=true, AA0, rendererIndex-1. Inspect runtime state before changing it; QoL can alter zoom.

World-space canvases target Main Camera. Root GameObjects all use layer5 (UI), but sorting and Canvas roles differ:

| level2 Canvas ID | Root | Mode | Sorting layer/order |
|---|---|---|---|
| 12426 | [UI] Alt World | 2 WorldSpace | Default / 11 |
| 12430 | [UI] World | 2 WorldSpace | Default / 10 |
| 12443 | [UI] Ground World | 2 WorldSpace | Ground / 6 |
| 12433 | [UI] HUD | 0 Overlay | UI / 0 |
| 12434 | [UI] DynamicHUD | 0 Overlay | UI / -2 |
| 12439 | [UI] Worldmap | 0 Overlay | UI / -3 |
| 12440 | [UI] InteractableHUD | 0 Overlay | UI / -2 |
| 12442 | [UI] Panels | 0 Overlay | UI / 2 |
| 12446 | [UI] System | 0 Overlay | UI / 10 |

HUD/panels can remain native-resolution with scaled world rendering. Damage text is `UI_DamageParticle` -> TextMeshProUGUI -> `EUIObjectPoolingParent.World`, so renderScale alone does not satisfy native-resolution TEXT. Route eligible world labels explicitly with original projection, sorting, occlusion, input/raycast and masks preserved. Ground World must not be promoted blindly to a topmost overlay. A blanket layer5 mask split is insufficient.

Artifact/item UI: UI_ItemIcon uses Image, subIconImages, rarity/favorite decorations, tooltip selection and materials; stone tablets have colorblind material variants. UI_ItemBoxTabArtifacts holds TMP combo text and Image. Legacy LocalizedText uses UnityEngine.UI.Text; TMP is not the only text path. Preserve HDR UI colors, clipping/stencil, accessibility materials and original interaction behavior.

## Characters, animation, batching and CPU

- TopdownActorRenderingMetadata contains body SortingGroup/SpriteRenderer, Animator2D_Basic, size and Shadow; body height participates in hitbox-layer calculations. Do not change gameplay metadata for render savings.
- Animator2D_Basic.Update advances a Timer and executes transitions. SetFrame sets a Sprite and invokes frame events and sounds. Do not throttle the whole method or skip frames without preserving all events and original clock semantics.
- SimpleAnimator2D uses activeInstances and CulledUpdate for simpler animation. CellBasedCulling.Update copies activeInstances to updateBuffer and linearly checks the orthographic bounds with margins x+5/y+4. Existing culling is not a spatial-cell query in this method.
- SimpleAnimator2D has OnceAndDisable/OnceAndDestroy lifecycle paths. Offscreen visual culling must not inadvertently keep objects alive or alter pool-return timing.
- AnimatorManager already caches baked animation keyframes. SpriteFx/ParticleFx/UI already have pools. Replacing these wholesale is not an evidence-based optimization.
- SpriteFx follows parents, resets animation/material state on pool reuse and can drive LightAnimator. ParticleFx follows parents and checks ParticleSystem.IsAlive before despawn. These lifecycle behaviors must survive any render-output batching.
- Shadow.LateUpdate computes alpha from body height and writes SpriteRenderer.color each frame. CommonOverlaySprite.Update likewise writes its color from ratio. Candidate: suppress unchanged renderer values, with existing callbacks/other side effects preserved. Cost remains unmeasured.
- TopdownObject changes object layers by height/corpse/hidden state; TopdownRigidbody has physics, spatial hash and networking. Layer-mask changes can change physics/gameplay, not only rendering. Retain original layers or isolate render-only objects.
- Animator2D_SpriteRenderer.SetTransparentOnMultiplayer uses MyFX/PartyMemberFX and sharedMaterial/color, including alpha0. Alpha0 does not establish skipped GPU draws. Renderer-only suppression is a candidate after checking masks, lights, callbacks and pool reset.

## Shader, particle, weather and postprocess observations

Packaged shader examples (`sharedassets0.assets`):

| ID | Shader | Serialized rendering behavior |
|---|---|---|
| 24463 | Shader Graphs/Sprite-Lit-SpriteWithFog | Transparent; SrcAlpha/OneMinusSrcAlpha; ZWrite0 |
| 24464 | Shader Graphs/Sprite-Premultiply | Transparent; One/OneMinusSrcAlpha; ZWrite0 |
| 24471 | Shader Graphs/Sprite-Additive | Transparent; SrcAlpha/One; ZWrite0 |
| 24492 | Universal Render Pipeline/2D/Sprite-Lit-Default | Transparent; SrcAlpha/OneMinusSrcAlpha; _ZWrite default0 |

Materials generally inherit queue (-1), not opaque queues. Also present: HDR emission/outline/hit-flash shaders, sprite stencil/ghost shaders, UI premultiplied HDR, TMP bitmap/distance-field variants and VFX warning shaders. Shader names/pass state do not reveal instruction cost. No compiled shader instruction analysis was performed.

GameCamera binds floor backgrounds, parallax layers, day-light multipliers, rain/fog/fire-sparks and volume profiles; its coroutine may replace the profile after floor completion. UpdateWeather toggles rain/snow with floor/session conditions. DayCycle.LateUpdate changes global Light2D, background stars/fog/camera color and fog material based on time/custom color/weather. LightAnimator and WavingLight2D make additional light state dynamic. Static-light caching cannot ignore these paths.

Blob shadows are separate SpriteRenderer objects (`Shadow`), not 3D shadow maps or the same thing as Light2D ShadowCaster rendering. Do not present 3D cascade/shadow-map settings as a fix for them.

Active scene-volume chain: `level2:21114` Volume -> `sharedassets1.assets:573` VolumeProfile -> Bloom556: enabled, intensity1.8, threshold1.03, scatter0.65. Default global profile Bloom5909 intensity0 does not prove the game scene has Bloom disabled. Runtime profile switching remains relevant.

Fullscreen features in `globalgamemanagers.assets:5904` renderer order:

| Feature ID | Material -> shader | Settings |
|---|---|---|
| 5895 Glitch | material4 Glitch -> shader58 Shader Graphs/FS-Glitch | active1, injection600, fetchColorBuffer1, requirements0, passIndex0, bindDepthStencil0 |
| 5917 FullScreenPassRendererFeature | material3 Distortion -> shader57 Shader Graphs/FS-Distortion | same |

These shaders expose pass0 DrawProcedural, pass1 Blit, RGB blend One/Zero and ZWrite0; materials inherit queues. Both saved `_Intensity=0`; Glitch `_GlitchIntensity=0`. Distortion also saves `_ColorIntensity=53.22`, `_GlitchIntensity=86.4`, so a single zero parameter must not be treated as proof of identity output. RadialDistortion/Glitch game scripts modify material values during effects.

FullScreenPassRendererFeature.AddRenderPasses does not test effect intensity. Selected FullScreenRenderPass.RecordRenderGraph has color-copy and procedural fullscreen render paths for fetchColorBuffer. Static activation gives a plausible bandwidth/pass hypothesis, not an observed cost. Prove the shader's identity state and runtime transitions before bypassing any feature; preserve original pass order. Do not remove fetchColorBuffer while a shader needs its source input.

## Proposed renderer structure (not implemented)

1. Keep native HUD/GUI/icons and eligible world text. Preserve world-label occlusion/sorting separately.
2. Independently choose world and light-buffer dimensions. First prototype changes only light scale at 1.0/0.75/0.5, with world unchanged.
3. Keep dynamic actors, combat markers and effect semantics at the real simulation rate. Reuse only proven-static background/light contributions.
4. Submit compatible contiguous decorative sprites in stable original order, with exact texture/atlas/UV, blend, stencil, clipping, material and sorting identity. Unknown/mod-created materials retain the original renderer.
5. Composite necessary lighting/weather/emission/post effects once where semantics allow, then native-resolution UI. Add a custom RenderGraph pass only when existing matched engine paths cannot accomplish the measured change.

### Static-light/background reuse

Candidate: cache stationary spatial light contributions per compatible sorting-layer target set/blend operation; render moving/animated/cookie lights separately. Global day/weather tint changes are tracked separately only when the original blending admits that decomposition. Changing light order, overlap mode, target layers, cookie, bounds, normals, shadow caster, camera region, weather or floor invalidates affected cache entries. Invalidate rather than extrapolate unsupported normal-map or shadow behavior. The current zero-normal packaged subset is a useful starting scope, not a universal runtime assumption.

Orthographic static backgrounds could reuse a world-space tile/expanded region and draw newly uncovered/dirty regions during scrolling. Parallax layers require separate transforms and floor-change invalidation. This is temporal reuse of proven-static content, not intermediate frame generation.

### Shader candidates

- Simple SpriteLit variant for confirmed decorative materials: preserve original color/HDR/fog/emission/alpha and masks; omit only features proven unused by that material. SRP-compatible material data and per-instance state must match batching strategy.
- Low-resolution light accumulation/composite: preserve Light2D shapes/cookies, target sorting layers and overlap semantics; fall back for unsupported dynamic shadows/normals.
- Effect composite: handle necessary glow/weather/distortion work with reduced buffers or bounded screen regions only after verifying source sampling, pass dependencies and alpha order. Do not fuse two passes merely because both are fullscreen.

New Unity shaders require compatible compiled resources/AssetBundles and platform variants; writing an HLSL file or calling Shader.Find is not proof a shipped player can compile/load a new shader. Build against the matching Unity6000.3.21f1 editor/API and verify the actually used graphics API; presence of a D3D12 folder does not prove it is active. Do not redistribute extracted game assets as project source.

## Requested techniques: decisions

| Technique | Decision and prerequisite |
|---|---|
| Spatial Resolution + sharpening | Suitable experiment after UI split. Compare matched built-in FSR1 against bilinear/pixel-aware output before writing another upscaler. Presence of EASU/FinalPost shaders and Renderer2D FSR path establishes a candidate, not runtime support/quality. Do not claim sharpening restores missing detail. |
| Temporal Frame Reprojection | Separate late experiment. Need scene/object motion, disocclusion/reactive treatment for spawned/despawned particles and sprite changes, camera shake, UI separation and actual presentation pacing. STP/TAA/FSR1, cached frames and true generated present frames are different. No fixed +20-30 FPS claim. |
| Opaque conversion | Only verified fully opaque terrain candidates. Transparent sprite edges, premult/additive effects, masks, outlines and Y ordering make global conversion incorrect. Alpha clip/depth writes may add cost or break sorting. |
| Fast render submission | SRP Batcher already enabled. Measure draw submission/state changes; batch only contiguous compatible content without reordering transparent sprites. More CPU dynamic batching is not automatically faster. |
| Async entity work | Only snapshot-based pure calculations, such as candidate visibility for verified visual-only objects. Unity object/Transform/renderer access, combat, physics, pool lifecycle and Mirror networking remain in their existing execution context. Measure snapshot and synchronization overhead. |

## First prototype / measurement contract

The next implementation's smallest complete scope: official loader entry + owned diagnostic controller + independent light-scale experiment + teardown/package checks. It does not claim to be the final custom renderer. No optimizer code or configuration was installed in this analysis.

1. Record engine/assembly identities, actual SystemInfo GPU/API, current quality/pipeline/cameras, loaded mod/Harmony identities, camera zoom, screen dimensions, refresh, VSync and target FPS.
2. Measure baseline CPU/GPU frame timings (only supported counters; unsupported GPU timing is unknown), p95/p99, GC allocation and actual render-pass/draw counts. FPS alone cannot distinguish CPU/GPU/cap/thermal limits.
3. Hold current user mods/settings constant; compare LXIUM off/on. If needed use an isolated clean installation for a separate baseline, never move the user's mod folders implicitly.
4. Test light scales individually before world scaling. Recreate only owned resources when size changes; avoid per-frame allocations and GPU/CPU ReadPixels or blocking readbacks.
5. Verify effect-identity bypass individually. Compare captures including Glitch/Distortion activation/deactivation; retain unknown effects.
6. Only measured CPU pressure justifies spatial candidate indexing/unchanged-value suppression; measured GPU pressure justifies world/effect buffer changes. Preserve simulation and presentation separately.

Representative scenarios: title/water reflection; normal and crowded combat; damage labels; rain/snow/fog/daylight and cutscene changes; boss warnings/emission; artifact inventory, tooltips, masks and colorblind mode; cooperative players/FX opacity/custom skins. Also check resize, fullscreen transitions, scene reload, disable/unload and original-camera/settings restoration. Warm assets before timing, repeat comparable captures and document power/thermal state.

Target 120 real FPS means approximately8.33ms per real frame under the declared workload; report generated presentation frames separately if that feature is ever built. No current timing or FPS acceptance result exists. Average gain alone does not justify regressions in p95/p99, text readability, warnings or interaction.

## REA ledger and useful coordinates

| Selected subject | Parent Evidence |
|---|---|
| GameCamera, TopdownActorRenderingMetadata, Animator2D_SpriteRenderer, Rain/Snow/FogControl, SpriteFx, ParticleFx, UI_DamageParticle | `ev_5bf5ade7f82151c9df86a0ee90994250fc3a9d56b481c199f4bb19ffe86ab792` |
| CellBasedCulling, SimpleAnimator2D, Animator2D_Basic, Shadow, TopdownObject, DayCycle, DisplayManager, UIRoot, CommonOverlaySprite, DamageFeedback, UI_ItemIcon/Tooltip/BoxTabArtifacts, TopdownRigidbody | `ev_091db2b0d092f557f22d706f4655be21b495ed0362784c8df00ddc3aabf26ccd` |
| Renderer2D, Renderer2DData | `ev_a3fc997e58a655337e7c76797176ce455ecc13bfe70bebd1577ce432348611e0` |
| FullScreenPassRendererFeature | `ev_b00faf75e81aa79dcaafecc6167036bfe80d7ff2a5836a16dc0253df71817239` |
| FullScreenRenderPass (REA metadata name is unqualified) | `ev_0436bc5f7c5292f4c457da547d9fde78ef25a938f2410d6a0e99b6e746365fbe` |
| Floor coroutine, RadialDistortion, Glitch | `ev_74767dcb60ec7071e77089ed69325305b327201338815216198c03b7cf8198ef` |

Main selected method pages had no partial/truncated bodies. Some URP #US strings carry terminal-flag diagnostics. Unqualified nested-type names are REA parser coordinates; do not turn them into CLR type lookup strings.

Useful Assembly-CSharp tokens: Animator2D_Basic.Update0x0600019b, SetFrame0x060001b3; SimpleAnimator2D.CulledUpdate0x060001f3; Animator2D_SpriteRenderer.SetTransparentOnMultiplayer0x060001c7; GameCamera.UpdateWeather0x06000951; Shadow.LateUpdate0x06001474; FogControl.Update0x06001df3; ParticleFx.Update0x06002237; DayCycle.LateUpdate0x06002771; CellBasedCulling.Update0x06002bcc; UI_DamageParticle.Update0x06003f87; CommonOverlaySprite.Update0x06004e2b.

URP2D: Renderer2D.CreateLightTextures0x06000256, RecordCustomRenderGraphPasses0x0600025d, OnRecordRenderGraph0x0600025e, OnMainRendering0x06000262, OnAfterRendering0x06000263. URP: FullScreenPassRendererFeature.AddRenderPasses0x0600069a.

Selected types not covered by a ledger row (for example some UI/pool subclasses and additional weather helpers) were supplementary ILSpy interpretation with the same assembly identity, not falsely promoted to REA-selected CIL coverage.

## External primary references

- [Unity6000.3 URP asset reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/universalrp-asset.html): game renderScale vs native UI, FSR/sharpness and SRP batching semantics. This general description does not exempt world-space canvases from the inspected camera path.
- [Unity6000.3 Renderer2D data reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/2DRendererData-overview.html): separate light textures, depth/stencil and blend-style settings. Installed serialized values take priority over documentation defaults.

## Checklist closure

Statically answered: mod entry, engine/pipeline identity, character/FX/animation map, native HUD vs world-text distinction, artifact UI/material behavior, light/weather/blob-shadow and fullscreen/postprocess configuration, shipped render extension points. Partially answered: shader algorithms, actual active instances and mod interaction. Unresolved: GPU shader instructions, native Unity render-thread/driver work, current runtime graph, timings, artifact-free output and120FPS feasibility. Required runtime questions remain explicitly open; static analysis is not a complete performance investigation.

# Rbx2Source — Agent Guide

## Build

```bash
# Requisite: clone sibling dependency first
git clone https://github.com/MaximumADHD/Roblox-File-Format.git ../Roblox-File-Format

nuget restore
msbuild Rbx2Source.sln
```

Configurations: `Debug|AnyCPU` (default), `Release|AnyCPU`, `Debug|x64`, `Release|x64`.

## Key facts

- **Windows-only** — WinForms, Registry (`HKCU\SOFTWARE\Rbx2Source`), calls `studiomdl.exe`/`vtfcmd.exe`.
- **No tests, no linters, no typecheck** — zero test projects. Manual testing only.
- **Costura.Fody** embeds all managed DLLs into a single `Rbx2Source.exe`.
- **Sibling dependency** `../Roblox-File-Format/` is required at build time (excluded from this repo's `.gitignore`).
- **API key** — Roblox Open Cloud key with `legacy-asset:manage` required at runtime.
- **MODEL_SCALE = 10** — all geometry scaled by 10× (Roblox studs → Source units).
- **Invariant culture** — use `ToInvariantString()` for all float/double formatting; system locale must never affect SMD/VMT/QC output.
- **Read-only output** — `FileUtility.WriteFile()` sets `FileAttributes.ReadOnly` on all generated files.
- **Settings** stored in Registry, defaulted via `App.config` `userSettings` on first run (`InitializedV3` key).
- **Assembler selection** — `R6CharacterAssembler` vs `R15CharacterAssembler` chosen at runtime by `PlayerAvatarType`.
- **R6 base limbs** — `CharacterBase.rbxm` stores limbs as plain `Part`, not `MeshPart`. `FindFirstChild<MeshPart>()` returns null for these — check `BasePart` + type-test instead.
- **Dynamic heads** — The Roblox avatar API provides `DynamicHead` assets containing a `MeshPart` that replaces the R15 `CharacterBase.rbxm` head. Two failure modes exist:
  - **Geometry**: Dynamic head meshes are `AssetTypeId=4` (`Mesh`) but use a COREMESH version (→Draco or newer) that `Mesh.LoadGeometry_Binary` can't parse. `RobloxFile.Open()` only accepts `<roblox`/`<roblox!` headers, so the `OpenAsModel` fallback is useless for `.mesh` content. `Mesh.BakePart` falls back to `Default.mesh` (embedded ASCII `.mesh` template) when it detects a `Head` limb via `CharacterAssembler.GetLimb()`.
  - **Texture**: `GetAvatarFace` checks `characterAssets/ASSEMBLY/Head` first (MeshPart `TextureID`, or SpecialMesh FileMesh `TextureId` — any non-empty baked texture is the face), then falls back to scanning all `characterAssets` descendants for `MeshPart` with `TextureID` where `GetLimb()` returns `BodyPart.Head`. The face texture is composited at layer 1 over the head color layer. SpecialMesh heads tagged `NoFace` with an empty `TextureId` return `null` (faceless — no face layer composited); callers null-guard it. A FileMesh/MeshPart head with an empty texture is also faceless (returns `null`) — it must NOT fall through to the `Images/face.png` default.
  - **Head mode** — Advanced tab `HeadMode` setting (`Default`/`Faceless`/`Headless`), Registry key `HeadMode`. `CharacterAssembler.HeadModeSetting` is the user override; `EffectiveHeadMode` is resolved per-avatar in `Assemble()` after `AppendCharacterAssets`. In `Default` mode the head asset name is auto-inspected (stamped onto the head SpecialMesh as `StringValue` `AssetName` from `AssetInfo.Name`): names containing `headless`/`nohead`/`no head` → Headless, `faceless`/`noface`/`no face` → Faceless. Headless destroys the `Head` limb part(s) in `AssembleModel` (R6 + R15, both char + collision models), so the SMD has no Head bone/material; Faceless only suppresses the face layer. `GetAvatarFace` returns `null` for both overrides.
  - Head material (`Head.vmt`) and texture (`Head_basetexture.vtf`) are always emitted; only the geometry is affected.
- **`.mesh` parser** — `FromBuffer()` checks for `"version "` prefix, supports ASCII (v1) and binary (v2-6). COREMESH chunk versions 1 (standard) and 2 (Draco) are handled; unknown versions are skipped (data consumed, no geometry loaded). All R15 body meshes (LowerTorso, UpperTorso, arms, legs) are `AssetTypeId=4` `RenderMesh` from 2016-2017 and use COREMESH v1. Post-2023 meshes may use COREMESH v2+ which can throw in Draco decode.
- **`LodOffsets` fix** — Post-2023 COREMESH v2 body meshes include a `LODS` chunk that overwrites `LodOffsets` with `[0, 0]` after the `GEOM` chunk set it correctly. `LoadGeometry_Chunks` now guards: if `LodOffsets.Count >= 2 && LodOffsets[1] == 0 && Faces.Count > 0`, it corrects `LodOffsets[1]` to `Faces.Count`. Without this, `BuildAvatarGeometry` adds 0 triangles for all body part materials (Torso, LeftArm, RightArm, LeftLeg, RightLeg).
- **`FindFirstChildOfClass<T>()`** defaults to `recursive: false` (direct children only). Use `GetDescendantsOfType<T>()` to search the full tree. This matters in `Mesh.BakePart` fallback when a model wraps its `MeshPart` inside folders.
- **Layered clothing** — unsupported. OBJ export can't represent wrap deformation, so the extractor output was wrong even when it didn't crash. The Studio prompt was removed: `R15CharacterAssembler.AssembleModel` now drops `WrapLayer` accessories at assembly time with a print message. `LayeredClothingExtractor` remains as dead code (Debug-only "Use Existing OBJ" checkbox in the form).
- **R6 animation space** — R6 `KeyframeSequence` poses are raw Roblox deltas whose joint frames match the classic R6 Motor6D attachments (shoulder/hip `Ry(±90)`, neck YZ-swap). `AnimationBuilder.Assemble` writes the raw deltas straight into the SMD (near-origin bone-local frames) and the QC marks R6 sequences WITH `$delta`; at runtime Source composes them onto the reference pose (`refBone.C0 * delta`), exactly Roblox's `boneCFrame = C0 * pose`. Do NOT route R6 through `DeltaSequence` (absolute frames `refBone.C0 * pose` + `$delta` double-composes the motion, scrambling it in-game), and do NOT restore the hand-crafted axis patches (Torso YZ-flip, right-side X-invert, ±90° arm/leg position swap, rotation inverse) — they mirrored the exported motion (right arm swung forward at t=0 instead of backward). R15 poses get an Euler-angle reorder and ARE written through `DeltaSequence` with absolute frames and no `$delta`; the two rigs use opposite conventions, do not unify them.
- **Self-update** — `Launcher.cs` fetches `version.txt` from `aleksa07/Rbx2Source` (experimental), compares to Registry `CurrentVersion`. If different, shows a `MessageBox` popup — Yes opens release page, No continues. Saved to registry to avoid repeat prompts. Guarded by `#if !DEBUG`.
- **Error reporting** — `LogException` uses `GetRootException` to recursively unwrap `AggregateException`/`InnerException` chains (e.g. from `task.Wait()` in R15 layered clothing) down to the deepest real exception before showing message/stack trace.

## Upcoming work
- [x] Feature: Layered clothing prompt removed — WrapLayer accessories are dropped at assembly (extractor was broken by design)
- [ ] Enhancement: Model name textbox could be saved/loaded from settings
- [ ] Enhancement: OutfitID field in form could show outfit name on load

## v2.10.5 changelog
- [x] Release: Bumped all version refs to 2.10.5 (AssemblyInfo, App.config, form title, version.txt, Settings.cs, in-app changelog, git tag)
- [x] Bug: R6 animations fixed — raw delta frames + QC `$delta` (Source composes `reference * delta` = Roblox's `C0 * pose`); no longer scramble or mirror in-game
- [x] Feature: Head mode (Default/Faceless/Headless) with name-based auto-detect from head asset name
- [x] Feature: Advanced tab — body package override, force avatar type dropdown (Default/R6/R15), torso type selection
- [x] Bug: Retry on Roblox 429 rate limits; accurate 404 message for missing users/outfits
- [x] Feature: Avatar/outfit fetch on a background thread so the UI no longer freezes
- [x] Feature: Outfit details cached to disk so repeated outfit lookups skip the rate-limited API
- [x] Bug: Faceless Dynamic Heads (NoFace + empty texture) and accessory (glasses) positioning fixes
- [x] Cleanup: Removed broken layered clothing prompt (WrapLayer items skipped at assembly)

## v2.9.2 changelog
- [x] Release: Bumped all version refs to 2.9.2 (AssemblyInfo, App.config, form title, version.txt, git tag)
- [x] Feature: Model name textbox added to form, CustomModelName on CharacterAssembler
- [x] Feature: Search bar accepts numeric UserID, OutfitID (prefix "outfit/" or "o:")
- [x] Feature: UserAvatar.FromOutfitId() fetches outfit details from avatar API
- [x] Feature: About section — aleksa07 as maintainer (avatar + link), MaximumADHD moved to special thanks
- [x] Feature: Triangle-count logging in BuildAvatarGeometry and AssembleModel
- [x] Bug: Post-2023 COREMESH v2 body meshes producing zero triangles fixed (LODS chunk guard)
- [x] Bug: TrySetUsername wrapped in try-catch, null/empty guard
- [x] Bug: UserData.FromUsername guards empty userInfos.Data before indexing
- [x] Bug: SetDrawColor null-guarded
- [x] Bug: compilerInputField_Leave uses try-finally to always re-enable controls
- [x] Bug: About section layout — shifted left contributors down + third-party/VTFCmd down to fix clipping
- [x] Bug: Auto-update no longer silently downloads/overwrites — replaced with popup prompt
- [x] Bug: CurrentVersion now initialized in Registry defaults (was missing, caused loop)
- [x] Cleanup: version.txt removed from release assets

## v2.9.4 changelog
- [x] Release: Bumped all version refs to 2.9.4
- [x] Bug: Fixed RobloxFileFormat CPU config — changed Rbx2Source to build as x64 to match upstream
- [x] UI: Updated username label to "Username or UserID:"
- [x] CI: Fixed released.yml to build as x64

## v2.9.3 changelog
- [x] Release: Bumped all version refs to 2.9.3 (AssemblyInfo, App.config, form title, version.txt, git tag)
- [x] Bug: Fixed RobloxFileFormat CPU config — changed Rbx2Source to build as x64 to match upstream

## Version update checklist
When bumping version (e.g. 2.9.2 → 2.9.3), update ALL of these:
1. `Properties/AssemblyInfo.cs` — `AssemblyVersion` + `AssemblyFileVersion`
2. `App.config` — `CurrentVersion` value
3. `Forms/Rbx2Source.Designer.cs` — form title `"Rbx2Source vX.X"`
4. `version.txt` — plain version string
5. `Resources/Settings.cs` — `SetSetting("CurrentVersion", "X.X.X")` in the `InitializedV3` default block
6. Git tag — delete old, create new on latest commit


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
  - **Texture**: `GetAvatarFace` checks `characterAssets/ASSEMBLY/Head` first, then falls back to scanning all `characterAssets` descendants for `MeshPart` with `TextureID` where `GetLimb()` returns `BodyPart.Head`. The face texture is composited at layer 1 over the head color layer.
  - Head material (`Head.vmt`) and texture (`Head_basetexture.vtf`) are always emitted; only the geometry is affected.
- **`.mesh` parser** — `FromBuffer()` checks for `"version "` prefix, supports ASCII (v1) and binary (v2-6). COREMESH chunk versions 1 (standard) and 2 (Draco) are handled; unknown versions are skipped (data consumed, no geometry loaded). All R15 body meshes (LowerTorso, UpperTorso, arms, legs) are `AssetTypeId=4` `RenderMesh` from 2016-2017 and use COREMESH v1. Post-2023 meshes may use COREMESH v2+ which can throw in Draco decode.
- **`FindFirstChildOfClass<T>()`** defaults to `recursive: false` (direct children only). Use `GetDescendantsOfType<T>()` to search the full tree. This matters in `Mesh.BakePart` fallback when a model wraps its `MeshPart` inside folders.
- **Layered clothing** — explicitly unsupported per README; `LayeredClothingExtractor` exists but is partial/incomplete.
- **Self-update** — compares local version to GitHub `version.txt` on startup; downloads and renames `NEW_Rbx2Source.exe`.

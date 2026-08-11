using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Rbx2Source.Animating;
using Rbx2Source.Geometry;
using Rbx2Source.QuakeC;
using Rbx2Source.StudioMdl;
using Rbx2Source.Textures;
using Rbx2Source.Web;

using RobloxFiles;
using RobloxFiles.Enums;
using RobloxFiles.DataTypes;
using System.Diagnostics.Contracts;

namespace Rbx2Source.Assembler
{
    public enum Limb { Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg, Unknown }

    public enum HeadMode
    {
        Default,
        Faceless,
        Headless
    }
    
    public class CharacterAssembler : IAssembler<UserAvatar>
    {
        public static bool DEBUG_RAPID_ASSEMBLY = false;
        public string CustomModelName { get; set; }
        private const float DEG2RAD = (float)Math.PI / 180f;

        // Caches baked mesh geometry (keyed by resolved mesh asset + bake
        // inputs) so the collision pass reuses the character pass's mesh
        // parse/decode work instead of re-baking every MeshPart. Cleared at
        // the start of each Assemble() so a new avatar never leaks entries.
        private static readonly object BakedMeshCacheLock = new object();
        private static readonly Dictionary<string, Mesh> BakedMeshCache = new Dictionary<string, Mesh>();

        // User's choice from the Advanced tab (Default = auto-detect).
        public static HeadMode HeadModeSetting = HeadMode.Default;

        // Resolved per-avatar: combines the user setting with the auto-detection
        // signals (head asset name, NoFace tag, baked texture presence).
        public static HeadMode EffectiveHeadMode = HeadMode.Default;

        public static bool IsHeadless()
        {
            return EffectiveHeadMode == HeadMode.Headless;
        }

        public static BodyPart? GetLimb(BasePart part)
        {
            Contract.Requires(part != null);

            var invariant = StringComparison.InvariantCulture;
            string name = part.Name;

            if (name == "Head")
                return BodyPart.Head;
            else if (name.EndsWith("Torso", invariant))
                return BodyPart.Torso;

            string limbName;

            if (name.StartsWith("Left", invariant))
                limbName = "Left";
            else if (name.StartsWith("Right", invariant))
                limbName = "Right";
            else
                return null;

            if (name.EndsWith("Arm", invariant) || name.EndsWith("Hand", invariant))
                limbName += "Arm";
            else if (name.EndsWith("Leg", invariant) || name.EndsWith("Foot", invariant))
                limbName += "Leg";

            if (!Enum.TryParse(limbName, out BodyPart bodyPart))
                return null;

            return bodyPart;
        }

        public static List<Attachment> FindOtherAttachments(Attachment a, Instance bin)
        {
            Contract.Requires(a != null && bin != null);
            List<Attachment> result = new List<Attachment>();

            foreach (Instance child in bin.GetChildren())
            {
                Attachment b = child.FindFirstChild<Attachment>(a.Name);

                if (b != null && a != b)
                {
                    result.Add(b);
                }
            }

            return result;
        }

        // Indexes the first attachment of each name found under each of bin's
        // children, matching the lookup semantics of FindOtherAttachments.
        private static Dictionary<string, List<Attachment>> IndexBinAttachments(Instance bin)
        {
            var byName = new Dictionary<string, List<Attachment>>();

            foreach (Instance child in bin.GetChildren())
            {
                HashSet<string> seen = null;

                foreach (Instance sub in child.GetChildren())
                {
                    if (!(sub is Attachment att))
                        continue;

                    if (seen == null)
                        seen = new HashSet<string>();

                    if (!seen.Add(att.Name))
                        continue;

                    if (!byName.TryGetValue(att.Name, out var list))
                    {
                        list = new List<Attachment>();
                        byName[att.Name] = list;
                    }

                    list.Add(att);
                }
            }

            return byName;
        }

        private static List<Attachment> FindOtherAttachments(Dictionary<string, List<Attachment>> attachmentsByName, Attachment a)
        {
            List<Attachment> result = new List<Attachment>();

            if (attachmentsByName.TryGetValue(a.Name, out var candidates))
            {
                foreach (Attachment b in candidates)
                {
                    if (b != a)
                        result.Add(b);
                }
            }

            return result;
        }

        private static void GenerateBones(BoneAssemblePrep prep, Attachment[] queue, HashSet<Attachment> completed)
        {
            if (queue.Length == 0)
                return;

            Instance bin = queue[0].Parent.Parent;
            GenerateBones(prep, queue, bin, IndexBinAttachments(bin), completed);
        }

        private static void GenerateBones(BoneAssemblePrep prep, Attachment[] queue, Instance bin, Dictionary<string, List<Attachment>> attachmentsByName, HashSet<Attachment> completed)
        {
            foreach (Attachment a0 in queue)
            {
                List<Attachment> a1s = FindOtherAttachments(attachmentsByName, a0);

                foreach (Attachment a1 in a1s)
                {
                    if (a1 != null && !completed.Contains(a1))
                    {
                        BasePart part0 = (BasePart)a0.Parent;
                        BasePart part1 = (BasePart)a1.Parent;

                        bool isRigAttachment = a0.Name.EndsWith("RigAttachment", StringComparison.InvariantCulture);

                        if (isRigAttachment || prep.AllowNonRigs)
                        {
                            StudioBone bone = new StudioBone(part1.Name, part0, part1)
                            {
                                C0 = a0.CFrame,
                                C1 = a1.CFrame,
                            };

                            bone.IsAvatarBone = !prep.AllowNonRigs;
                            prep.Bones.Add(bone);

                            Node node = bone.Node;
                            node.NodeIndex = prep.Bones.Count - 1;
                            prep.Nodes.Add(node);

                            if (completed.Add(a0))
                                prep.Completed.Add(a0);

                            if (completed.Add(a1))
                                prep.Completed.Add(a1);

                            part1.CFrame = part0.CFrame * a0.CFrame * a1.CFrame.Inverse();

                            if (prep.AllowNonRigs)
                                continue;

                            GenerateBones(prep, part1.GetChildrenOfType<Attachment>(), bin, attachmentsByName, completed);
                        }
                        else // We'll deal with Accessory attachments afterwards.
                        {
                            prep.NonRigs.Add(a0);
                        }
                    }
                }
            }
        }

        public static BoneKeyframe AssembleBones(StudioMdlWriter meshBuilder, BasePart rootPart)
        {
            Contract.Requires(meshBuilder != null);
            Contract.Requires(rootPart != null);

            Rbx2Source.Print("Building Skeleton...");
            BoneKeyframe kf = new BoneKeyframe();

            List<StudioBone> bones = kf.Bones;
            List<Node> nodes = meshBuilder.Nodes;

            StudioBone rootBone = new StudioBone(rootPart.Name, rootPart)
            {
                C0 = new CFrame(),
                IsAvatarBone = true
            };

            Node rootNode = rootBone.Node;
            rootNode.NodeIndex = 0;

            bones.Add(rootBone);
            nodes.Add(rootNode);

            // Assemble the base rig.
            BoneAssemblePrep prep = new BoneAssemblePrep(ref bones, ref nodes);
            var completed = new HashSet<Attachment>();
            GenerateBones(prep, rootPart.GetChildrenOfType<Attachment>(), completed);

            // Assemble the accessories.
            prep.AllowNonRigs = true;
            GenerateBones(prep, prep.NonRigs.ToArray(), completed);

            // Apply the rig cframe data.
            meshBuilder.Skeleton.Add(kf);

            return kf;
        }

        public static void PrepareAccessory(Instance asset, Folder assembly)
        {
            Contract.Requires(asset != null && assembly != null);

            if (DEBUG_RAPID_ASSEMBLY)
            {
                asset.Destroy();
                return;
            }

            BasePart handle = asset.FindFirstChild<BasePart>("Handle");

            if (handle != null)
            {
                // Accessory instance names are not unique (e.g. "Accessory (MeshPartAccessory)"
                // is reused across uploads). The handle name becomes the material key, so two
                // same-named accessories would share one material and one of their textures
                // would win, rendering both with it. Dedupe against parts already in the assembly.
                string safeName = FileUtility.MakeNameWindowsSafe(asset.Name);

                if (safeName.Length == 0)
                    safeName = "accessory";

                string baseName = safeName;
                int index = 1;

                while (assembly.FindFirstChild<BasePart>(safeName) != null)
                    safeName = baseName + "_" + (index++);

                handle.Name = safeName;
                handle.CFrame = new CFrame();
                handle.Parent = assembly;
                
                if (asset is Accessory) // Make sure the attachment is in the Handle
                {
                    Attachment accAtt = asset.FindFirstChildOfClass<Attachment>();

                    if (accAtt == null)
                        return;

                    accAtt.Parent = handle;
                }
            }
        }

        public static void OverwriteHead(DataModelMesh mesh, BasePart head)
        {
            Contract.Requires(mesh != null && head != null);
            DataModelMesh currentMesh = head.FindFirstChild<DataModelMesh>("Mesh");

            if (currentMesh != null)
                currentMesh.Destroy();

            mesh.Name = "Mesh";
            mesh.Parent = head;

            // Apply Rthro adjustments
            Vector3Value[] overrides = mesh.GetChildrenOfType<Vector3Value>();

            foreach (Vector3Value overrider in overrides)
            {
                Attachment attachment = head.FindFirstChild<Attachment>(overrider.Name);
                if (attachment != null)
                {
                    CFrame cf = attachment.CFrame;
                    attachment.CFrame = new CFrame(overrider.Value) * (cf - cf.Position);
                }
            }

            // Move any extra instances into the Head.
            var extraInstances = mesh.GetChildren()
                .Except(overrides)
                .ToList();

            extraInstances.ForEach((inst) => inst.Parent = head);
        }

        public static void BuildAvatarGeometry(StudioMdlWriter meshBuilder, StudioBone bone)
        {
            Contract.Requires(meshBuilder != null && bone != null);
            
            string task = "BuildGeometry_" + bone.Node.Name;
            Rbx2Source.ScheduleTasks(task);

            Node node = bone.Node;
            BasePart part = bone.Part1;

            bool isAvatarLimb = bone.IsAvatarBone;
            string matName = part.Name;

            if (isAvatarLimb)
            {
                BodyPart? limb = GetLimb(part);

                if (!limb.HasValue)
                    throw new ArgumentException("Provided StudioBone did not point to a limb correctly.");

                matName = Rbx2Source.GetEnumName(limb.Value);
            }

            var material = new ValveMaterial() { UseAvatarMap = isAvatarLimb };
            Rbx2Source.Print("Building Geometry for {0}", part.Name);
            Rbx2Source.IncrementStack();

            Mesh geometry = GetBakedMesh(part, material);
            meshBuilder.Materials[matName] = material;
            int faceStride = geometry.LodOffsets[1];

            for (int i = 0; i < faceStride; i++)
            {
                Triangle tri = new Triangle()
                {
                    Node = node,
                    FaceIndex = i,
                    Mesh = geometry,
                    Material = matName
                };
                
                meshBuilder.Triangles.Add(tri);
            }

            Rbx2Source.Print("  -> Added {0} triangles for material '{1}', LodOffsets[1]={2}", faceStride, matName, geometry.LodOffsets[1]);

            Rbx2Source.DecrementStack();
            Rbx2Source.MarkTaskCompleted(task);
        }

        // Bakes (or reuses from cache) the geometry for a part. Head geometry is
        // always baked fresh: the collision model swaps the head for a low-poly
        // mesh, so a cached head bake would leak into the collision writer.
        // Everything else is cached keyed by the resolved mesh asset plus the
        // baked CFrame offset and scale, so parts that share a mesh id but have
        // different transforms never reuse a wrong bake.
        private static Mesh GetBakedMesh(BasePart part, ValveMaterial material)
        {
            bool isHead = GetLimb(part) == BodyPart.Head;
            string cacheKey = (isHead || part.Transparency >= 1) ? null : GetBakedMeshCacheKey(part);

            if (cacheKey != null)
            {
                lock (BakedMeshCacheLock)
                {
                    if (BakedMeshCache.TryGetValue(cacheKey, out Mesh cached))
                    {
                        SetupMaterialFromPart(part, material);
                        return cached;
                    }
                }
            }

            Mesh geometry = Mesh.BakePart(part, material);

            if (cacheKey != null && geometry != null)
            {
                lock (BakedMeshCacheLock)
                {
                    BakedMeshCache[cacheKey] = geometry;
                }
            }

            return geometry;
        }

        // Resolves the mesh Mesh.BakePart would load for this part and derives a
        // cache key from it plus the baked CFrame offset and scale. Returns null
        // for parts with no cacheable mesh (legacy DataModelMesh / no mesh), which
        // are always baked fresh.
        private static string GetBakedMeshCacheKey(BasePart part)
        {
            CFrame offset = part.CFrame;
            Vector3 scale = null;
            string meshRef;

            if (part is MeshPart meshPart)
            {
                string meshId = meshPart.MeshId;

                if (meshId != null && meshId.Length > 0)
                    meshRef = "id:" + meshId;
                else
                    meshRef = "std:" + part.Name; // StandardLimbs fallback is keyed by part name

                var initialSize = meshPart.InitialSize;

                if (initialSize.X == 0 || initialSize.Y == 0 || initialSize.Z == 0)
                    scale = Vector3.one;
                else
                    scale = meshPart.Size / initialSize;
            }
            else
            {
                SpecialMesh specialMesh = part.FindFirstChildOfClass<SpecialMesh>();

                if (specialMesh == null || specialMesh.MeshType != MeshType.FileMesh)
                    return null;

                meshRef = "id:" + specialMesh.MeshId;
                offset *= new CFrame(specialMesh.Offset);
                scale = specialMesh.Scale;
            }

            return meshRef + "|" + offset.ToString() + "|" + (scale ?? Vector3.one).ToString();
        }

        // Mirrors the material setup Mesh.BakePart performs so cached geometry
        // can be reused without re-decoding the mesh file.
        private static void SetupMaterialFromPart(BasePart part, ValveMaterial material)
        {
            material.LinkedTo = part;
            material.Reflectance = part.Reflectance;
            material.Transparency = part.Transparency;

            if (part.Transparency >= 1)
                return;

            Asset albedoAsset = null;
            Asset normalAsset = null;

            if (part is MeshPart meshPart)
            {
                var surface = meshPart.FindFirstChildOfClass<SurfaceAppearance>();

                if (surface != null)
                {
                    material.UseAvatarMap = false;
                    albedoAsset = Asset.GetByAssetId(surface.ColorMap);
                    normalAsset = Asset.GetByAssetId(surface.NormalMap);
                }
                else if (meshPart.TextureID != null)
                {
                    albedoAsset = Asset.GetByAssetId(meshPart.TextureID);
                }
            }
            else
            {
                SpecialMesh specialMesh = part.FindFirstChildOfClass<SpecialMesh>();

                if (specialMesh != null && specialMesh.MeshType == MeshType.FileMesh)
                {
                    albedoAsset = Asset.GetByAssetId(specialMesh.TextureId);
                    material.VertexColor = specialMesh.VertexColor;
                }
            }

            material.AddTextureAsset("basetexture", albedoAsset);
            material.AddTextureAsset("bumpmap", normalAsset);
        }

        public static Folder AppendCharacterAssets(UserAvatar avatar, string avatarType, string context = "CHARACTER")
        {
            Contract.Requires(avatar != null);
            Rbx2Source.PrintHeader("GATHERING " + context + " ASSETS");

            Folder characterAssets = new Folder();
            AssetInfo[] assets = avatar.Assets;

            foreach (AssetInfo info in assets)
            {
                long id = info.Id;
                var meta = info.Meta;
                Asset asset = Asset.Get(id);

                Instance import = asset.OpenAsModel();
                Folder typeSpecific = import.FindFirstChild<Folder>(avatarType);
                Folder artistIntent = import.FindFirstChild<Folder>("R15ArtistIntent");

                Vector3 scale = meta?.Scale;
                Vector3 position = meta?.Position;
                Vector3 rotation = meta?.Rotation;

                if (artistIntent != null && avatarType == "R15")
                    import = artistIntent;
                else if (typeSpecific != null)
                    import = typeSpecific;

                bool hasName = info.Name != null && info.Name.Length > 0;
                HashSet<SpecialMesh> headMeshes = null;

                // Single tree walk replacing the previous rotation / name-stamp /
                // position / scale passes. Each instance gets its transforms applied
                // in the same order the old passes used; head meshes (a SpecialMesh
                // named "Mesh" with a NeckRigAttachment descendant) are collected
                // here and stamped with the AssetName below.
                foreach (Instance desc in import.GetDescendants())
                {
                    if (desc is Attachment att)
                    {
                        CFrame cf = att.CFrame;

                        if (rotation != null)
                        {
                            // Meta rotation is applied to the attachment orientation.
                            // The position must be preserved, otherwise an attachment
                            // with its own 180-degree rotation (e.g. FaceFrontAttachment
                            // glasses) gets its position flipped even when meta
                            // rotation is (0, 0, 0).
                            var rot = cf.Rotation;
                            var pos = cf.Position;

                            var newRot = rot
                                * CFrame.Angles(0, 0, -rotation.Z * DEG2RAD)
                                * CFrame.Angles(-rotation.X * DEG2RAD, 0, 0)
                                * CFrame.Angles(0, -rotation.Y * DEG2RAD, 0);

                            cf = newRot + pos;
                        }

                        if (position != null)
                            cf *= new CFrame(-position);

                        if (scale != null)
                            cf = cf.Rotation + (cf.Position * scale);

                        att.CFrame = cf;
                    }
                    else if (desc is Vector3Value vec3)
                    {
                        if (scale != null)
                            vec3.Value *= scale;

                        if (hasName && vec3.Name == "NeckRigAttachment")
                        {
                            Instance parent = vec3.Parent;

                            while (parent != null)
                            {
                                if (parent is SpecialMesh headMesh
                                    && headMesh.Name == "Mesh"
                                    && headMesh.MeshType == MeshType.FileMesh)
                                {
                                    if (headMeshes == null)
                                        headMeshes = new HashSet<SpecialMesh>();

                                    headMeshes.Add(headMesh);
                                }

                                parent = parent.Parent;
                            }
                        }
                    }
                    else if (desc is BasePart part)
                    {
                        if (scale != null)
                            part.Size *= scale;
                    }
                    else if (desc is SpecialMesh specialMesh)
                    {
                        if (scale != null)
                            specialMesh.Scale *= scale;
                    }
                    else if (desc is FileMesh mesh)
                    {
                        if (scale != null)
                            mesh.Scale *= scale;
                    }
                }

                // Stamp the source asset name onto the head mesh so face detection
                // can classify faceless/headless heads by name.
                if (hasName && headMeshes != null)
                {
                    foreach (SpecialMesh mesh in headMeshes)
                    {
                        StringValue existing = mesh.FindFirstChild<StringValue>("AssetName");
                        if (existing != null)
                            existing.Destroy();

                        new StringValue { Name = "AssetName", Value = info.Name }.Parent = mesh;
                    }
                }

                var children = import
                    .GetChildren()
                    .ToList();

                children.ForEach(obj => obj.Parent = characterAssets);
            }

            return characterAssets;
        }

        public static Folder AppendCollisionAssets(UserAvatar avatar, string avatarType)
        {
            Folder collisionAssets;

            if (DEBUG_RAPID_ASSEMBLY)
                collisionAssets = new Folder();
            else
                collisionAssets = AppendCharacterAssets(avatar, avatarType, "COLLISION");

            // Replace the head mesh with a low-poly head
            DataModelMesh oldHeadMesh = collisionAssets.FindFirstChildOfClass<DataModelMesh>();

            if (oldHeadMesh != null)
                oldHeadMesh.Destroy();
            
            _ = new SpecialMesh()
            {
                MeshId = "rbxassetid://582002794",
                MeshType = MeshType.FileMesh,
                Scale = new Vector3(1, 1, 1),
                Offset = new Vector3(),
                Parent = collisionAssets
            };

            return collisionAssets;
        }

        // Resolves the head mode for this avatar. An explicit user override wins;
        // otherwise the head asset name is inspected for headless/faceless hints.
        private static HeadMode ResolveHeadMode(Folder characterAssets)
        {
            if (HeadModeSetting != HeadMode.Default)
                return HeadModeSetting;

            string headName = GetHeadAssetName(characterAssets);

            if (IsHeadlessName(headName))
                return HeadMode.Headless;

            if (IsFacelessName(headName))
                return HeadMode.Faceless;

            return HeadMode.Default;
        }

        private static string GetHeadAssetName(Folder characterAssets)
        {
            Contract.Requires(characterAssets != null);
            Folder assembly = characterAssets.FindFirstChild<Folder>("ASSEMBLY");

            if (assembly != null)
            {
                BasePart head = assembly.FindFirstChild<BasePart>("Head");

                if (head != null)
                {
                    StringValue nameValue = head.FindFirstChild<StringValue>("AssetName");

                    if (nameValue != null && nameValue.Value != null)
                        return nameValue.Value;

                    SpecialMesh mesh = head.FindFirstChildOfClass<SpecialMesh>();

                    if (mesh != null)
                    {
                        nameValue = mesh.FindFirstChild<StringValue>("AssetName");
                        if (nameValue != null && nameValue.Value != null)
                            return nameValue.Value;
                    }
                }
            }

            return "";
        }

        private static bool IsHeadlessName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            string lower = name.ToLowerInvariant();
            return lower.Contains("headless")
                || lower.Contains("nohead")
                || lower.Contains("no head");
        }

        private static bool IsFacelessName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            string lower = name.ToLowerInvariant();
            return lower.Contains("faceless")
                || lower.Contains("noface")
                || lower.Contains("no face");
        }

        public static Asset GetAvatarFace(Folder characterAssets)
        {
            // Check if this avatar's head is using a texture overlay.
            Contract.Requires(characterAssets != null);
            Folder assembly = characterAssets.FindFirstChild<Folder>("ASSEMBLY");

            // Faceless/headless modes composite no face layer at all.
            if (EffectiveHeadMode == HeadMode.Headless || EffectiveHeadMode == HeadMode.Faceless)
                return null;

            if (assembly != null)
            {
                BasePart head = assembly.FindFirstChild<BasePart>("Head");

                if (head != null)
                {
                    // Dynamic head as MeshPart with baked texture.
                    if (head is MeshPart meshHead)
                    {
                        string texId = meshHead.TextureID;

                        if (texId != null && texId.Length > 0)
                            return Asset.GetByAssetId(texId);

                        // Mesh head with no baked texture is faceless.
                        return null;
                    }

                    // Dynamic head via SpecialMesh. The face texture is baked into the
                    // head texture itself, so any non-empty FileMesh texture is the face.
                    SpecialMesh headMesh = head.FindFirstChildOfClass<SpecialMesh>();

                    if (headMesh != null && headMesh.MeshType == MeshType.FileMesh)
                    {
                        string textureId = headMesh.TextureId;

                        // NoFace dynamic heads come in two kinds:
                        //  - Mood/expression control maps (e.g. Devon Default), whose
                        //    face is drawn by Roblox's dynamic-head shader, not baked
                        //    into the texture — nothing static to export.
                        //  - Real face overlays (e.g. expression heads like "Tired Face"),
                        //    where the texture is the face painted on a transparent
                        //    background and can be composited directly.
                        // Only composite the overlay kind; keep shader-driven heads faceless.
                        if (textureId == null || textureId.Length == 0)
                            return null;

                        Asset faceTexture = Asset.GetByAssetId(textureId);

                        if (headMesh.Tags.Contains("NoFace") && !IsRealFaceOverlay(faceTexture))
                            return null;

                        return faceTexture;
                    }
                }
            }

            // Fallback: search characterAssets for dynamic head MeshParts outside ASSEMBLY.
            foreach (MeshPart part in characterAssets.GetDescendantsOfType<MeshPart>())
            {
                if (part.Parent == assembly)
                    continue;

                BodyPart? limb = GetLimb(part);
                if (limb == BodyPart.Head)
                {
                    string texId = part.TextureID;
                    if (texId != null && texId.Length > 0)
                        return Asset.GetByAssetId(texId);
                }
            }

            // Fall back to normal behavior.
            Decal face = characterAssets.FindFirstChild<Decal>("face");
            Asset result;

            if (face != null && face.Texture != "rbxasset://textures/face.png")
                result = Asset.GetByAssetId(face.Texture);
            else
                result = Asset.FromResource("Images/face.png");

            return result;
        }

        // Distinguishes real face overlays from shader-driven mood/expression
        // control maps. A genuine painted face has anti-aliased (semi-transparent)
        // edges around its features, while control maps are flat color regions
        // with strictly opaque or fully transparent pixels.
        private static bool IsRealFaceOverlay(Asset texture)
        {
            try
            {
                byte[] content = texture.GetContent();

                if (content == null || content.Length == 0)
                    return false;

                using (var stream = new MemoryStream(content))
                using (var image = new Bitmap(stream))
                using (var argb = image.PixelFormat == PixelFormat.Format32bppArgb
                    ? image
                    : new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb))
                {
                    if (!ReferenceEquals(argb, image))
                    {
                        using (Graphics g = Graphics.FromImage(argb))
                            g.DrawImage(image, 0, 0, image.Width, image.Height);
                    }

                    var rect = new Rectangle(0, 0, argb.Width, argb.Height);
                    BitmapData data = argb.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                    try
                    {
                        int stride = data.Stride;
                        int bufferLength = Math.Abs(stride) * argb.Height;
                        byte[] pixels = new byte[bufferLength];
                        Marshal.Copy(data.Scan0, pixels, 0, bufferLength);

                        bool topDown = stride > 0;

                        for (int y = 0; y < argb.Height; y += 4)
                        {
                            int row = topDown ? y * stride : (argb.Height - 1 - y) * stride;

                            for (int x = 0; x < argb.Width; x += 4)
                            {
                                int alpha = pixels[row + x * 4 + 3];

                                if (alpha >= 16 && alpha <= 239)
                                    return true;
                            }
                        }
                    }
                    finally
                    {
                        argb.UnlockBits(data);
                    }
                }
            }
            catch
            {
                // Not an image, or decode failed — treat as faceless.
            }

            return false;
        }

        public static float ComputeFloorLevel(Folder assembly)
        {
            Contract.Requires(assembly != null);

            BasePart lowest = null;
            float lowestY = float.MaxValue;

            foreach (BasePart part in assembly.GetChildrenOfType<BasePart>())
            {
                if (part.Name.Contains("_"))
                    continue;

                float y = part.CFrame.Y;

                if (y < lowestY)
                {
                    lowest = part;
                    lowestY = y;
                }
            }

            return (lowestY - (lowest.Size.Y / 2f)) * Rbx2Source.MODEL_SCALE;
        }

        public AssemblerData Assemble(UserAvatar avatar)
        {
            Contract.Requires(avatar != null);

            lock (BakedMeshCacheLock)
                BakedMeshCache.Clear();

            UserInfo userInfo = avatar.UserInfo;
            string userName = FileUtility.MakeNameWindowsSafe(userInfo.Name);

            string appData = Environment.GetEnvironmentVariable("LocalAppData");
            string rbx2Src = Path.Combine(appData, "Rbx2Source");
            string avatars = Path.Combine(rbx2Src, "Avatars");
            string userBin = Path.Combine(avatars, userName);

            string modelDir = Path.Combine(userBin, "Model");
            string anim8Dir = Path.Combine(modelDir, "Animations");
            string texturesDir = Path.Combine(userBin, "Textures");
            string materialsDir = Path.Combine(userBin, "Materials");

            FileUtility.InitiateEmptyDirectories(modelDir, anim8Dir, texturesDir, materialsDir);

            AvatarType avatarType = avatar.PlayerAvatarType;

            string forceAvatarType = Resources.Settings.GetString("ForceAvatarType");
            if (forceAvatarType == "R15")
                avatarType = AvatarType.R15;
            else if (forceAvatarType == "R6")
                avatarType = AvatarType.R6;

            ICharacterAssembler assembler;

            if (avatarType == AvatarType.R15)
                assembler = new R15CharacterAssembler();
            else
                assembler = new R6CharacterAssembler();

            string modelNameSafe = FileUtility.MakeNameWindowsSafe(CustomModelName);
            string compileDir = string.IsNullOrWhiteSpace(CustomModelName)
                ? "roblox_avatars/" + userName
                : "roblox_avatars/" + modelNameSafe;

            string avatarTypeName = Rbx2Source.GetEnumName(avatarType);
            Folder characterAssets = AppendCharacterAssets(avatar, avatarTypeName);

            EffectiveHeadMode = ResolveHeadMode(characterAssets);

            ApplyBodyPackageOverrides(characterAssets, avatarType);

            Rbx2Source.ScheduleTasks
            (
                "BuildCharacter",
                "BuildCollisionModel", 
                "BuildAnimations",
                "BuildTextures",
                "BuildMaterials", 
                "BuildCompilerScript"
            );

            Rbx2Source.PrintHeader("BUILDING CHARACTER MODEL");
            #region Build Character Model
            ///////////////////////////////////////////////////////////////////////////////////////////////////////

            StudioMdlWriter writer = assembler.AssembleModel(characterAssets, avatar, DEBUG_RAPID_ASSEMBLY);

            string studioMdl = writer.BuildFile();
            string modelPath = Path.Combine(modelDir, "CharacterModel.smd");
            FileUtility.WriteFile(modelPath, studioMdl);

            string staticPose = writer.BuildFile(false);
            string refPath = Path.Combine(modelDir, "ReferencePos.smd");
            FileUtility.WriteFile(refPath, staticPose);

            Rbx2Source.MarkTaskCompleted("BuildCharacter");

            ///////////////////////////////////////////////////////////////////////////////////////////////////////
            #endregion

            Rbx2Source.PrintHeader("BUILDING COLLISION MODEL");
            #region Build Character Collisions
            ///////////////////////////////////////////////////////////////////////////////////////////////////////

            Folder collisionAssets = AppendCollisionAssets(avatar, avatarTypeName);
            StudioMdlWriter collisionWriter = assembler.AssembleModel(collisionAssets, avatar, true);

            string collisionModel = collisionWriter.BuildFile();
            string cmodelPath = Path.Combine(modelDir, "CollisionModel.smd");
            FileUtility.WriteFile(cmodelPath, collisionModel); 

            byte[] collisionJoints = assembler.CollisionModelScript;
            string cjointsPath = Path.Combine(modelDir, "CollisionJoints.qc");

            FileUtility.WriteFile(cjointsPath, collisionJoints);
            Rbx2Source.MarkTaskCompleted("BuildCollisionModel");

            ///////////////////////////////////////////////////////////////////////////////////////////////////////
            #endregion

            Rbx2Source.PrintHeader("BUILDING CHARACTER ANIMATIONS");
            #region Build Character Animations
            ///////////////////////////////////////////////////////////////////////////////////////////////////////

            var animIds = assembler.CollectAnimationIds(avatar);
            var compileAnims = new Dictionary<string, Asset>();
                
            if (animIds.Count > 0)
            {
                Rbx2Source.Print("Collecting Animations...");
                Rbx2Source.IncrementStack();

                Action<string, Asset> collectAnimation = (animName, animAsset) =>
                {
                    if (!compileAnims.ContainsKey(animName))
                    {
                        Rbx2Source.Print("Collected animation {0} with id {1}", animName, animAsset.Id);
                        compileAnims.Add(animName, animAsset);
                    }
                };

                foreach (string animName in animIds.Keys)
                {
                    var animId = animIds[animName];
                    var animAsset = animId.GetAsset();
                    var import = animAsset.OpenAsModel();

                    if (animId.AnimationType == AnimationType.R15AnimFolder)
                    {
                        Folder r15Anim = import.FindFirstChild<Folder>("R15Anim");

                        if (r15Anim != null)
                        {
                            foreach (Instance animDef in r15Anim.GetChildren())
                            {
                                if (animDef.Name == "idle")
                                {
                                    var anims = animDef.GetChildrenOfType<Animation>();

                                    if (anims.Length == 2)
                                    {
                                        var getLookAnim = anims.OrderBy((anim) =>
                                        {
                                            var weight = anim.FindFirstChild<NumberValue>("Weight");

                                            if (weight != null)
                                                return weight.Value;

                                            return 0.0;
                                        });

                                        var lookAnim = getLookAnim.First();
                                        lookAnim.Destroy();

                                        Asset lookAsset = Asset.GetByAssetId(lookAnim.AnimationId);
                                        collectAnimation("Idle2", lookAsset);
                                    }
                                }

                                Animation compileAnim = animDef.FindFirstChildOfClass<Animation>();

                                if (compileAnim != null)
                                {
                                    Asset compileAsset = Asset.GetByAssetId(compileAnim.AnimationId);
                                    string compileName = animName;

                                    if (animDef.Name == "pose")
                                        compileName = "Pose";

                                    collectAnimation(compileName, compileAsset);
                                }
                            }
                        }
                    }
                    else
                    {
                        collectAnimation(animName, animAsset);
                    }
                }

                Rbx2Source.DecrementStack();
            }
            else
            {
                Rbx2Source.Print("No animations found :(");
            }

            if (compileAnims.Count > 0)
            {
                Rbx2Source.Print("Assembling Animations...");
                Rbx2Source.IncrementStack();

                foreach (string animName in compileAnims.Keys)
                {
                    Rbx2Source.Print("Building Animation {0}...", animName);

                    Asset animAsset = compileAnims[animName];
                    KeyframeSequence sequence = OpenAnimationSequence(animAsset, avatarType, animName, out Asset usedAsset);

                    if (sequence == null)
                        continue;

                    sequence.Name = animName;

                    var avatarTypeRef = new StringValue()
                    {
                        Value = $"{avatarType}",
                        Name = "AvatarType",
                        Parent = sequence
                    };

                    string animation = AnimationBuilder.Assemble(sequence, writer.Skeleton[0].Bones);
                    string animPath = Path.Combine(anim8Dir, animName + ".smd");

                    FileUtility.WriteFile(animPath, animation);
                }

                Rbx2Source.DecrementStack();
            }

            Rbx2Source.MarkTaskCompleted("BuildAnimations");

            ///////////////////////////////////////////////////////////////////////////////////////////////////////
            #endregion

            Rbx2Source.PrintHeader("BUILDING CHARACTER TEXTURES");
            #region Build Character Textures
            ///////////////////////////////////////////////////////////////////////////////////////////////////////

            var materials = writer.Materials;
            TextureBindings textures;

            if (DEBUG_RAPID_ASSEMBLY)
            {
                textures = new TextureBindings();
                materials.Clear();
            }
            else
            {
                TextureCompositor texCompositor = assembler.ComposeTextureMap(characterAssets, avatar.BodyColor3s);
                textures = assembler.BindTextures(texCompositor, materials);
            }

            var images = textures.Images;
            textures.MaterialDirectory = compileDir;

            foreach (string imageName in images.Keys)
            {
                Rbx2Source.Print("Writing Image {0}.png", imageName);

                Image image = images[imageName];
                string imagePath = Path.Combine(texturesDir, imageName + ".png");

                try
                {
                    image.Save(imagePath, ImageFormat.Png);
                }
                catch
                {
                    Rbx2Source.Print("IMAGE {0}.png FAILED TO SAVE!", imageName);
                }

                FileUtility.LockFile(imagePath);
            }

            CompositData.FreeAllocatedTextures();
            Rbx2Source.MarkTaskCompleted("BuildTextures");

            ///////////////////////////////////////////////////////////////////////////////////////////////////////
            #endregion

            Rbx2Source.PrintHeader("WRITING MATERIAL FILES");
            #region Write Material Files
            ///////////////////////////////////////////////////////////////////////////////////////////////////////

            var matLinks = textures.MatLinks;

            foreach (string mtlName in matLinks.Keys)
            {
                Rbx2Source.Print("Building VMT {0}.vmt", mtlName);

                var targetVtfs = matLinks[mtlName];
                string vmtPath = Path.Combine(materialsDir, mtlName + ".vmt");
                ValveMaterial mtl = materials[mtlName];

                foreach (var pair in targetVtfs)
                    mtl.SetVmtField(pair.Key, "models/" + compileDir + "/" + pair.Value);

                mtl.WriteVmtFile(vmtPath);
            }

            Rbx2Source.MarkTaskCompleted("BuildMaterials");

            ///////////////////////////////////////////////////////////////////////////////////////////////////////
            #endregion

            Rbx2Source.PrintHeader("WRITING COMPILER SCRIPT");
            #region Write Compiler Script
            ///////////////////////////////////////////////////////////////////////////////////////////////////////

            string modelName = compileDir + ".mdl";
            QuakeCWriter qc = new QuakeCWriter();

            qc.Add("body", userName, "CharacterModel.smd");
            qc.Add("modelname", modelName);
            qc.Add("upaxis", "y");

            // Compute the floor level of the avatar.
            Folder assembly = characterAssets.FindFirstChild<Folder>("ASSEMBLY");

            if (assembly != null)
            {
                float floor = ComputeFloorLevel(assembly);
                string origin = "0 " + floor.ToInvariantString() + " 0";
                qc.Add("origin", origin);
            }

            qc.Add("cdmaterials", "models/" + compileDir);
            qc.Add("surfaceprop", "flesh");
            qc.Add("include", "CollisionJoints.qc");

            QuakeCItem refAnim = qc.Add("sequence", "reference", "ReferencePos.smd");
            refAnim.AddSubItem("fps", 1);
            refAnim.AddSubItem("loop");

            foreach (string animName in compileAnims.Keys)
            {
                QuakeCItem sequence = qc.Add("sequence", animName.ToLowerInvariant(), "Animations/" + animName + ".smd");
                sequence.AddSubItem("fps", AnimationBuilder.FrameRate);

                if (avatarType == AvatarType.R6)
                    sequence.AddSubItem("delta");

                sequence.AddSubItem("loop");
            }

            string qcFile = qc.ToString();
            string qcPath = Path.Combine(modelDir, "Compile.qc");

            FileUtility.WriteFile(qcPath, qcFile);
            Rbx2Source.MarkTaskCompleted("BuildCompilerScript");

            ///////////////////////////////////////////////////////////////////////////////////////////////////////
            #endregion

            AssemblerData data = new AssemblerData()
            {
                ModelData = writer,
                ModelName = modelName,
                TextureData = textures,
                CompilerScript = qcPath,
                
                RootDirectory = userBin,
                CompileDirectory = compileDir,
                TextureDirectory = texturesDir,
                MaterialDirectory = materialsDir,
            };

            return data;
        }

        private static KeyframeSequence OpenAnimationSequence(Asset animAsset, AvatarType avatarType, string animName, out Asset usedAsset)
        {
            usedAsset = animAsset;

            if (animAsset == null)
                return null;

            Instance import = animAsset.OpenAsModel();
            KeyframeSequence sequence = import?.FindFirstChildOfClass<KeyframeSequence>();

            if (sequence != null)
                return sequence;

            // Roblox now ships default locomotion animations as CurveAnimation
            // bundles (per-joint Vector3Curve / EulerRotationCurve). AnimationBuilder
            // can't parse those yet, so fall back to the legacy R15 KeyframeSequence
            // defaults to avoid failing the whole assembly.
            if (avatarType == AvatarType.R15
                && R15CharacterAssembler.TryGetDefaultAnimationId(animName, out long defaultId)
                && animAsset.Id != defaultId)
            {
                Asset fallback = Asset.Get(defaultId);
                Instance fallbackImport = fallback.OpenAsModel();
                KeyframeSequence fallbackSeq = fallbackImport?.FindFirstChildOfClass<KeyframeSequence>();

                if (fallbackSeq != null)
                {
                    Rbx2Source.Print("Animation {0} uses the unsupported CurveAnimation format; falling back to default (id {1}).", animName, defaultId);
                    usedAsset = fallback;
                    return fallbackSeq;
                }
            }

            Rbx2Source.Print("Animation {0} uses an unsupported format and was skipped.", animName);
            return null;
        }

        private void ApplyBodyPackageOverrides(Folder characterAssets, AvatarType avatarType)
        {
            string forceAvatarType = Resources.Settings.GetString("ForceAvatarType");
            if (forceAvatarType == "Default")
                return;

            if (avatarType == AvatarType.R15)
            {
                Rbx2Source.Print("Force R15 is enabled — stripping custom body parts for blocky R15");
                ApplyR15Overrides(characterAssets);
            }
            else
            {
                string bodyPackage = Resources.Settings.GetString("BodyPackage");
                string torsoType = Resources.Settings.GetString("TorsoType");

                bool hasBodyPackageOverride = bodyPackage != "Default" && BodyPackages.Packages.ContainsKey(bodyPackage);
                bool hasTorsoTypeOverride = torsoType == "Girl";

                if (!hasBodyPackageOverride && !hasTorsoTypeOverride)
                    return;

                Dictionary<BodyPart, long> meshOverrides = null;

                if (hasBodyPackageOverride)
                {
                    Rbx2Source.Print("Applying R6 body package: " + bodyPackage);
                    meshOverrides = BodyPackages.ResolveMeshIds(BodyPackages.Packages[bodyPackage]);
                }
                else if (hasTorsoTypeOverride)
                {
                    Rbx2Source.Print("Applying R6 Girl torso override");
                    meshOverrides = BodyPackages.ResolveGirlTorso();
                }

                if (meshOverrides != null && meshOverrides.Count > 0)
                    ApplyR6Overrides(characterAssets, meshOverrides);
            }
        }

        private void ApplyR6Overrides(Folder characterAssets, Dictionary<BodyPart, long> meshOverrides)
        {
            foreach (Instance child in characterAssets.GetChildren())
            {
                if (child is CharacterMesh)
                {
                    var characterMesh = child as CharacterMesh;

                    if (meshOverrides.ContainsKey(characterMesh.BodyPart))
                    {
                        long newMeshId = meshOverrides[characterMesh.BodyPart];
                        characterMesh.MeshId = newMeshId;
                    }
                }
            }
        }

        private void ApplyR15Overrides(Folder characterAssets)
        {
            var toRemove = new List<Instance>();

            foreach (Instance child in characterAssets.GetChildren())
            {
                if (child is MeshPart)
                {
                    BodyPart? limb = GetLimb(child as BasePart);

                    if (limb.HasValue && limb.Value != BodyPart.Head)
                    {
                        toRemove.Add(child);
                    }
                }
            }

            foreach (Instance item in toRemove)
            {
                Rbx2Source.Print("  Removing custom R15 part: " + item.Name);
                item.Destroy();
            }
        }
    }
}

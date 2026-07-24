using System.Collections.Generic;
using RobloxFiles;
using RobloxFiles.Enums;
using Rbx2Source.Web;

namespace Rbx2Source.Assembler
{
    public static class BodyPackages
    {
        public struct PackageData
        {
            public long Torso;
            public long LeftArm;
            public long RightArm;
            public long LeftLeg;
            public long RightLeg;
        }

        public static readonly Dictionary<string, PackageData> Packages = new Dictionary<string, PackageData>
        {
            {
                "Boy", new PackageData
                {
                    Torso   = 376532000,
                    LeftArm = 376530220,
                    RightArm= 376531012,
                    LeftLeg = 376531300,
                    RightLeg= 376531703
                }
            },
            {
                "Girl", new PackageData
                {
                    Torso   = 376547767,
                    LeftArm = 376547633,
                    RightArm= 376547341,
                    LeftLeg = 376546668,
                    RightLeg= 376547092
                }
            },
            {
                "Man", new PackageData
                {
                    Torso   = 86500008,
                    LeftArm = 86500054,
                    RightArm= 86500036,
                    LeftLeg = 86500064,
                    RightLeg= 86500078
                }
            },
            {
                "Woman", new PackageData
                {
                    Torso   = 86499666,
                    LeftArm = 86499716,
                    RightArm= 86499698,
                    LeftLeg = 86499753,
                    RightLeg= 86499793
                }
            }
        };

        public static readonly long GirlTorso = 376547767;

        public static long GetMeshId(long characterMeshAssetId)
        {
            Asset asset = Asset.Get(characterMeshAssetId);
            Instance model = asset.OpenAsModel();

            CharacterMesh mesh = null;
            foreach (Instance child in model.GetChildren())
            {
                if (child is CharacterMesh)
                {
                    mesh = child as CharacterMesh;
                    break;
                }
            }

            if (mesh == null)
            {
                Folder r6Folder = model.FindFirstChild<Folder>("R6");
                if (r6Folder != null)
                {
                    foreach (Instance child in r6Folder.GetChildren())
                    {
                        if (child is CharacterMesh)
                        {
                            mesh = child as CharacterMesh;
                            break;
                        }
                    }
                }
            }

            if (mesh != null)
                return mesh.MeshId;

            return 0;
        }

        public static Dictionary<BodyPart, long> ResolveMeshIds(PackageData package)
        {
            var result = new Dictionary<BodyPart, long>();

            result[BodyPart.Torso]    = GetMeshId(package.Torso);
            result[BodyPart.LeftArm]  = GetMeshId(package.LeftArm);
            result[BodyPart.RightArm] = GetMeshId(package.RightArm);
            result[BodyPart.LeftLeg]  = GetMeshId(package.LeftLeg);
            result[BodyPart.RightLeg] = GetMeshId(package.RightLeg);

            return result;
        }

        public static Dictionary<BodyPart, long> ResolveGirlTorso()
        {
            var result = new Dictionary<BodyPart, long>();
            result[BodyPart.Torso] = GetMeshId(GirlTorso);
            return result;
        }
    }
}

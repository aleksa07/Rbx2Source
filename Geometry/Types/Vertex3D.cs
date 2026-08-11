using System.Drawing;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using RobloxFiles;
using RobloxFiles.DataTypes;
using Rbx2Source.StudioMdl;

namespace Rbx2Source.Geometry
{
    // 3D Geometry components of the Vertex class
    // The 2D Texture components are defined in Textures/Vertex2D.cs

    public partial class Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 UV;

        public Color? Color;
        public MeshSkinning Skinning;
        public Dictionary<int, float> Weights;

        private static void AppendFloat(StringBuilder sb, float value)
        {
            if (float.IsNaN(value))
                sb.Append('0');
            else
                sb.Append(value.ToString("0.#######", CultureInfo.InvariantCulture));
        }

        public string WriteStudioMdl(StudioMdlWriter writer, BasePart identity, Mesh mesh)
        {
            var scale = Rbx2Source.MODEL_SCALE;

            float[] baseValues =
            {
                Position.X * scale,
                Position.Y * scale,
                Position.Z * scale,

                Normal.X,
                Normal.Y,
                Normal.Z,

                UV.X,
                1 - UV.Y,
            };

            int numWeights = Weights == null ? 0 : Weights.Count;
            int foundWeights = 0;
            int insertAt = 8;

            if (numWeights > 0)
            {
                foreach (var pair in Weights)
                {
                    var bone = mesh.Bones[pair.Key];

                    if (bone.Name == identity.Name)
                        numWeights -= 1;
                    else if (writer.GetNodeIndexByName(bone.Name) >= 0)
                        foundWeights += 1;
                }

                insertAt = 8 + (foundWeights * 2) - (numWeights * 2);
            }

            var sb = new StringBuilder(64);

            for (int i = 0; i < insertAt; i++)
            {
                AppendFloat(sb, baseValues[i]);
                sb.Append(' ');
            }

            if (numWeights > 0)
            {
                sb.Append(numWeights.ToInvariantString());
                sb.Append(' ');
            }

            for (int i = insertAt; i < baseValues.Length; i++)
            {
                AppendFloat(sb, baseValues[i]);
                sb.Append(' ');
            }

            if (numWeights > 0)
            {
                foreach (var pair in Weights)
                {
                    var bone = mesh.Bones[pair.Key];

                    if (bone.Name == identity.Name)
                        continue;

                    int targetNode = writer.GetNodeIndexByName(bone.Name);

                    if (targetNode >= 0)
                    {
                        sb.Append(targetNode.ToInvariantString());
                        sb.Append(' ');
                        AppendFloat(sb, pair.Value);
                        sb.Append(' ');
                    }
                }
            }

            sb.Length -= 1;
            return sb.ToString();
        }
    }
}

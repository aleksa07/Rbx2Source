using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.IO;

using Rbx2Source.Geometry;

namespace Rbx2Source.StudioMdl
{
    public class Triangle : IStudioMdlEntity<Triangle>
    {
        public string GroupName => "triangles";

        public string Material;
        public int FaceIndex;

        public Node Node;
        public Mesh Mesh;
        
        public void WriteStudioMdl(StringWriter buffer, StudioMdlWriter writer, List<Triangle> triangles)
        {
            Contract.Requires(buffer != null && triangles != null);

            writer.EnsureNodeIndexCache();

            var verts = Mesh.Verts;
            int boneIndex = Node.NodeIndex;

            string[] coords = writer.GetCachedVertexCoords(Mesh);

            if (coords == null)
            {
                var part1 = Node.StudioBone.Part1;
                coords = new string[verts.Count];

                for (int i = 0; i < verts.Count; i++)
                    coords[i] = verts[i].WriteStudioMdl(writer, part1, Mesh);

                writer.SetCachedVertexCoords(Mesh, coords);
            }

            int[] face = Mesh.Faces[FaceIndex];
            buffer.WriteLine(Material);

            for (int i = 0; i < 3; i++)
            {
                string coordStr = coords[face[i]];

                buffer.Write(boneIndex.ToInvariantString());
                buffer.Write(' ');
                buffer.WriteLine(coordStr);
            }
        }
    }
}

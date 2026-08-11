#pragma warning disable 0649

using System.Collections;
using System.Collections.Generic;
using System.IO;

using RobloxFiles;
using Rbx2Source.Geometry;
using System.Diagnostics.Contracts;

namespace Rbx2Source.StudioMdl
{
    public class Node : IStudioMdlEntity<Node>
    {
        public string GroupName => "nodes";

        public int NodeIndex;
        public string Name;

        public StudioBone StudioBone;
        public Mesh Mesh;

        public int ParentIndex = -1;
        public bool UseParentIndex = false;

        public void WriteStudioMdl(StringWriter fileBuffer, StudioMdlWriter writer, List<Node> nodes)
        {
            Contract.Requires(fileBuffer != null && nodes != null);

            writer.EnsureNodeIndexCache();

            int nodeIndex = writer.GetNodeIndex(this);
            NodeIndex = nodeIndex;
            ParentIndex = UseParentIndex ? ParentIndex : writer.GetParentIndex(this);

            string joined = string.Join(" ", nodeIndex, '"' + Name + '"', ParentIndex);
            fileBuffer.WriteLine(joined);
        }
    }
}

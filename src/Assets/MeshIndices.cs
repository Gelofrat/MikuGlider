using UnityEngine;
using UnityEngine.Rendering;

namespace MikuGlider;

internal static class MeshIndices
{
    internal static void Assign(Mesh mesh, int[][] submeshes)
    {
        int count = 0;
        var sections = new SubMeshDescriptor[submeshes.Length];
        for (int index = 0; index < submeshes.Length; index++)
        {
            sections[index] = new SubMeshDescriptor(count, submeshes[index].Length, MeshTopology.Triangles);
            count = checked(count + submeshes[index].Length);
        }
        mesh.SetIndexBufferParams(count, mesh.indexFormat);
        if (mesh.indexFormat == IndexFormat.UInt16)
        {
            var indices = new ushort[count];
            int offset = 0;
            foreach (int[] submesh in submeshes)
                foreach (int value in submesh)
                    indices[offset++] = checked((ushort)value);
            mesh.SetIndexBufferData(indices, 0, 0, count, MeshUpdateFlags.DontRecalculateBounds);
        }
        else
        {
            var indices = new int[count];
            int offset = 0;
            foreach (int[] submesh in submeshes)
                foreach (int value in submesh)
                    indices[offset++] = value;
            mesh.SetIndexBufferData(indices, 0, 0, count, MeshUpdateFlags.DontRecalculateBounds);
        }
        mesh.SetSubMeshes(sections);
    }
}

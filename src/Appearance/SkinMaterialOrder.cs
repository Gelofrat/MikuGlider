using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider;

internal static class SkinMaterialOrder
{
    internal static void Normalize(List<Material> materials, Texture face, Texture body)
    {
        if (materials == null || materials.Count != 2 || face == null || body == null)
            return;

        Material first = materials[0];
        Material second = materials[1];
        if (first == null || second == null ||
            first.GetTexture("_BaseMap") != body || second.GetTexture("_BaseMap") != face)
            return;

        materials[0] = second;
        materials[1] = first;
    }
}

using UnityEngine;

namespace MikuGlider;

// Keep a regular renderer on the menu skeleton so the UI camera sees the mask
// with the character's final transform, including while the menu is paused.
internal sealed class MaskPreviewSurface
{
    private MeshFilter filter;
    private MeshRenderer renderer;
    private bool shown;

    internal void Show(Transform socket, Mesh mesh, Material material, Vector3 offset, Quaternion rotation,
        Vector3 scale, int layer, uint renderingLayers)
    {
        if (renderer == null)
        {
            var node = new GameObject("Porcelain Mask preview");
            filter = node.AddComponent<MeshFilter>();
            renderer = node.AddComponent<MeshRenderer>();
        }
        Transform host = renderer.transform;
        if (host.parent != socket) host.SetParent(socket, false);
        host.SetLocalPositionAndRotation(offset, rotation);
        host.localScale = scale;
        host.gameObject.layer = layer;
        renderer.renderingLayerMask = renderingLayers;
        if (filter.sharedMesh != mesh) filter.sharedMesh = mesh;
        if (renderer.sharedMaterial != material) renderer.sharedMaterial = material;
        renderer.enabled = true;
        shown = true;
    }

    internal void EndFrame()
    {
        if (!shown) Hide();
        shown = false;
    }

    internal void Hide()
    {
        if (renderer != null) renderer.enabled = false;
        shown = false;
    }
}

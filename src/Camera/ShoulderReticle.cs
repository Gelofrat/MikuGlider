using Pigeon.Movement;
using UnityEngine;
using UnityEngine.UI;

namespace MikuGlider;

internal sealed class ShoulderReticle
{
    private GameObject root;
    private PlayerLook owner;

    internal void Update(PlayerLook look, bool visible)
    {
        if (!visible || look == null || look.StaticReticle == null)
        {
            Hide();
            return;
        }

        if (root == null || owner != look)
            Create(look);

        if (look.Reticle != null)
        {
            look.Reticle.anchoredPosition = Vector2.zero;
            look.Reticle.localRotation = Quaternion.identity;
        }
    }

    internal void Hide()
    {
        if (root != null)
        {
            root.SetActive(false);
            Object.Destroy(root);
        }

        root = null;
        owner = null;
    }

    private void Create(PlayerLook look)
    {
        Hide();
        owner = look;
        root = new GameObject("MikuGlider shoulder reticle", typeof(RectTransform));
        var rect = (RectTransform)root.transform;
        rect.SetParent(look.StaticReticle, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(32f, 32f);
        AddMark(rect, Vector2.zero, new Vector2(3f, 3f));
        AddMark(rect, new Vector2(-9f, 0f), new Vector2(5f, 1.5f));
        AddMark(rect, new Vector2(9f, 0f), new Vector2(5f, 1.5f));
        AddMark(rect, new Vector2(0f, -9f), new Vector2(1.5f, 5f));
        AddMark(rect, new Vector2(0f, 9f), new Vector2(1.5f, 5f));
    }

    private static void AddMark(RectTransform parent, Vector2 position, Vector2 size)
    {
        AddRectangle(parent, position, size + new Vector2(2f, 2f), new Color(0f, 0f, 0f, 0.9f));
        AddRectangle(parent, position, size, Color.white);
    }

    private static void AddRectangle(RectTransform parent, Vector2 position, Vector2 size, Color color)
    {
        var element = new GameObject("Reticle mark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = (RectTransform)element.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = element.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }
}

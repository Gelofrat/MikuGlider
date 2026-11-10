using System;
using UnityEngine;

namespace MikuGlider;

internal static class SkinMaterialFactory
{
    private static Shader shader;

    internal static Shader Shader
    {
        get
        {
            if (shader == null)
            {
                shader = UnityEngine.Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("The installed game has no supported URP Unlit shader.");
            }

            return shader;
        }
    }

    internal static Material Create(Texture2D texture, bool persistent = false)
    {
        var material = new Material(Shader);
        try
        {
            if (persistent)
                material.hideFlags = HideFlags.DontUnloadUnusedAsset;

            Apply(material, texture);
            return material;
        }
        catch
        {
            UnityEngine.Object.Destroy(material);
            throw;
        }
    }

    internal static void Apply(Material material, Texture2D texture)
    {
        material.name = texture.name;
        material.shaderKeywords = Array.Empty<string>();
        material.SetTexture("_BaseMap", texture);
        material.SetTexture("_MainTex", texture);
        material.SetTextureScale("_BaseMap", Vector2.one);
        material.SetTextureOffset("_BaseMap", Vector2.zero);
        material.SetTextureScale("_MainTex", Vector2.one);
        material.SetTextureOffset("_MainTex", Vector2.zero);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_BlendOp", 0f);
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", 0.35f);
        material.SetFloat("_Cull", 0f);
        material.SetFloat("_ZWrite", 1f);
        material.SetFloat("_SrcBlend", 1f);
        material.SetFloat("_DstBlend", 0f);
        material.SetFloat("_SrcBlendAlpha", 1f);
        material.SetFloat("_DstBlendAlpha", 0f);
        material.EnableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = 2450;
    }
}

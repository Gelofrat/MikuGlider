using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace MikuGlider;

internal sealed class SkinAssets : IDisposable
{
    private readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    private readonly SkinVariantDefinition variant;
    private bool disposed;

    internal SkinModelDefinition ModelDefinition { get; private set; }
    internal HairAnimationData Hair { get; private set; }

    private SkinAssets(SkinVariantDefinition variant)
    {
        this.variant = variant;
    }

    internal static SkinAssets Load(SkinVariantDefinition variant)
    {
        var assets = new SkinAssets(variant);
        var resources = new EmbeddedResources(variant.ResourcePrefix);
        try
        {
            assets.ModelDefinition = SkinModelDefinition.Parse(resources.ReadText("Definition", 128 * 1024));
            using (Stream stream = resources.Open("Model", MeshResourceReader.MaximumBytes))
            {
                foreach (MeshData data in MeshResourceReader.Read(stream))
                    assets.CreateMesh(data);
            }

            assets.LoadTextures(resources);
            if (variant.AnimatedHair)
            {
                using (Stream stream = resources.Open("Hair", HairResourceReader.MaximumBytes))
                    assets.Hair = HairResourceReader.Read(stream);

                assets.ValidateHairMeshes();
            }

            return assets;
        }
        catch
        {
            assets.Dispose();
            throw;
        }
    }

    internal Mesh GetMesh(string name)
    {
        ThrowIfDisposed();
        if (!meshes.TryGetValue(name, out Mesh mesh))
            throw new InvalidDataException("Missing mesh " + name + " in skin " + variant.Key + ".");

        return mesh;
    }

    internal Texture2D GetTexture(string name)
    {
        ThrowIfDisposed();
        if (!textures.TryGetValue(name, out Texture2D texture))
            throw new InvalidDataException("Missing texture " + name + " in skin " + variant.Key + ".");

        return texture;
    }

    internal bool TryGetMesh(string name, out Mesh mesh)
    {
        ThrowIfDisposed();
        return meshes.TryGetValue(name, out mesh);
    }

    private void CreateMesh(MeshData data)
    {
        var mesh = new Mesh
        {
            name = variant.Key + "." + data.Name,
            hideFlags = HideFlags.DontUnloadUnusedAsset
        };
        meshes.Add(data.Name, mesh);
        mesh.indexFormat = data.Vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.vertices = data.Vertices;
        mesh.normals = data.Normals;
        mesh.uv = data.UV;
        if (data.Weights != null)
        {
            mesh.bindposes = data.BindPoses;
            mesh.boneWeights = data.Weights;
        }

        MeshIndices.Assign(mesh, data.Submeshes);

        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        mesh.UploadMeshData(false);
    }

    private void LoadTextures(EmbeddedResources resources)
    {
        long totalPixels = 0;
        LoadTexture(resources, "body-albedo", "BodyAlbedo", false, ref totalPixels);
        LoadTexture(resources, "body-specular", "BodySpecular", true, ref totalPixels);
        LoadTexture(resources, "face-albedo", "FaceAlbedo", false, ref totalPixels);
        LoadTexture(resources, "face-specular", "FaceSpecular", true, ref totalPixels);
        LoadTexture(resources, "icon", "Icon", false, ref totalPixels);
    }

    private void ValidateHairMeshes()
    {
        foreach (string name in new[] { "hair-l", "hair-r" })
        {
            if (GetMesh(name).bindposes.Length != Hair.Parents.Length)
                throw new InvalidDataException("Hair mesh skeleton does not match its animation rig: " + name);
        }
    }

    private void LoadTexture(EmbeddedResources resources, string name, string suffix, bool linear, ref long totalPixels)
    {
        Texture2D texture = TextureResourceReader.Read(resources, name, suffix, linear, ref totalPixels);
        texture.name = variant.Key + "." + name;
        textures.Add(name, texture);
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(SkinAssets));
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        foreach (Mesh mesh in meshes.Values)
            UnityEngine.Object.Destroy(mesh);
        foreach (Texture2D texture in textures.Values)
            UnityEngine.Object.Destroy(texture);

        meshes.Clear();
        textures.Clear();
    }
}

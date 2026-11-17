using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Horse.MikuPhysicsLab
{
    internal static class LabCapture
    {
        private static readonly bool CuffTrimOnly = Environment.GetCommandLineArgs().Contains("-labCuffTrimOnly");

        [Serializable] private sealed class CameraDump
        {
            public int unityFrame;
            public Vector3 position;
            public Quaternion rotation;
            public float fieldOfView;
            public float aspect;
            public float nearClipPlane;
            public float farClipPlane;
            public int width;
            public int height;
            public Matrix4x4 worldToCamera;
            public Matrix4x4 projection;
        }

        [Serializable] private sealed class CuffDump
        {
            public string renderer;
            public string mesh;
            public string material;
            public string shader;
            public string texture;
            public int textureWidth;
            public int textureHeight;
            public Vector2 textureScale;
            public Vector2 textureOffset;
            public Matrix4x4 localToWorld;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector2[] uv;
            public int[] triangles;
            public int[] trimTriangles;
            public float[] blendWeights;
        }

        internal static void Save(Camera camera, string path)
        {
            SaveImage(camera, path, false);
            if (CuffTrimOnly) SaveCuffTrim(camera, path);
        }

        private static void SaveImage(Camera camera, string path, bool forceRender)
        {
            bool ownsTarget = camera.targetTexture == null;
            RenderTexture target = camera.targetTexture ?? new RenderTexture(1200, 1000, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                if (ownsTarget || forceRender)
                {
                    camera.targetTexture = target;
                    LabRenderClock clock = camera.GetComponent<LabRenderClock>();
                    if (clock != null)
                        clock.RenderManual();
                    else
                        camera.Render();
                }
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                File.WriteAllText(Path.ChangeExtension(path, ".camera.json"), JsonUtility.ToJson(new CameraDump
                {
                    unityFrame = Time.frameCount, position = camera.transform.position, rotation = camera.transform.rotation,
                    fieldOfView = camera.fieldOfView, aspect = camera.aspect,
                    nearClipPlane = camera.nearClipPlane, farClipPlane = camera.farClipPlane,
                    width = target.width, height = target.height,
                    worldToCamera = camera.worldToCameraMatrix, projection = camera.projectionMatrix
                }, true));
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (ownsTarget)
                {
                    target.Release();
                    Object.Destroy(target);
                }
                Object.Destroy(pixels);
            }
        }

        private static void SaveCuffTrim(Camera camera, string path)
        {
            SkinnedMeshRenderer[] renderers = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            foreach (SkinnedMeshRenderer target in renderers)
            {
                Mesh source = target.sharedMesh;
                if (!target.enabled || source == null ||
                    !(source.name.StartsWith("v2.", StringComparison.Ordinal) ||
                    source.name.StartsWith("MikuGliderV2.", StringComparison.Ordinal)) ||
                    !source.name.Contains(".sleeves")) continue;
                var baked = new Mesh();
                var visible = renderers.Select(renderer => renderer.enabled).ToArray();
                var blendWeights = Enumerable.Range(0, source.blendShapeCount)
                    .Select(target.GetBlendShapeWeight).ToArray();
                Mesh trim = null;
                try
                {
                    target.BakeMesh(baked);
                    Vector2[] uv = source.uv;
                    int[] triangles = source.triangles;
                    var selected = new List<int>();
                    for (int index = 0; index < triangles.Length; index += 3)
                        if (Trim(uv[triangles[index]]) && Trim(uv[triangles[index + 1]]) && Trim(uv[triangles[index + 2]]))
                        {
                            selected.Add(triangles[index]);
                            selected.Add(triangles[index + 1]);
                            selected.Add(triangles[index + 2]);
                        }
                    Material material = target.sharedMaterial;
                    Texture texture = material != null ? material.mainTexture : null;
                    var dump = new CuffDump
                    {
                        renderer = target.name, mesh = source.name,
                        material = material != null ? material.name : null,
                        shader = material != null && material.shader != null ? material.shader.name : null,
                        texture = texture != null ? texture.name : null,
                        textureWidth = texture != null ? texture.width : 0,
                        textureHeight = texture != null ? texture.height : 0,
                        textureScale = material != null ? material.mainTextureScale : Vector2.zero,
                        textureOffset = material != null ? material.mainTextureOffset : Vector2.zero,
                        localToWorld = target.localToWorldMatrix,
                        vertices = baked.vertices, normals = baked.normals, uv = uv,
                        triangles = triangles, trimTriangles = selected.ToArray(), blendWeights = blendWeights
                    };
                    string prefix = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
                    File.WriteAllText(prefix + "-cuff.json", JsonUtility.ToJson(dump));
                    trim = Object.Instantiate(source);
                    trim.subMeshCount = 1;
                    trim.SetTriangles(dump.trimTriangles, 0, false);
                    foreach (SkinnedMeshRenderer renderer in renderers) renderer.enabled = renderer == target;
                    target.sharedMesh = trim;
                    for (int index = 0; index < blendWeights.Length; index++)
                        target.SetBlendShapeWeight(index, blendWeights[index]);
                    SaveImage(camera, prefix + "-trim-only.png", true);
                }
                finally
                {
                    target.sharedMesh = source;
                    for (int index = 0; index < blendWeights.Length; index++)
                        target.SetBlendShapeWeight(index, blendWeights[index]);
                    for (int index = 0; index < renderers.Length; index++) renderers[index].enabled = visible[index];
                    Object.Destroy(baked);
                    if (trim != null) Object.Destroy(trim);
                }
            }
        }

        private static bool Trim(Vector2 uv)
        {
            Vector2 pixel = uv * 4096f;
            return pixel.x >= 2055.95f && pixel.x <= 2552.05f &&
                pixel.y >= 2055.95f && pixel.y <= 2552.05f;
        }
    }
}

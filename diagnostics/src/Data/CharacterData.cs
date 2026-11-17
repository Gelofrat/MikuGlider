using System;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal sealed class CharacterFrame
{
    public int unityFrame;
    public double realtime;
    public string phase;
    public string perspective;
    public string skinMesh;
    public string character;
    public string weapon;
    public string motion;
    public Vector3 velocity;
    public float[] playerMatrix;
    public float[] weaponMatrix;
    public float[] leftHandMatrix;
    public AnimationData[] animations;
    public string animationError;
    public RendererFrame[] renderers = Array.Empty<RendererFrame>();
    public CameraData camera;
    public string geometryPath;
    public string error;
    public double captureMilliseconds;
}

internal sealed class AnimationData
{
    public string rig;
    public int layer;
    public string clip;
    public float time;
    public float weight;
    public float speed;
    public bool playing;
}

internal sealed class RendererFrame
{
    public string role;
    public string binding;
    public string path;
    public string mesh;
    public bool enabled;
    public bool active;
    public bool forceRenderingOff;
    public bool cameraLayerVisible;
    public bool inFrustum;
    public string frustumSource = "camera.cullingMatrix";
    public bool? inRenderFrustum;
    public int layer;
    public uint renderingLayerMask;
    public string projectionSource;
    public int vertexCount;
    public float[] localToWorld;
    public float[] boneMatrices;
    public float[] sourceBoneMatrices;
    public float[] blendShapes;
    public string geometryStatus;
    public string error;
}

internal sealed class RendererAsset
{
    public string binding;
    public string role;
    public string path;
    public string mesh;
    public string quality;
    public string[] bonePaths;
    public int[] boneParents;
    public float[] bindPoses;
    public int[] weightIndices;
    public float[] weights;
    public Vector3[] vertices;
    public Vector3[] normals;
    public Vector2[] uv;
    public int[][] submeshes;
    public MaterialData[] materials;
    public string[] blendShapes;
}

internal sealed class MaterialData
{
    public string name;
    public string shader;
    public int renderQueue;
    public string[] keywords;
    public TextureData[] textures;
    public System.Collections.Generic.Dictionary<string, float[]> colors;
    public System.Collections.Generic.Dictionary<string, float> values;
}

internal sealed class TextureData
{
    public string property;
    public string name;
    public int width;
    public int height;
    public string filter;
    public float mipBias;
    public Vector2 scale;
    public Vector2 offset;
}

internal sealed class GeometryCapture
{
    public int sample;
    public int unityFrame;
    public double realtime;
    public string phase;
    public string space = "renderer_local";
    public SurfacePose[] surfaces;
}

internal sealed class SurfacePose
{
    public string binding;
    public string role;
    public float[] localToWorld;
    public Vector3[] vertices;
    public Vector3[] normals;
    public MaterialData[] materials;
}

internal sealed class CaptureEvent
{
    public string utc;
    public double? realtime;
    public int? unityFrame;
    public int? sample;
    public string kind;
    public string source;
    public string message;
}

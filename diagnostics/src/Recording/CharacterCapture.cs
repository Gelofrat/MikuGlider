using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Animancer;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal sealed class CharacterCapture : IDisposable
{
    private readonly DiagnosticOptions options;
    private readonly PropertyInfo runtimeProperty;
    private readonly Type poseCorrectionType;
    private readonly PropertyInfo sourceBonesProperty;
    private readonly Dictionary<string, string> bindings = new Dictionary<string, string>();
    private Mesh baked;
    private double nextGeometry;
    private int bindingId;
    private CaptureSession session;
    private static readonly FieldInfo NativeAnimator = typeof(PlayerAnimation).GetField("animator", BindingFlags.Instance | BindingFlags.NonPublic);

    internal CharacterCapture(DiagnosticOptions options, Assembly source)
    {
        this.options = options;
        runtimeProperty = source.GetType("MikuGlider.MikuGliderPlugin")?
            .GetProperty("Runtime", BindingFlags.Static | BindingFlags.NonPublic);
        poseCorrectionType = source.GetType("MikuGlider.ModelPoseCorrections");
        sourceBonesProperty = poseCorrectionType?.GetProperty("SourceBones", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    internal void Begin(CaptureSession value)
    {
        session = value;
        bindings.Clear();
        bindingId = 0;
        nextGeometry = 0;
    }

    internal CharacterFrame Read(Camera camera, SkirtTarget target, int sample, bool forceGeometry, string phase)
    {
        var watch = Stopwatch.StartNew();
        var frame = new CharacterFrame
        {
            unityFrame = Time.frameCount,
            realtime = Time.realtimeSinceStartupAsDouble,
            phase = phase,
            camera = ReadCamera(camera)
        };
        var renderers = new List<RendererFrame>();
        var surfaces = new List<SurfacePose>();
        var visited = new HashSet<Renderer>();
        bool geometry = options.CaptureMeshes.Value && (forceGeometry || frame.realtime >= nextGeometry);
        int remainingVertices = options.MaximumMeshVertices.Value;
        try
        {
            Player player = Player.LocalPlayer;
            PlayerAnimation animation = player?.Animator;
            frame.perspective = target?.Owner == null && target != null ? "preview" : ReadPerspective();
            frame.character = player?.Character != null ? player.Character.name : null;
            frame.skinMesh = animation?.OverrideThirdPersonMesh != null ? animation.OverrideThirdPersonMesh.name : target?.Body.sharedMesh?.name;
            if (player != null)
            {
                frame.playerMatrix = Matrix(player.transform.localToWorldMatrix);
                frame.velocity = player.Velocity;
                bool flying = player.Gear != null && player.Gear.Any(gear => gear is Wingsuit wingsuit && wingsuit.IsFlying);
                frame.motion = !player.IsAlive ? "dead" : flying ? "flight" : player.Sliding ? "slide" :
                    !player.Grounded ? player.Velocity.y > 0 ? "jump" : "fall" :
                    player.Velocity.sqrMagnitude < .04f ? "idle" : player.IsSprinting ? "run" : "walk";
            }
            if (animation != null)
            {
                var states = new List<AnimationData>();
                var animator = NativeAnimator?.GetValue(animation) as AnimancerComponent;
                // A stale optional animation state must not discard the hands,
                // weapon transforms and geometry for this entire sample.
                try
                {
                    int layerCount = NativeAnimator == null ? 3 : animator != null ? Math.Min(3, animator.Layers.Count) : 0;
                    for (int layer = 0; layer < layerCount; layer++)
                        AddState(states, animation.GetCurrentState(layer), "player", layer);
                    AddState(states, animation.SelfThirdPersonAnimator?.CurrentState, "self_third_person", 0);
                }
                catch (Exception error)
                {
                    frame.animationError = error.GetType().Name + ": " + error.Message;
                }
                frame.animations = states.ToArray();
                frame.weaponMatrix = MatrixOf(animation.WeaponRoot);
                frame.leftHandMatrix = MatrixOf(animation.LeftHand);
                Transform arms = NativeAnimator == null || animator != null && animator.Animator != null ? animation.FirstPersonRoot : null;
                if (arms != null)
                    for (int child = 1; child <= 2 && child < arms.childCount; child++)
                        Add(arms.GetChild(child).GetComponent<SkinnedMeshRenderer>(), child == 1 ? "arm_left" : "arm_right");
                if (animation.WeaponRoot != null)
                {
                    frame.weapon = string.Join(" | ", Enumerable.Range(0, animation.WeaponRoot.childCount)
                        .Select(index => animation.WeaponRoot.GetChild(index)).Where(node => node.gameObject.activeSelf).Select(node => node.name));
                    foreach (Renderer renderer in animation.WeaponRoot.GetComponentsInChildren<Renderer>(false).Take(12))
                        if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                            Add(renderer, "weapon");
                }
            }
        }
        catch (Exception error)
        {
            frame.error = error.GetType().Name + ": " + error.Message;
        }
        if (target != null && target.Alive)
        {
            Add(target.Surface, "skirt_surface");
            Add(target.Coat, "coat");
            Add(target.Sleeves, "sleeves");
            Add(target.Body, "body");
            // The orbit-view weapon is a separate mesh-only copy. Capturing
            // only WeaponRoot records the first-person geometry instead.
            if (target.Host != null)
                foreach (Transform node in target.Host.GetComponentsInChildren<Transform>(false))
                    if (node.name == "MikuThirdPersonWeapon")
                        foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(false))
                            Add(renderer, "weapon_third_person");
        }
        frame.renderers = renderers.ToArray();
        if (geometry)
        {
            nextGeometry = frame.realtime + 1.0 / options.MeshFramesPerSecond.Value;
            if (surfaces.Count > 0)
            {
                string path = "poses/pose-" + sample.ToString("D6") + ".json.gz";
                var capture = new GeometryCapture
                {
                    sample = sample, unityFrame = frame.unityFrame, realtime = frame.realtime,
                    phase = phase, surfaces = surfaces.ToArray()
                };
                if (session.Archive(path, capture, surfaces.Sum(surface => surface.vertices.Length * 96 + 1024)))
                    frame.geometryPath = path;
                else
                    foreach (RendererFrame renderer in renderers.Where(item => item.geometryStatus == "captured"))
                        renderer.geometryStatus = "writer_rejected";
            }
        }
        frame.captureMilliseconds = watch.Elapsed.TotalMilliseconds;
        return frame;

        void Add(Renderer renderer, string role)
        {
            if (renderer == null || !visited.Add(renderer))
                return;
            var item = new RendererFrame { role = role, path = SkirtTarget.PathOf(renderer.transform) };
            renderers.Add(item);
            try
            {
                item.enabled = renderer.enabled;
                item.active = renderer.gameObject.activeInHierarchy;
                item.forceRenderingOff = renderer.forceRenderingOff;
                item.layer = renderer.gameObject.layer;
                item.renderingLayerMask = renderer.renderingLayerMask;
                item.projectionSource = (item.renderingLayerMask & 2) != 0 ?
                    frame.camera?.firstPersonPass != null ? "firstPersonPass" : "firstPersonPass_unavailable" : "camera";
                item.cameraLayerVisible = camera != null && (camera.cullingMask & (1 << item.layer)) != 0;
                item.inFrustum = camera != null && InFrustum(camera.cullingMatrix, renderer.bounds);
                RenderProjectionData pass = frame.camera?.firstPersonPass;
                item.inRenderFrustum = (item.renderingLayerMask & 2) == 0 ? item.inFrustum :
                    pass == null ? (bool?)null : InFrustum(
                        ReadMatrix(pass.projection) * ReadMatrix(pass.worldToCamera), renderer.bounds);
                item.localToWorld = Matrix(renderer.localToWorldMatrix);
                var skinned = renderer as SkinnedMeshRenderer;
                Mesh mesh = skinned != null ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                {
                    item.geometryStatus = "no_mesh";
                    return;
                }
                item.mesh = mesh.name;
                item.vertexCount = mesh.vertexCount;
                if (skinned != null)
                {
                    item.boneMatrices = Matrices(skinned.bones.Select(bone => bone != null ? bone.localToWorldMatrix : Matrix4x4.zero));
                    var correction = poseCorrectionType != null ? skinned.GetComponent(poseCorrectionType) : null;
                    var sources = correction != null ? sourceBonesProperty?.GetValue(correction) as Transform[] : null;
                    if (sources != null)
                        item.sourceBoneMatrices = Matrices(sources.Select(bone => bone != null ? bone.localToWorldMatrix : Matrix4x4.zero));
                    item.blendShapes = Enumerable.Range(0, mesh.blendShapeCount).Select(skinned.GetBlendShapeWeight).ToArray();
                }
                item.geometryStatus = !geometry ? "not_due" : !item.enabled || !item.active || item.forceRenderingOff ? "hidden" :
                    !mesh.isReadable ? "unreadable_mesh" : mesh.vertexCount > remainingVertices ? "vertex_budget" : "pending";
                string key = renderer.GetEntityId() + ":" + mesh.GetEntityId() + ":" + mesh.vertexCount + ":" + mesh.subMeshCount + ":" +
                    string.Join(",", renderer.sharedMaterials.Select(material => material != null ? material.GetEntityId().ToString() : "null"));
                if (skinned != null)
                    key += ":" + string.Join(",", skinned.bones.Select(bone => bone != null ? bone.GetEntityId().ToString() : "null"));
                if (!bindings.TryGetValue(key, out item.binding) && item.geometryStatus == "pending")
                {
                    string identity = (++bindingId).ToString("D5");
                    RendererAsset asset = ReadAsset(renderer, mesh, skinned, identity, role);
                    if (session.Archive("renderers/" + identity + ".json.gz", asset, mesh.vertexCount * 192 + asset.submeshes.Sum(indices => indices.Length) * 8 + 8192))
                    {
                        item.binding = identity;
                        bindings.Add(key, identity);
                    }
                    else
                        item.geometryStatus = "metadata_rejected";
                }
                if (item.geometryStatus != "pending")
                    return;
                remainingVertices -= mesh.vertexCount;
                if (skinned != null)
                {
                    baked ??= new Mesh { name = "Miku diagnostic bake", hideFlags = HideFlags.HideAndDontSave };
                    baked.Clear();
                    skinned.BakeMesh(baked, true);
                    surfaces.Add(new SurfacePose { binding = item.binding, role = role, localToWorld = item.localToWorld, vertices = baked.vertices, normals = baked.normals, materials = renderer.sharedMaterials.Select(ReadMaterial).ToArray() });
                }
                else
                    surfaces.Add(new SurfacePose { binding = item.binding, role = role, localToWorld = item.localToWorld, vertices = mesh.vertices, normals = mesh.normals, materials = renderer.sharedMaterials.Select(ReadMaterial).ToArray() });
                item.geometryStatus = "captured";
            }
            catch (Exception error)
            {
                item.geometryStatus = "error";
                item.error = error.GetType().Name + ": " + error.Message;
            }
        }
    }

    internal static RendererAsset ReadAsset(Renderer renderer, Mesh mesh, SkinnedMeshRenderer skinned, string binding, string role)
    {
        Transform[] bones = skinned != null ? skinned.bones : Array.Empty<Transform>();
        BoneWeight[] weights = mesh.boneWeights;
        return new RendererAsset
        {
            binding = binding, role = role, path = SkirtTarget.PathOf(renderer.transform), mesh = mesh.name,
            quality = skinned != null ? skinned.quality.ToString() : "static",
            vertices = mesh.vertices, normals = mesh.normals, uv = mesh.uv,
            submeshes = Enumerable.Range(0, mesh.subMeshCount).Select(index => mesh.GetTriangles(index)).ToArray(),
            bonePaths = bones.Select(bone => bone != null ? SkirtTarget.PathOf(bone) : null).ToArray(),
            boneParents = bones.Select(bone => bone != null ? Array.IndexOf(bones, bone.parent) : -1).ToArray(),
            bindPoses = Matrices(mesh.bindposes),
            weightIndices = weights.SelectMany(weight => new[] { weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3 }).ToArray(),
            weights = weights.SelectMany(weight => new[] { weight.weight0, weight.weight1, weight.weight2, weight.weight3 }).ToArray(),
            materials = renderer.sharedMaterials.Select(ReadMaterial).ToArray(),
            blendShapes = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray()
        };
    }

    private static MaterialData ReadMaterial(Material material)
    {
        if (material == null)
            return null;
        return new MaterialData
        {
            name = material.name, shader = material.shader != null ? material.shader.name : null,
            renderQueue = material.renderQueue, keywords = material.shaderKeywords,
            colors = new[] { "_Color", "_BaseColor", "_EmissionColor" }.Where(material.HasProperty).ToDictionary(property => property, property =>
            {
                Color color = material.GetColor(property);
                return new[] { color.r, color.g, color.b, color.a };
            }),
            values = new[] { "_Cutoff", "_Glossiness", "_Smoothness", "_Metallic", "_Cull", "_AlphaClip", "_ZWrite" }.Where(material.HasProperty).ToDictionary(property => property, material.GetFloat),
            textures = material.GetTexturePropertyNames().Select(property =>
            {
                Texture texture = material.GetTexture(property);
                return texture == null ? null : new TextureData
                {
                    property = property, name = texture.name, width = texture.width, height = texture.height,
                    filter = texture.filterMode.ToString(), mipBias = texture.mipMapBias,
                    scale = material.GetTextureScale(property), offset = material.GetTextureOffset(property)
                };
            }).Where(texture => texture != null).ToArray()
        };
    }

    private string ReadPerspective()
    {
        object runtime = runtimeProperty?.GetValue(null, null);
        object camera = runtime?.GetType().GetProperty("Camera", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(runtime, null);
        return camera?.GetType().GetProperty("Mode", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(camera, null)?.ToString() ?? "unknown";
    }

    private static bool InFrustum(Matrix4x4 cameraMatrix, Bounds bounds)
    {
        foreach (Plane plane in GeometryUtility.CalculateFrustumPlanes(cameraMatrix))
        {
            Vector3 normal = plane.normal;
            Vector3 extent = bounds.extents;
            float radius = Math.Abs(normal.x) * extent.x + Math.Abs(normal.y) * extent.y + Math.Abs(normal.z) * extent.z;
            if (plane.GetDistanceToPoint(bounds.center) + radius < 0)
                return false;
        }
        return true;
    }

    private static void AddState(List<AnimationData> states, AnimancerState state, string rig, int layer)
    {
        if (state != null)
            states.Add(new AnimationData { rig = rig, layer = layer, clip = state.Clip != null ? state.Clip.name : state.ToString(), time = state.Time, weight = state.Weight, speed = state.Speed, playing = state.IsPlaying });
    }

    internal static CameraData ReadCamera(Camera camera) => camera == null ? null : new CameraData
    {
        name = camera.name, position = camera.transform.position, rotation = camera.transform.rotation,
        fieldOfView = camera.fieldOfView, width = Screen.width, height = Screen.height,
        pixelWidth = camera.pixelWidth, pixelHeight = camera.pixelHeight, cullingMask = camera.cullingMask,
        near = camera.nearClipPlane, far = camera.farClipPlane, orthographic = camera.orthographic,
        targetTexture = camera.targetTexture != null ? camera.targetTexture.name : null,
        worldToCamera = Matrix(camera.worldToCameraMatrix), projection = Matrix(camera.projectionMatrix),
        firstPersonPass = FirstPersonProjectionCapture.Read(camera),
        firstPersonProjectionError = FirstPersonProjectionCapture.Error
    };

    private static float[] MatrixOf(Transform transform) => transform != null ? Matrix(transform.localToWorldMatrix) : null;

    private static Matrix4x4 ReadMatrix(float[] values)
    {
        var matrix = new Matrix4x4();
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                matrix[row, column] = values[row * 4 + column];
        return matrix;
    }

    internal static float[] Matrix(Matrix4x4 matrix)
    {
        var values = new float[16];
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                values[row * 4 + column] = matrix[row, column];
        return values;
    }

    private static float[] Matrices(IEnumerable<Matrix4x4> matrices) => matrices.SelectMany(Matrix).ToArray();

    public void Dispose()
    {
        if (baked != null)
            UnityEngine.Object.Destroy(baked);
        baked = null;
        session = null;
        bindings.Clear();
    }
}

using System;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

[DefaultExecutionOrder(1100)]
internal sealed class SkirtGarmentVisual : MonoBehaviour
{
    private RegisteredSkin skin;
    private SkinnedMeshRenderer body;
    private SkinnedMeshRenderer skirt;
    private SkinnedMeshRenderer surfaceRenderer;
    private SkinnedMeshRenderer coat;
    private Player owner;
    private bool ready;
    private bool failed;
    private Transform root;
    private Material material;
    private Material coatMaterial;
    private int materialIndex;
    private readonly System.Collections.Generic.List<Material> materials = new System.Collections.Generic.List<Material>(4);
    private PoseGarmentRig poseRig;
    private Mesh skirtMesh;
    private Mesh coatMesh;
    private GarmentArmClearance armClearance;
    private SeatedHandClearance seatedHands;
    internal SkinnedMeshRenderer SurfaceRenderer => surfaceRenderer;
    internal bool UsesPoseRig => ready && !failed && poseRig != null;
    internal double PoseUpdateMilliseconds { get; private set; }
    internal double InitializationMilliseconds { get; private set; }

    internal static void Attach(GameObject host, SkinnedMeshRenderer body, int mainIndex,
        RegisteredSkin skin, Player owner)
    {
        var visual = host.GetComponent<SkirtGarmentVisual>() ?? host.AddComponent<SkirtGarmentVisual>();
        bool rebuild = visual.root == null || visual.skin != skin || visual.body != body || visual.root.parent != body.transform;
        visual.skin = skin;
        visual.body = body;
        visual.owner = owner;
        visual.materialIndex = mainIndex;
        if (rebuild) visual.CreateRenderer();
    }

    private void CreateRenderer()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        RemoveRenderer();
        try
        {
            root = new GameObject(skin.Definition.Key + " pose garments").transform;
            root.SetParent(body.transform, false);
            Mesh source = skin.Assets.GetMesh("skirt");
            poseRig = new PoseGarmentRig(body.bones, source);
            skin.Assets.TryGetMesh("coat-flaps", out Mesh overlay);
            skirtMesh = poseRig.Bind(source, false, overlay);
            skirt = CreateGarment(root.gameObject, skirtMesh);
            surfaceRenderer = skirt;
            if (overlay != null)
            {
                var garment = new GameObject(skin.Definition.Key + " coat flaps");
                garment.transform.SetParent(root, false);
                coatMesh = poseRig.Bind(overlay, true);
                coat = CreateGarment(garment, coatMesh);
            }
            armClearance = new GarmentArmClearance(body.bones, source.vertices);
            seatedHands = new SeatedHandClearance(body.bones, poseRig.Bones, skirtMesh, coatMesh);
            ready = true;
            failed = false;
            SynchronizeRenderer();
        }
        catch (Exception error)
        {
            RemoveRenderer();
            failed = true;
            MikuGliderPlugin.Runtime?.Logger.LogError("Pose garments could not be initialized: " + error);
        }
        InitializationMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) *
            1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    private SkinnedMeshRenderer CreateGarment(GameObject host, Mesh mesh)
    {
        var renderer = host.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = mesh;
        renderer.bones = poseRig.Bones;
        renderer.rootBone = body.rootBone;
        renderer.quality = SkinQuality.Bone4;
        renderer.updateWhenOffscreen = false;
        Bounds bounds = body.localBounds;
        bounds.Expand(.6f);
        renderer.localBounds = bounds;
        return renderer;
    }

    private void Update()
    {
        armClearance?.Restore();
        seatedHands?.Restore();
    }

    private void LateUpdate()
    {
        if (!ready || body == null || poseRig == null) return;
        bool visible = skin.OwnsBody(body.sharedMesh) && body.enabled;
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        if (!visible) return;
        SynchronizeRenderer();
        if (!body.enabled || body.forceRenderingOff) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        poseRig.UpdatePose(GliderEquipment.FindWingsuit(owner)?.IsFlying == true);
        if (poseRig.SeatedBlend < .15f)
            armClearance?.Apply(poseRig.Reference.localToWorldMatrix, Time.deltaTime);
        else
            armClearance?.ResetCorrections();
        seatedHands?.Apply(poseRig.Reference, poseRig.SeatedBlend, Time.deltaTime);
        PoseUpdateMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) *
            1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    private void SynchronizeRenderer()
    {
        body.GetSharedMaterials(materials);
        Material next = materials[materialIndex];
        if (material != next)
        {
            material = next;
            skirt.sharedMaterial = material;
            UpdateCoatMaterial();
        }
        CopyVisibility(skirt);
        if (coat != null) CopyVisibility(coat);
    }

    private void CopyVisibility(SkinnedMeshRenderer renderer)
    {
        renderer.enabled = body.enabled;
        renderer.gameObject.layer = body.gameObject.layer;
        renderer.renderingLayerMask = body.renderingLayerMask;
        renderer.forceRenderingOff = body.forceRenderingOff;
        renderer.shadowCastingMode = body.shadowCastingMode;
    }

    private void UpdateCoatMaterial()
    {
        if (coat == null)
            return;
        if (coatMaterial != null)
            Destroy(coatMaterial);
        coatMaterial = null;
        int culling = skin.Assets.ModelDefinition.coatCullMode;
        if (culling == 0)
        {
            coat.sharedMaterial = material;
            return;
        }
        coatMaterial = new Material(material)
        {
            name = material.name + " coat",
            hideFlags = HideFlags.HideAndDontSave
        };
        coatMaterial.SetFloat("_Cull", culling);
        coat.sharedMaterial = coatMaterial;
    }


    private void OnDisable()
    {
        armClearance?.Restore();
        seatedHands?.Restore();
    }

    private void RemoveRenderer()
    {
        ready = false;
        armClearance?.Restore();
        armClearance = null;
        seatedHands?.Restore();
        seatedHands = null;
        if (root != null) { root.gameObject.SetActive(false); Destroy(root.gameObject); }
        poseRig?.Dispose();
        poseRig = null;
        if (coatMaterial != null) Destroy(coatMaterial);
        root = null;
        skirt = null;
        surfaceRenderer = null;
        coat = null;
        skirtMesh = null;
        coatMesh = null;
        coatMaterial = null;
        material = null;
    }

    private void OnDestroy() => RemoveRenderer();
}

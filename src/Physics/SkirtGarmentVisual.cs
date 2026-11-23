using System;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

[DefaultExecutionOrder(1600)]
internal sealed class SkirtGarmentVisual : MonoBehaviour
{
    private RegisteredSkin skin;
    private SkinnedMeshRenderer body;
    private SkinnedMeshRenderer skirt;
    private SkinnedMeshRenderer surfaceRenderer;
    private SkinnedMeshRenderer coat;
    private SkinnedMeshRenderer sleeves;
    private SleevePoseRig sleeveRig;
    private Player owner;
    private bool inventoryPreview;
    private bool ready;
    private bool failed;
    private Transform[] sourceBones;
    private Mesh failedBodyMesh;
    private Transform root;
    private Material material;
    private Material coatMaterial;
    private int materialIndex;
    private readonly System.Collections.Generic.List<Material> materials = new System.Collections.Generic.List<Material>(4);
    private PoseGarmentRig poseRig;
    private Mesh skirtMesh;
    private Mesh coatMesh;
    private GarmentSurfaceClearance skirtSurface;
    private GarmentSurfaceClearance coatSurface;
    private SeatedArmRig seatedArms;
    private bool armsUpdated;
    private readonly SeatedPoseContext seatedContext = new SeatedPoseContext();
    internal SkinnedMeshRenderer SurfaceRenderer => surfaceRenderer;
    internal bool UsesPoseRig => ready && !failed && poseRig != null;
    internal double PoseUpdateMilliseconds { get; private set; }
    internal double InitializationMilliseconds { get; private set; }

    internal static void Attach(GameObject host, SkinnedMeshRenderer body, int mainIndex,
        RegisteredSkin skin, Player owner, bool inventoryPreview = false)
    {
        var visual = host.GetComponent<SkirtGarmentVisual>() ?? host.AddComponent<SkirtGarmentVisual>();
        Transform[] nativeBones = RendererBoneBinding.NativeBones(body);
        bool rebuild = visual.NeedsRebuild(skin, body, nativeBones, mainIndex, inventoryPreview);
        visual.skin = skin;
        visual.body = body;
        visual.owner = owner;
        visual.inventoryPreview = inventoryPreview;
        visual.materialIndex = mainIndex;
        if (rebuild) visual.CreateRenderer(nativeBones);
    }

    private bool NeedsRebuild(RegisteredSkin nextSkin, SkinnedMeshRenderer nextBody, Transform[] nativeBones,
        int nextMaterialIndex, bool nextInventoryPreview)
    {
        if (skin != nextSkin || body != nextBody || inventoryPreview != nextInventoryPreview ||
            !RendererBoneBinding.SameBones(sourceBones, nativeBones))
            return true;

        // Attach also runs every preview frame. Retry a rejected rig when its inputs change.
        if (failed)
            return failedBodyMesh != nextBody.sharedMesh || materialIndex != nextMaterialIndex;

        return root == null || root.parent != nextBody.transform;
    }

    private void CreateRenderer(Transform[] nativeBones)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        RemoveRenderer();
        sourceBones = nativeBones;
        try
        {
            root = new GameObject(skin.Definition.Key + " pose garments").transform;
            root.SetParent(body.transform, false);
            Mesh source = skin.Assets.GetMesh("skirt");
            SkinModelDefinition model = skin.Assets.ModelDefinition;
            poseRig = new PoseGarmentRig(nativeBones, source, body.sharedMesh,
                model.preserveSourceGarmentShape, model.standingHipClearance);
            skin.Assets.TryGetMesh("coat-flaps", out Mesh overlay);
            skirtMesh = poseRig.Bind(source, false, overlay);
            skirt = CreateGarment(root.gameObject, skirtMesh);
            skirt.enabled = false;
            skirtSurface = poseRig.CreateSurface(skirtMesh);
            var surface = new GameObject(skin.Definition.Key + " skirt surface");
            surface.transform.SetParent(root, false);
            surfaceRenderer = CreateGarment(surface, skirtSurface.Mesh);
            if (overlay != null)
            {
                var garment = new GameObject(skin.Definition.Key + " coat flaps");
                garment.transform.SetParent(root, false);
                coatMesh = poseRig.Bind(overlay, true);
                coatSurface = poseRig.CreateSurface(coatMesh, true);
                coat = CreateGarment(garment, coatSurface.Mesh);
            }
            skin.Assets.TryGetMesh("sleeves", out Mesh sleeveMesh);
            seatedArms = new SeatedArmRig(body, nativeBones, poseRig.Bones, skirtSurface.Mesh, coatSurface?.Mesh,
                sleeveMesh, model, skin.Assets.SeatedPose, inventoryPreview ? model.inventoryWaistPose : null);
            if (sleeveMesh != null)
            {
                var garment = new GameObject(skin.Definition.Key + " sleeves");
                garment.transform.SetParent(root, false);
                sleeveRig = new SleevePoseRig(sleeveMesh, seatedArms.Bones, garment.transform);
                sleeves = CreateGarment(garment, sleeveRig.Mesh);
                sleeves.bones = sleeveRig.Bones;
                seatedArms.BindSleeves(sleeveMesh, sleeveRig);
            }
            ModelPoseCorrections.Attach(surfaceRenderer, skin.Assets.Corrections);
            if (coat != null) ModelPoseCorrections.Attach(coat, skin.Assets.Corrections);
            if (sleeves != null) ModelPoseCorrections.Attach(sleeves, skin.Assets.Corrections);
            if (!SynchronizeRenderer()) root.gameObject.SetActive(false);
            failed = false;
            failedBodyMesh = null;
            ready = true;
        }
        catch (Exception error)
        {
            RemoveRenderer();
            failed = true;
            failedBodyMesh = body.sharedMesh;
            MikuGliderPlugin.Runtime?.Logger.LogError("Could not initialize pose garments for " + skin.Definition.Key + ": " + error);
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

    private void LateUpdate() => UpdatePose(Time.deltaTime);

    internal void UpdatePose(float deltaTime)
    {
        if (!ready || body == null || poseRig == null) return;
        bool visible = skin.OwnsBody(body.sharedMesh) && body.enabled && SynchronizeRenderer();
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        if (!visible || body.forceRenderingOff) { ResetArms(); return; }
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        poseRig.UpdatePose(GliderEquipment.FindWingsuit(owner)?.IsFlying == true);
        skirtSurface?.Apply();
        coatSurface?.Apply();
        float seatedWeight = seatedContext.PrepareFrame(owner, body, seatedArms.Playback);
        armsUpdated = true;
        seatedArms.Apply(poseRig.Reference, seatedWeight, deltaTime, poseRig.StandingBlend * (1 - seatedWeight));
        sleeveRig?.UpdatePose();
        PoseUpdateMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) *
            1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    private bool SynchronizeRenderer()
    {
        body.GetSharedMaterials(materials);
        // The preview can assign the body mesh a frame before its material slots.
        if (materialIndex < 0 || materialIndex >= materials.Count || materials[materialIndex] == null)
            return false;
        Material next = materials[materialIndex];
        if (material != next)
        {
            material = next;
            skirt.sharedMaterial = material;
            surfaceRenderer.sharedMaterial = material;
            if (sleeves != null) sleeves.sharedMaterial = material;
            UpdateCoatMaterial();
        }
        CopyVisibility(surfaceRenderer);
        if (coat != null) CopyVisibility(coat);
        if (sleeves != null) CopyVisibility(sleeves);
        return true;
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


    private void RemoveRenderer()
    {
        ready = false;
        armsUpdated = false;
        seatedArms?.Dispose();
        seatedArms = null;
        skirtSurface?.Dispose();
        skirtSurface = null;
        coatSurface?.Dispose();
        coatSurface = null;
        sleeveRig?.Dispose();
        sleeveRig = null;
        if (root != null) { root.gameObject.SetActive(false); Destroy(root.gameObject); }
        poseRig?.Dispose();
        poseRig = null;
        if (coatMaterial != null) Destroy(coatMaterial);
        root = null;
        skirt = null;
        surfaceRenderer = null;
        coat = null;
        sleeves = null;
        skirtMesh = null;
        coatMesh = null;
        coatMaterial = null;
        material = null;
    }

    private void ResetArms()
    {
        PoseUpdateMilliseconds = 0;
        if (!armsUpdated) return;
        seatedArms?.Reset();
        armsUpdated = false;
    }

    private void OnDisable() => ResetArms();
    private void OnDestroy() => RemoveRenderer();
}

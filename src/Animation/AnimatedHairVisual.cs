using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

[DefaultExecutionOrder(1000)]
internal sealed class AnimatedHairVisual : MonoBehaviour
{
    private const float BlendDuration = 0.18f;

    private RegisteredSkin skin;
    private SkinnedMeshRenderer body;
    private Player owner;
    private Transform root;
    private Transform[] bones;
    private SkinnedMeshRenderer[] tails;
    private Quaternion[] blendFrom;
    private Material material;
    private readonly System.Collections.Generic.List<Material> materials = new System.Collections.Generic.List<Material>(4);
    private int materialIndex;
    private HairAnimationState state;
    private float animationStarted;
    private bool wantedFlight;
    private HairSpringSolver spring;
    private CharacterBodyBones bodyBones;
    private Quaternion[] authoredRotations;
    private Quaternion[] authoredWorldRotations;
    private Vector3[] authoredPositions;
    private int[] primaryChildren;
    private bool physicsApplied;

    internal static void Attach(GameObject host, SkinnedMeshRenderer body, Transform head,
        int materialIndex, RegisteredSkin skin, Player owner)
    {
        var visual = host.GetComponent<AnimatedHairVisual>();
        if (visual == null)
            visual = host.AddComponent<AnimatedHairVisual>();

        visual.Bind(body, head, materialIndex, skin, owner);
    }

    private void Bind(SkinnedMeshRenderer renderer, Transform head, int mainIndex,
        RegisteredSkin variant, Player player)
    {
        bool rebuild = root == null || skin != variant || body != renderer || root.parent != head;
        skin = variant;
        body = renderer;
        owner = player;
        materialIndex = mainIndex;
        if (rebuild)
            CreateRig(head);
    }

    private void CreateRig(Transform head)
    {
        RemoveRig();
        root = new GameObject(skin.Definition.Key + " animated hair").transform;
        root.SetParent(head, false);

        HairAnimationData data = skin.Assets.Hair;
        bones = new Transform[data.Parents.Length];
        blendFrom = new Quaternion[bones.Length];
        authoredRotations = new Quaternion[bones.Length];
        authoredWorldRotations = new Quaternion[bones.Length];
        authoredPositions = new Vector3[bones.Length];
        primaryChildren = new int[bones.Length];
        for (int index = 0; index < primaryChildren.Length; index++)
            primaryChildren[index] = -1;
        for (int index = 0; index < bones.Length; index++)
        {
            Transform bone = new GameObject(data.Names[index]).transform;
            bone.SetParent(data.Parents[index] < 0 ? root : bones[data.Parents[index]], false);
            bone.localPosition = data.Positions[index];
            bone.localRotation = data.RestRotations[index];
            bone.localScale = data.Scales[index];
            bones[index] = bone;
            blendFrom[index] = bone.localRotation;
            authoredRotations[index] = bone.localRotation;
            int parent = data.Parents[index];
            if (parent >= 0 && primaryChildren[parent] < 0)
                primaryChildren[parent] = index;
        }

        spring = new HairSpringSolver(data);
        bodyBones = new CharacterBodyBones(body.bones);
        tails = new[] { CreateTail("hair-l"), CreateTail("hair-r") };
        state = HairAnimationState.Idle;
        wantedFlight = false;
        material = null;
        physicsApplied = false;
        animationStarted = Time.time;
    }

    private SkinnedMeshRenderer CreateTail(string meshName)
    {
        var tailObject = new GameObject(skin.Definition.Key + " " + meshName);
        tailObject.transform.SetParent(root, false);
        var tail = tailObject.AddComponent<SkinnedMeshRenderer>();
        tail.sharedMesh = skin.Assets.GetMesh(meshName);
        tail.bones = bones;
        tail.rootBone = root;
        tail.localBounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f));
        tail.quality = SkinQuality.Bone4;
        tail.updateWhenOffscreen = false;
        return tail;
    }

    private void LateUpdate()
    {
        if (root == null || body == null)
            return;

        bool visible = skin.OwnsBody(body.sharedMesh) && body.enabled;
        root.gameObject.SetActive(visible);
        if (!visible)
        {
            spring.Invalidate();
            return;
        }

        SynchronizeRenderers();
        if (physicsApplied)
            RestoreAuthoredPose();
        bool flying = GliderEquipment.FindWingsuit(owner)?.IsFlying == true;
        Animate(flying);
        physicsApplied = !body.forceRenderingOff && Time.deltaTime > 0f && HairSpringSolver.ShouldSimulate(state, flying);
        if (physicsApplied)
        {
            CaptureAuthoredPose();
            SimulateGroundMotion();
        }
        else
            spring.Invalidate();
    }

    private void RestoreAuthoredPose()
    {
        for (int index = 0; index < bones.Length; index++)
            bones[index].localRotation = authoredRotations[index];
    }

    private void CaptureAuthoredPose()
    {
        for (int index = 0; index < bones.Length; index++)
        {
            authoredRotations[index] = bones[index].localRotation;
            authoredWorldRotations[index] = bones[index].rotation;
            authoredPositions[index] = bones[index].position;
        }
    }

    private void SimulateGroundMotion()
    {
        float scale = CharacterBodyBones.GetScale(body.transform);
        spring.Step(authoredPositions, bodyBones.UpdateHairCollisions(scale), Time.deltaTime, scale);
        for (int index = 0; index < bones.Length; index++)
        {
            int child = primaryChildren[index];
            if (child < 0 || spring.Freedom[child] == 0f)
                continue;

            Vector3 authored = authoredPositions[child] - authoredPositions[index];
            Vector3 simulated = spring.Positions[child] - spring.Positions[index];
            if (authored.sqrMagnitude < 0.000001f || simulated.sqrMagnitude < 0.000001f)
                continue;

            Quaternion offset = Quaternion.FromToRotation(authored, simulated);
            float angleLimit = Mathf.Lerp(7f, 42f, spring.Freedom[child]);
            offset = Quaternion.RotateTowards(Quaternion.identity, offset, angleLimit);
            bones[index].rotation = offset * authoredWorldRotations[index];
        }
    }

    private void SynchronizeRenderers()
    {
        body.GetSharedMaterials(materials);
        Material nextMaterial = materials[materialIndex];
        if (material != nextMaterial)
        {
            material = nextMaterial;
            foreach (SkinnedMeshRenderer tail in tails)
                tail.sharedMaterial = material;
        }

        foreach (SkinnedMeshRenderer tail in tails)
        {
            tail.gameObject.layer = body.gameObject.layer;
            tail.renderingLayerMask = body.renderingLayerMask;
            tail.forceRenderingOff = body.forceRenderingOff;
            tail.shadowCastingMode = body.shadowCastingMode;
        }
    }

    private void Animate(bool flying)
    {
        if (flying != wantedFlight)
        {
            wantedFlight = flying;
            TransitionTo(flying ? HairAnimationState.Unfolding : HairAnimationState.Folding);
        }

        float elapsed = Time.time - animationStarted;
        HairAnimationClip animation = skin.Assets.Hair.GetClip(state);
        if (elapsed >= animation.Duration &&
            (state == HairAnimationState.Unfolding || state == HairAnimationState.Folding))
        {
            TransitionTo(state == HairAnimationState.Unfolding ? HairAnimationState.Gliding : HairAnimationState.Idle);
            elapsed = 0f;
            animation = skin.Assets.Hair.GetClip(state);
        }

        float blend = Mathf.SmoothStep(0f, 1f, elapsed / BlendDuration);
        bool loop = state == HairAnimationState.Idle || state == HairAnimationState.Gliding;
        for (int index = 0; index < bones.Length; index++)
        {
            Quaternion target = animation.Sample(elapsed, index, loop);
            bones[index].localRotation = Quaternion.Slerp(blendFrom[index], target, blend);
        }
    }

    private void TransitionTo(HairAnimationState nextState)
    {
        for (int index = 0; index < bones.Length; index++)
            blendFrom[index] = bones[index].localRotation;

        state = nextState;
        animationStarted = Time.time;
    }

    private void RemoveRig()
    {
        if (root == null)
            return;

        root.gameObject.SetActive(false);
        Destroy(root.gameObject);
        root = null;
    }

    private void OnDestroy()
    {
        RemoveRig();
    }
}

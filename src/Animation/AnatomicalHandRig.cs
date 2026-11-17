using System;
using UnityEngine;

namespace MikuGlider;

// Evaluated after the native animation graph, never written back to it.
internal sealed class AnatomicalHandRig : IDisposable
{
    private readonly HandRigDefinition definition;
    private readonly Transform[] source, output;
    private readonly Transform[] controls = new Transform[28];
    private readonly Matrix4x4[] skinOffsets = new Matrix4x4[28], result = new Matrix4x4[28];
    private readonly Quaternion[] seed = new Quaternion[28];
    private readonly Vector3[] axes = new Vector3[28];
    private Transform[] anchors;
    private Transform root;
    private int active = -1;
    private float weight;
    private float contactWeight;
    private readonly float[] fingerWeights = new float[5], desiredFingers = new float[5];
    private readonly int[] driverCounts = new int[5];
    private bool initialized;
    internal Transform[] Controls => controls;
    internal float Weight => weight;
    internal float ContactWeight => contactWeight;
    internal float ContactError { get; private set; }

    internal AnatomicalHandRig(HandRigDefinition definition, Transform[] source, Transform[] output)
    {
        this.definition = definition; this.source = source; this.output = output;
        root = new GameObject("Miku anatomical hand controls").transform;
        root.gameObject.hideFlags = HideFlags.DontSave;
        root.SetParent(source[0].parent, false);
        for (int i = 0; i < 28; i++)
        {
            var node = new GameObject("Anatomy " + source[i].name).transform;
            node.gameObject.hideFlags = HideFlags.DontSave;
            int parent = Array.IndexOf(source, source[i].parent);
            node.SetParent(parent >= 0 && parent < i ? controls[parent] : root, false);
            controls[i] = node; skinOffsets[i] = Matrix4x4.identity;
        }
        foreach (var j in definition.joints)
        {
            skinOffsets[j.bone] = Matrix4x4.TRS(j.skinPosition, j.skinRotation, Vector3.one);
            axes[j.bone] = j.axisMap * Vector3.forward;
        }
        anchors = new Transform[definition.grips.Length];
    }

    internal void ResolveAnchors(Transform weaponRoot, string prefix)
    {
        for (int i = 0; i < anchors.Length; i++)
        {
            anchors[i] = weaponRoot == null ? null : weaponRoot.Find(definition.grips[i].path);
            if (anchors[i] != null || weaponRoot == null || string.IsNullOrEmpty(prefix)) continue;
            string path = definition.grips[i].path;
            string leaf = path.Substring(path.LastIndexOf('/') + 1);
            foreach (var child in weaponRoot.GetComponentsInChildren<Transform>(true))
                if (child.name == prefix + leaf) { anchors[i] = child; break; }
        }
    }

    internal void Evaluate(Func<string, bool> matchesWeapon, float[] shapes, float deltaTime)
    {
        for (int i = 0; i < 28; i++)
        {
            controls[i].localPosition = source[i].localPosition;
            controls[i].localRotation = source[i].localRotation;
            controls[i].localScale = source[i].localScale;
        }
        foreach (var j in definition.joints)
        {
            controls[j.bone].localPosition = j.position;
            controls[j.bone].localRotation = HandKinematics.Retarget(j, source[j.bone].localRotation);
        }
        int selected = -1;
        for (int i = 0; i < definition.grips.Length; i++)
        {
            if (definition.grips[i].skinPose >= 0) shapes[definition.grips[i].skinPose] = 0;
            if (anchors[i] != null && anchors[i].gameObject.activeInHierarchy && matchesWeapon(definition.grips[i].weapon)) selected = i;
        }
        ContactError = 0;
        if (selected < 0)
        {
            // Partial profile migration must not move the joints of unrelated
            // weapons. ModelPoseCorrections has already copied their native pose.
            Reset();
            return;
        }
        else
        {
            HandGrip grip = definition.grips[selected]; Transform anchor = anchors[selected];
            Vector3 palmTarget = anchor.TransformPoint(grip.position);
            float distance = Vector3.Distance(source[3].position, palmTarget) / Mathf.Max(.0001f, source[3].lossyScale.x);
            float release = Mathf.InverseLerp(grip.innerDistance, grip.outerDistance, distance);
            Array.Clear(desiredFingers,0,5);Array.Clear(driverCounts,0,5);
            foreach (var driver in grip.intent)
            {
                int finger = driver.bone >= 24 ? 4 : (driver.bone-4)/5;
                float curl = HandKinematics.Curl(definition.joints[driver.bone-4].sourceRotation,
                    driver.rotation, source[driver.bone].localRotation, driver.axis);
                desiredFingers[finger] += curl;driverCounts[finger]++;
            }
            float blend = !initialized || active != selected || deltaTime <= 0 ? 1 : 1-Mathf.Exp(-Mathf.Min(deltaTime, .1f)/grip.blendTime);
            weight = 0;
            float holdIntent = 0;
            for (int i = 0; i < 5; i++)
            {
                desiredFingers[i] /= Mathf.Max(1,driverCounts[i]);
                fingerWeights[i] = Mathf.Lerp(fingerWeights[i], desiredFingers[i], blend);
                weight += fingerWeights[i]/5;
                holdIntent = Mathf.Max(holdIntent, desiredFingers[i]);
            }
            // Letting go of the handle must not change the hand's anatomy or
            // open fingers that the native animation is still holding closed.
            float desiredContact = holdIntent * (1-release*release*(3-2*release));
            // Native motion already blends the target. Lagging the contact lock
            // lets a closed hand trail behind a moving handle through its surface.
            contactWeight = desiredContact;
            initialized = true; active = selected;
            controls[3].localRotation = Quaternion.Slerp(controls[3].localRotation, grip.closed[0], contactWeight);
            for (int i = 4; i < 28; i++)
            {
                // Retarget motion relative to the fitted grasp in calibrated
                // joint frames. Curl controls contact, not the shape of the hand.
                Quaternion axisMap = grip.axes[i-3];
                controls[i].localRotation = grip.closed[i-3] * axisMap * Quaternion.Inverse(grip.reference[i-3]) *
                    source[i].localRotation * Quaternion.Inverse(axisMap);
            }
            Quaternion palmRotation = Quaternion.Slerp(controls[3].rotation, anchor.rotation*grip.rotation, contactWeight);
            Vector3 palmPosition = Vector3.Lerp(controls[3].position, palmTarget, contactWeight);
            Quaternion wristRotation = palmRotation * Quaternion.Inverse(controls[3].localRotation);
            Vector3 offset = Vector3.Scale(controls[3].localPosition, controls[2].lossyScale);
            if (contactWeight > 1e-6f)
            {
                HandKinematics.SolveArm(controls[0], controls[1], controls[2], palmPosition-wristRotation*offset, source[1].position);
                controls[2].rotation = wristRotation;
            }
            for (int i = 4; i < 28; i++) seed[i] = controls[i].localRotation;
            foreach (var contact in grip.contacts)
            {
                for (int i = contact.root; i <= contact.tip; i++) axes[i] = definition.joints[i-4].axisMap * contact.axis;
                Vector3 target = anchor.TransformPoint(contact.target);
                float contactStrength = contactWeight*fingerWeights[contact.tip==26?4:(contact.tip-4)/5];
                HandKinematics.SolveFinger(controls, contact, target, seed, axes, contactStrength);
                if (contactStrength > .99f)
                    ContactError = Mathf.Max(ContactError, Vector3.Distance(controls[contact.tip].TransformPoint(contact.localPoint), target));
            }
            // The bind-space grip calibration stays fixed through movement;
            // scaling it with a pose-template weight distorts nails and phalanges.
            if (grip.skinPose >= 0) shapes[grip.skinPose] = 1;
        }
        // Snapshot before changing parents of other output bones. These are
        // bind-frame adapters, not joint pivots; controls hold the actual joints.
        for (int i = 0; i < 28; i++) result[i] = controls[i].localToWorldMatrix * skinOffsets[i];
        for (int i = 0; i < 28; i++) output[i].SetPositionAndRotation(result[i].GetColumn(3), result[i].rotation);
    }

    internal void Reset() { active = -1; weight = 0; contactWeight = 0; initialized = false; Array.Clear(fingerWeights,0,5); }
    public void Dispose()
    {
        if (root == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(root.gameObject);
        else UnityEngine.Object.DestroyImmediate(root.gameObject);
        root = null;
    }
}

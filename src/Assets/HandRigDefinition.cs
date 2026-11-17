using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MikuGlider;

// Authoring data in model units. Calibration is independent of animation clips.
[Serializable]
internal sealed class HandRigDefinition
{
    public HandJoint[] joints = Array.Empty<HandJoint>();
    public HandGrip[] grips = Array.Empty<HandGrip>();

    internal void Validate(int boneCount, PoseShapeEdit[] shapes)
    {
        if (boneCount < 28 || joints == null || joints.Length != 24 || grips == null || grips.Length == 0 || grips.Length > 12)
            throw new InvalidDataException("Invalid anatomical hand definition.");
        foreach (var pose in shapes)
            if (pose.boneRotations.Length != 0 || pose.gripAnchor != null)
                throw new InvalidDataException("An anatomical hand cannot also use legacy bone corrections.");
        for (int n = 0; n < joints.Length; n++)
        {
            var j = joints[n];
            if (j == null || j.bone != n + 4 || j.parent < 3 || j.parent >= j.bone || string.IsNullOrEmpty(j.name) ||
                !Point(j.position) || j.position.sqrMagnitude < 1e-12f || !Rotation(j.rotation) || !Rotation(j.sourceRotation) ||
                !Rotation(j.axisMap) || !Point(j.skinPosition) || !Rotation(j.skinRotation))
                throw new InvalidDataException("Invalid anatomical joint calibration.");
        }
        var weapons = new HashSet<string>();
        foreach (var grip in grips)
        {
            if (grip == null || string.IsNullOrEmpty(grip.weapon) || !weapons.Add(grip.weapon) || string.IsNullOrEmpty(grip.path) ||
                !Point(grip.position) || !Rotation(grip.rotation) || grip.closed == null || grip.closed.Length != 25 ||
                grip.reference == null || grip.reference.Length != 25 || grip.axes == null || grip.axes.Length != 25 ||
                grip.intent == null || grip.intent.Length < 5 || grip.intent.Length > 16 || grip.contacts == null || grip.contacts.Length != 5 ||
                !Finite(grip.innerDistance) || !Finite(grip.outerDistance) || grip.innerDistance <= 0 || grip.outerDistance <= grip.innerDistance ||
                grip.outerDistance > .5f || !Finite(grip.blendTime) || grip.blendTime < .02f || grip.blendTime > .5f ||
                grip.skinPose < -1 || grip.skinPose >= shapes.Length)
                throw new InvalidDataException("Invalid anatomical grip.");
            foreach (Quaternion rotation in grip.closed)
                if (!Rotation(rotation)) throw new InvalidDataException("Invalid closed hand rotation.");
            foreach (Quaternion rotation in grip.reference)
                if (!Rotation(rotation)) throw new InvalidDataException("Invalid reference hand rotation.");
            foreach (Quaternion rotation in grip.axes)
                if (!Rotation(rotation)) throw new InvalidDataException("Invalid reference hand axis mapping.");
            var driven = new HashSet<int>();
            var fingers = new HashSet<int>();
            foreach (var driver in grip.intent)
            {
                if (driver == null || driver.bone < 4 || driver.bone > 26 || string.IsNullOrEmpty(driver.name) || !Rotation(driver.rotation) ||
                    !Point(driver.axis) || Mathf.Abs(driver.axis.magnitude-1)>.001f || !driven.Add(driver.bone) ||
                    (driver.bone < 24 && ((driver.bone-4)%5 == 0 || (driver.bone-4)%5 == 4)))
                    throw new InvalidDataException("Invalid grip intent.");
                fingers.Add(driver.bone >= 24 ? 4 : (driver.bone-4)/5);
            }
            if (fingers.Count != 5) throw new InvalidDataException("Grip intent must cover all five fingers.");
            var used = new HashSet<int>();
            foreach (var c in grip.contacts)
            {
                if (c == null || !used.Add(c.tip) || (c.tip != 7 && c.tip != 12 && c.tip != 17 && c.tip != 22 && c.tip != 26) ||
                    c.root != (c.tip == 26 ? 24 : c.tip - 2) || !Point(c.localPoint) || !Point(c.target) || !Point(c.axis) ||
                    Mathf.Abs(c.axis.magnitude - 1) > .001f || !Finite(c.maxAngle) || c.maxAngle <= 0 || c.maxAngle > 25)
                    throw new InvalidDataException("Invalid finger contact chain.");
            }
        }
    }

    private static bool Point(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z) && p.magnitude < 2;
    private static bool Rotation(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w) &&
        Mathf.Abs(q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w - 1) < .001f;
    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}

[Serializable]
internal sealed class HandJoint
{
    public int bone = 0, parent = 0;
    public string name = "";
    public Vector3 position = Vector3.zero;
    public Quaternion rotation = default, sourceRotation = default, axisMap = default;
    // Anatomical control -> original mesh bind frame. Keeps mesh/native rig ownership separate.
    public Vector3 skinPosition = Vector3.zero;
    public Quaternion skinRotation = default;
}

[Serializable]
internal sealed class HandGrip
{
    public string weapon = "", path = "";
    public Vector3 position = Vector3.zero;
    public Quaternion rotation = default;
    public Quaternion[] closed = Array.Empty<Quaternion>(); // Palm 3, then the 24 finger controls.
    public Quaternion[] reference = Array.Empty<Quaternion>(), axes = Array.Empty<Quaternion>();
    public HandCurlDriver[] intent = Array.Empty<HandCurlDriver>();
    public FingerContact[] contacts = Array.Empty<FingerContact>();
    public int skinPose = -1; // Optional fixed bind-space calibration, not an animation key.
    public float innerDistance = .02f, outerDistance = .06f, blendTime = .08f;
}

[Serializable]
internal sealed class FingerContact
{
    public int root = 0, tip = 0;
    public Vector3 localPoint = Vector3.zero, target = Vector3.zero, axis = Vector3.zero;
    // Authoring envelope around the fitted grasp, not a universal medical ROM.
    public float maxAngle = 12;
}

[Serializable]
internal sealed class HandCurlDriver
{
    public int bone = 0;
    public string name = "";
    public Quaternion rotation = default;
    public Vector3 axis = Vector3.forward;
}

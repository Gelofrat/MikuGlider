using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    [Serializable] public sealed class LabCaseResult
    {
        public string variant;
        public string actualVariant;
        public string motion;
        public string label;
        public string rig;
        public string category;
        public string source;
        public string baseMotion;
        public string weapon;
        public int weaponMeshes;
        public bool additive;
        public int frameRate;
        public float expectedSeconds;
        public float simulatedSeconds;
        public float normalizedCoverage;
        public bool completeClip;
        public int matchedBindings;
        public string[] missingPaths;
        public bool inferredSettings;
        public int samples;
        public int untrustedSamples;
        public int delayedSamples;
        public int invalidVertices;
        public int invalidBones;
        public int errorEvents;
        public float maximumAnchorError;
        public float mappingError;
        public float maximumDisplacement;
        public float maximumProxyPenetration;
        public int bodyIntersectionCandidates;
        public int legIntersectionCandidates;
        public int freeSkirtLegIntersectionCandidates;
        public int coatIntersectionCandidates;
        public int selfIntersectionCandidates;
        public float maximumGuideStretch;
        public float maximumGuideStretch95;
        public float maximumRenderedStretch;
        public float maximumRenderedStretch95;
        public float maximumLengthStretch;
        public float maximumSeatedBlend;
        public float maximumBoneAngle;
        public float maximumHairBoneAngle;
        public float maximumGlideHairStep;
        public float bodySkinningError;
        public double garmentInitializationMilliseconds;
        public bool passed;
        public bool visualReviewRequired;
        public string screenshot;
        public CombinationSegment[] combinationSegments;
        public string[] transitionScreenshots;
    }

    [Serializable] public sealed class LabTestReport
    {
        public string createdUtc;
        public string unity;
        public string graphics;
        public string suite;
        public string sourceManifest;
        public LabPhysicsParity parity;
        public string garmentMode;
        public string particleSpace;
        public int renderedFrames;
        public int forcedRenderFrames;
        public int automaticRenderPasses;
        public int manualRenderPasses;
        public string[] renderTrace;
        public int catalogConfigurations;
        public int testedConfigurations;
        public string[] untestedConfigurations;
        public string[] errors;
        public LabMeshAuditResult[] meshAudits;
        public LabCaseResult[] cases;
        public bool passed;
        public string limitation;
    }

    [Serializable] internal sealed class LabProgress
    {
        public int completed;
        public int total;
        public string current;
    }

    internal sealed class LabCaseSpec
    {
        internal int Variant;
        internal int Clip;
        internal int Rate;
        internal int Replay = -1;
        internal bool HairCycle;
    }

    public sealed class LabSuite : MonoBehaviour
    {
        private static void DumpHandClearance(string prefix)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            foreach (var visual in UnityEngine.Object.FindObjectsByType<MikuGlider.SkirtGarmentVisual>(FindObjectsSortMode.None))
            {
                var clearance = typeof(MikuGlider.SkirtGarmentVisual).GetField("seatedHands", flags).GetValue(visual);
                if (clearance == null) continue;
                var type = clearance.GetType();
                var lifts = (float[])type.GetField("lifts", flags).GetValue(clearance);
                var arms = (Transform[,])type.GetField("arms", flags).GetValue(clearance);
                var probes = (Transform[,])type.GetField("probes", flags).GetValue(clearance);
                var rig = (MikuGlider.PoseGarmentRig)typeof(MikuGlider.SkirtGarmentVisual).GetField("poseRig",flags).GetValue(visual);
                Debug.Log("HAND_CONTACT " + prefix + " blend=" + rig.SeatedBlend + " lifts=" + string.Join(",",lifts));
                foreach (Transform bone in visual.GetComponentsInChildren<SkinnedMeshRenderer>()[0].bones)
                    if (bone.name.Contains("Knee") || bone.name.Contains("Thigh"))
                        Debug.Log("LAP_BONE " + bone.name + " world=" + bone.position.ToString("F4") + " frame=" + rig.Reference.InverseTransformPoint(bone.position).ToString("F4"));
                for (int side=0;side<2;side++) for(int i=-1;i<3;i++)
                {
                    var contact = i<0?arms[side,2]:probes[side,i];
                    float height = (float)type.GetMethod("SurfaceHeight",flags).Invoke(clearance,new object[]{contact.position,.04f});
                    Debug.Log("HAND_CONTACT " + side + " " + i + " point=" + contact.position.ToString("F4") + " height=" + height);
                }
            }
        }
        private readonly HashSet<string> errors = new HashSet<string>();
        private int errorEvents;
        private readonly List<LabCaseResult> results = new List<LabCaseResult>();
        private string directory;
        private LabController lab;
        private LabRenderClock render;

        internal IEnumerator Run(LabController controller, string mode, bool quit)
        {
            lab = controller;
            lab.Testing = true;
            Application.logMessageReceived += Log;
            directory = Path.Combine(lab.ReportDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + mode + "-" + (LabController.Argument("-labDriver") ?? "playable"));
            Directory.CreateDirectory(directory);
            LabMeshAuditResult[] audits = lab.Variants.Select(LabMeshAudit.Run).ToArray();
            render = lab.View.GetComponent<LabRenderClock>();
            lab.SetSpeed(1);
            lab.Replay = -1;
            if (mode == "hair")
                yield return LabFlightHairChecks.Run(lab.Variants, directory);
            List<LabCaseSpec> specs = null;
            try { specs = SelectCases(mode); }
            catch (Exception error)
            {
                Debug.LogException(error);
                Application.logMessageReceived -= Log;
                lab.Testing = false;
                lab.Ready = false;
                File.WriteAllText(Path.Combine(directory, "failure.txt"), error.ToString());
                if (quit) Application.Quit(2);
            }
            if (specs == null) yield break;
            for (int index = 0; index < specs.Count; index++)
            {
                LabCaseSpec spec = specs[index];
                string title = lab.Variants[spec.Variant].key + " / " + (spec.Replay >= 0 ? lab.Replays[spec.Replay].label : lab.Clips[spec.Clip].label) + " / " + spec.Rate;
                lab.Status = (index + 1) + " / " + specs.Count + " | " + title;
                File.WriteAllText(Path.Combine(directory, "progress.json"), JsonUtility.ToJson(new LabProgress { completed = index, total = specs.Count, current = title }));
                yield return RunCase(spec);
            }
            lab.Ready = false;
            lab.Playing = false;
            lab.Actor?.Dispose();
            lab.Actor = null;
            yield return new WaitForEndOfFrame();
            LabAssetLibrary.Clear();
            yield return new WaitForEndOfFrame();
            if (quit)
                render.Shutdown();
            LabPhysicsParity parity = LabPhysicsProfile.Capture();
            string[] tested = specs.Where(s => s.Replay < 0).Select(s => lab.Clips[s.Clip].key).Distinct().ToArray();
            var report = new LabTestReport
            {
                createdUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                graphics = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType,
                suite = mode, sourceManifest = Resources.Load<TextAsset>("MikuPhysicsLab/source-manifest").text, parity = parity,
                garmentMode = "pose_driven", particleSpace = "skinned_world",
                renderedFrames = render.RenderedFrames, forcedRenderFrames = render.ForcedFrames,
                automaticRenderPasses = render.AutomaticPasses, manualRenderPasses = render.ManualPasses, renderTrace = render.Trace.ToArray(),
                catalogConfigurations = lab.Clips.Length, testedConfigurations = tested.Length,
                untestedConfigurations = lab.Clips.Where(c => !tested.Contains(c.key)).Select(c => c.label).ToArray(),
                errors = errors.ToArray(), meshAudits = audits, cases = results.ToArray(),
                limitation = "Native Unity Playables and shared pose-driven garment code. Combination scenarios use two override layers without masks, matching the inspected third-person layer setup; gameplay state-selection conditions are not executed. Complete clips, native speeds, loop flags and fade settings. Override layers use the native generic rig. Game locomotion forces, IK, gameplay events and full Animancer state selection are not recreated. Intersections are candidates requiring visual review. Inferred clip settings and missing bindings are reported explicitly."
            };
            report.passed = parity.settingsMatch && report.renderedFrames > 0 &&
                errors.Count == 0 && audits.All(a => a.passed) && results.All(c => c.passed);
            File.WriteAllText(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(directory, "index.html"), LabReport.Html(report));
            File.WriteAllText(Path.Combine(directory, "progress.json"), JsonUtility.ToJson(new LabProgress { completed = specs.Count, total = specs.Count, current = "Complete" }));
            Application.logMessageReceived -= Log;
            Time.captureDeltaTime = 0;
            Application.targetFrameRate = 60;
            lab.Testing = false;
            lab.Playing = true;
            lab.View.Yaw = 180;
            lab.View.Pitch = 5;
            if (!quit)
            {
                lab.SelectMotion(Array.FindIndex(lab.Clips, c => c.key == "idle"));
                lab.Ready = true;
            }
            lab.Status = (report.passed ? "Checks PASSED. " : "Checks flagged issues. ") + Path.Combine(directory, "index.html");
            Debug.Log("LAB_SUITE_RESULT " + lab.Status);
            if (quit)
                lab.RequestQuit(report.passed ? 0 : 2);
        }

        private List<LabCaseSpec> SelectCases(string mode)
        {
            var selected = new List<LabCaseSpec>();
            var core = new HashSet<string> { "idle", "walk", "run", "jump", "jumpLoop", "land", "slide", "clamber", "airJump" };
            for (int clip = 0; clip < lab.Clips.Length; clip++)
            {
                ClipEntry entry = lab.Clips[clip];
                bool include = mode == "all" || mode == "full" || (mode == "weapons" && entry.rig == "body" && entry.category == "weapon") ||
                    (mode == "seated" && entry.rig == "body" && (entry.label.StartsWith("Sit ", StringComparison.OrdinalIgnoreCase) ||
                        entry.label == "Glider Sit Idle" || entry.label.StartsWith("Emote transitions / Sit ", StringComparison.OrdinalIgnoreCase))) ||
                    (mode == "combinations" && entry.category == "combination") || (mode == "combosmoke" && new[] { "combo-locomotion", "combo-flight", "combo-weapon-0", "combo-emote-clip-049", "combo-emote-clip-050" }.Contains(entry.key)) || (mode == "arms" && entry.rig == "arms") || (mode == "emotes" && entry.category == "emote") ||
                    (mode == "parity" && core.Contains(entry.key)) || (mode == "gait" && new[] { "idle", "walk", "run" }.Contains(entry.key)) || (mode == "smoke" && (new[] { "idle", "run", "jump", "slide" }.Contains(entry.key) || entry.flight)) ||
                    (mode == "views" && entry.flight);
                if (!include)
                    continue;
                int[] rates = mode == "parity" || mode == "gait" ? new[] { 30, 60, 120 } : new[] { 60 };
                if (mode == "full" && entry.rig == "body" && (core.Contains(entry.key) || entry.category == "crouch" || entry.category == "emote" || entry.flight))
                    rates = new[] { 30, 60, 120 };
                foreach (int rate in rates)
                    for (int variant = 0; variant < lab.Variants.Length; variant++)
                        selected.Add(new LabCaseSpec { Clip = clip, Variant = variant, Rate = rate });
            }
            if (mode == "views")
                for (int variant = 0; variant < lab.Variants.Length; variant++)
                {
                    int replay = Array.FindLastIndex(lab.Replays, r => r.variant == variant);
                    if (replay >= 0)
                        selected.Add(new LabCaseSpec { Variant = variant, Replay = replay, Rate = 60 });
                }
            if (mode == "full" || mode == "all" || mode == "replay")
                for (int replay = 0; replay < lab.Replays.Length; replay++)
                    for (int variant = 0; variant < lab.Variants.Length; variant++)
                        selected.Add(new LabCaseSpec { Variant = variant, Replay = replay, Rate = 60 });
            if (mode == "full" || mode == "all" || mode == "smoke" || mode == "parity" || mode == "hair")
                foreach (int rate in mode == "full" || mode == "parity" || mode == "hair" ? new[] { 30, 60, 120 } : new[] { 60 })
                    for (int variant = 0; variant < lab.Variants.Length; variant++)
                        selected.Add(new LabCaseSpec { Variant = variant, Clip = Array.FindIndex(lab.Clips, c => c.key == "idle"), Rate = rate, HairCycle = true });
            if (selected.Count == 0)
                throw new InvalidOperationException("No animation cases selected: " + mode);
            string filter = LabController.Argument("-labFilter");
            if (filter != null)
                selected = selected.Where(s => s.Replay >= 0 ? lab.Replays[s.Replay].key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 :
                    lab.Clips[s.Clip].label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || lab.Clips[s.Clip].key == filter).ToList();
            string variantKey = LabController.Argument("-labVariant");
            if (variantKey != null)
                selected = selected.Where(s => lab.Variants[s.Variant].key == variantKey).ToList();
            if (selected.Count == 0)
                throw new InvalidOperationException("No animation cases match the selected filters.");
            return selected;
        }

        private IEnumerator RunCase(LabCaseSpec spec)
        {
            Time.captureDeltaTime = 1f / spec.Rate;
            Application.targetFrameRate = -1;
            lab.Playing = false;
            lab.Variant = spec.Variant;
            lab.Elbow = 0;
            lab.Hat = false;
            if (spec.Replay >= 0)
                lab.SelectReplay(spec.Replay, spec.Variant);
            else
                lab.SelectMotion(spec.Clip);
            if (lab.Actor.Metrics != null)
                lab.Actor.Metrics.Deterministic = true;
            for (int frame = 0; frame < spec.Rate; frame++)
            {
                yield return new WaitForEndOfFrame();
                render.EnsureRendered();
            }
            ClipEntry entry = spec.Replay >= 0 ? new ClipEntry { key = "recording-" + lab.Replays[spec.Replay].key,
                label = lab.Replays[spec.Replay].label, rig = "body", category = "recording", matchedBindings = 72 } : lab.Clips[spec.Clip];
            if (spec.HairCycle)
                entry = new ClipEntry { key = "hair-cycle", label = "Idle / unfold / glide / fold", category = "hair", rig = "body", matchedBindings = 72 };
            var result = new LabCaseResult { variant = lab.Variants[spec.Variant].key, motion = entry.key, label = entry.label,
                actualVariant = lab.Actor.Skin.Definition.Key, baseMotion = entry.baseKey,
                weapon = lab.Actor.WeaponKey, weaponMeshes = lab.Actor.WeaponMeshCount, additive = entry.additive,
                rig = entry.rig, category = entry.category, source = entry.source, frameRate = spec.Rate,
                matchedBindings = entry.matchedBindings, missingPaths = entry.missingPaths, inferredSettings = entry.inferredSettings,
                combinationSegments = entry.segments, expectedSeconds = Mathf.Max(1f / spec.Rate, lab.Duration) };
            result.garmentInitializationMilliseconds = lab.Actor.Garment != null ? lab.Actor.Garment.InitializationMilliseconds : 0;
            var hair = lab.Actor.Skin.Assets.Hair;
            float idleEnd = hair.GetClip(MikuGlider.HairAnimationState.Idle).Duration;
            float glideEnd = idleEnd + hair.GetClip(MikuGlider.HairAnimationState.Unfolding).Duration + hair.GetClip(MikuGlider.HairAnimationState.Gliding).Duration;
            if (spec.HairCycle)
                result.expectedSeconds = glideEnd + hair.GetClip(MikuGlider.HairAnimationState.Folding).Duration + .4f;
            var trace = new List<LabFrameMetrics>();
            Transform[] bones = lab.Actor.Rig.Nodes;
            Quaternion[] first = bones.Select(b => b.rotation).ToArray();
            Transform[] hairBones = lab.Actor.HairBones;
            Quaternion[] firstHair = hairBones.Select(b => b.localRotation).ToArray();
            Quaternion[] previousGlideHair = null;
            int beforeErrors = errorEvents;
            int frames = Mathf.CeilToInt((result.expectedSeconds + .2f) * spec.Rate);
            lab.Position = 0;
            lab.Playing = true;
            string prefix = result.variant + "-" + entry.key + "-" + spec.Rate;
            using var poseWriter = LabController.Argument("-labLimbPoses") != null
                ? new LabPoseWriter(lab.Actor, Path.Combine(directory, prefix + ".poses")) : null;
            bool captured = false;
            var transitionShots = new List<string>();
            int transition = 1;
            float nextTransition = entry.segments != null ? entry.segments[0].duration : float.PositiveInfinity;
            for (int frame = 0; frame < frames; frame++)
            {
                if (frame == spec.Rate / 2 && LabController.Argument("-labLateBind") != null && !lab.Actor.Arms)
                    lab.Actor.RecreateGarment();
                yield return new WaitForEndOfFrame();
                render.EnsureRendered();
                result.simulatedSeconds = lab.Position;
                result.normalizedCoverage = spec.Replay >= 0 || spec.HairCycle || entry.segments != null || LabController.Argument("-labDriver") == "sample"
                    ? Mathf.Clamp01(lab.Position / result.expectedSeconds) : lab.Actor.AnimationCoverage;
                if (spec.HairCycle)
                    lab.Flight = lab.Position >= idleEnd && lab.Position < glideEnd;
                poseWriter?.Sample(lab.Position, result.normalizedCoverage);
                if (entry.segments != null && transition < entry.segments.Length && lab.Position >= nextTransition + .05f)
                {
                    string image = prefix + "-transition-" + transition.ToString("D2") + ".png";
                    LabCapture.Save(lab.View.GetComponent<Camera>(), Path.Combine(directory, image));
                    transitionShots.Add(image);
                    nextTransition += entry.segments[transition++].duration;
                }
                if (!captured && result.normalizedCoverage >= .5f)
                {
                    if (lab.Flight)
                        lab.View.Distance = 7.2f;
                    lab.View.Yaw = 145;
                    lab.View.Refresh();
                    result.screenshot = prefix + ".png";
                    if (!lab.Actor.Arms && entry.label.IndexOf("sit", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var bodyBones = new MikuGlider.CharacterBodyBones(lab.Actor.Body.bones);
                        int pelvis = Array.IndexOf(lab.Actor.Body.bones, bodyBones.Pelvis);
                        Matrix4x4 restFrame = bodyBones.Pelvis.localToWorldMatrix * lab.Actor.Body.sharedMesh.bindposes[pelvis];
                        Matrix4x4 inverse = restFrame.inverse;
                        DumpHandClearance(prefix);
                        Debug.Log("LAB_SEATED_POSE " + prefix + " down=" + inverse.MultiplyVector(Vector3.down).normalized +
                            " left=" + inverse.MultiplyVector(bodyBones.LeftKnee.position - bodyBones.LeftThigh.position).normalized +
                            " right=" + inverse.MultiplyVector(bodyBones.RightKnee.position - bodyBones.RightThigh.position).normalized);
                    }
                    LabCapture.Save(lab.View.GetComponent<Camera>(), Path.Combine(directory, result.screenshot));
                    if (entry.label.IndexOf("sit", StringComparison.OrdinalIgnoreCase) >= 0 && !lab.Actor.Arms)
                    {
                        lab.View.AlternateAngle = true;
                        foreach (float yaw in new[] { 90f, 270f, 0f })
                        {
                            lab.View.Yaw = yaw;
                            lab.View.Refresh();
                            render.RenderManual();
                            string side = prefix + "-view-" + yaw + ".png";
                            LabCapture.Save(lab.View.GetComponent<Camera>(), Path.Combine(directory, side));
                            transitionShots.Add(side);
                        }
                        lab.View.AlternateAngle = false;
                        lab.View.Yaw = 145;
                        lab.View.Refresh();
                    }
                    captured = true;
                }
                if (frame % Math.Max(1, spec.Rate / 10) != 0 ||
                    (LabController.Argument("-labLateBind") != null && frame >= spec.Rate / 2 && frame < spec.Rate / 2 + 10))
                    continue;
                result.samples++;
                if (result.samples == 1)
                    result.bodySkinningError = BodySkinningError();
                for (int bone = 0; bone < bones.Length; bone++)
                {
                    if (!MikuGlider.Diagnostics.GeometryMath.Finite(bones[bone].position) || !float.IsFinite(bones[bone].rotation.w))
                        result.invalidBones++;
                    result.maximumBoneAngle = Mathf.Max(result.maximumBoneAngle, Quaternion.Angle(first[bone], bones[bone].rotation));
                }
                bool stableGlide = spec.HairCycle && lab.Position > idleEnd + hair.GetClip(MikuGlider.HairAnimationState.Unfolding).Duration + .2f && lab.Position < glideEnd;
                for (int bone = 0; bone < hairBones.Length; bone++)
                {
                    Quaternion rotation = hairBones[bone].localRotation;
                    result.maximumHairBoneAngle = Mathf.Max(result.maximumHairBoneAngle, Quaternion.Angle(firstHair[bone], rotation));
                    if (stableGlide && previousGlideHair != null)
                        result.maximumGlideHairStep = Mathf.Max(result.maximumGlideHairStep, Quaternion.Angle(previousGlideHair[bone], rotation));
                }
                if (stableGlide)
                    previousGlideHair = hairBones.Select(b => b.localRotation).ToArray();
                if (lab.Actor.Metrics == null)
                    continue;
                LabFrameMetrics value = lab.Actor.Metrics.SampleNow();
                if (value.legCrossings > result.legIntersectionCandidates)
                {
                    lab.View.Yaw = 145;
                    lab.View.Refresh();
                    render.RenderManual();
                    LabCapture.Save(lab.View.GetComponent<Camera>(), Path.Combine(directory, prefix + "-worst-leg.png"));
                }
                result.legIntersectionCandidates = Math.Max(result.legIntersectionCandidates, value.legCrossings);
                if (value.freeSkirtLegCrossings > result.freeSkirtLegIntersectionCandidates)
                {
                    File.WriteAllText(Path.Combine(directory, prefix + "-leg-geometry.json"), lab.Actor.Metrics.ExportGeometry());
                    LabCapture.Save(lab.View.GetComponent<Camera>(), Path.Combine(directory, prefix + "-worst-free-leg.png"));
                }
                result.freeSkirtLegIntersectionCandidates = Math.Max(result.freeSkirtLegIntersectionCandidates, value.freeSkirtLegCrossings);
                trace.Add(value);
                result.mappingError = value.mappingError;
                if (!value.ready || !value.reliable)
                {
                    result.untrustedSamples++;
                    if (result.untrustedSamples == 1 && lab.Actor.Metrics.Snapshot != null)
                        File.WriteAllText(Path.Combine(directory, prefix + "-snapshot.json"), JsonUtility.ToJson(lab.Actor.Metrics.Snapshot));
                }
                if (value.bestAnchorLagFrames > 0 && value.anchorError > .004f && value.previousAnchorError < .004f)
                    result.delayedSamples++;
                result.invalidVertices += value.invalid;
                result.invalidBones += value.invalidBones;
                result.maximumAnchorError = Mathf.Max(result.maximumAnchorError, value.anchorError);
                result.maximumDisplacement = Mathf.Max(result.maximumDisplacement, value.displacement);
                result.maximumProxyPenetration = Mathf.Max(result.maximumProxyPenetration, value.penetration);
                result.bodyIntersectionCandidates = Math.Max(result.bodyIntersectionCandidates, value.bodyCrossings);
                result.coatIntersectionCandidates = Math.Max(result.coatIntersectionCandidates, value.coatCrossings);
                result.selfIntersectionCandidates = Math.Max(result.selfIntersectionCandidates, value.selfCrossings);
                result.maximumGuideStretch = Mathf.Max(result.maximumGuideStretch, value.guideStretch);
                result.maximumGuideStretch95 = Mathf.Max(result.maximumGuideStretch95, value.guideStretch95);
                result.maximumRenderedStretch = Mathf.Max(result.maximumRenderedStretch, value.renderedStretch);
                result.maximumRenderedStretch95 = Mathf.Max(result.maximumRenderedStretch95, value.renderedStretch95);
                result.maximumLengthStretch = Mathf.Max(result.maximumLengthStretch, value.lengthStretch);
                result.maximumSeatedBlend = Mathf.Max(result.maximumSeatedBlend, value.seatedBlend);
            }
            result.completeClip = result.normalizedCoverage >= .9999f;
            result.visualReviewRequired = result.bodyIntersectionCandidates > 0 || result.coatIntersectionCandidates > 0 ||
                result.selfIntersectionCandidates > 0 || result.maximumLengthStretch > 1.08f || result.maximumRenderedStretch95 > 1.2f ||
                result.rig == "arms" || (result.missingPaths?.Length ?? 0) > 0;
            result.errorEvents = errorEvents - beforeErrors;
            result.passed = result.variant == result.actualVariant && result.completeClip && result.matchedBindings > 0 && result.samples > 0 && result.untrustedSamples == 0 &&
                result.invalidVertices == 0 && result.invalidBones == 0 && result.errorEvents == 0 && result.bodySkinningError < .0001f &&
                (string.IsNullOrEmpty(entry.weaponKey) || result.weapon == entry.weaponKey && result.weaponMeshes > 0) &&
                (!spec.HairCycle || (result.maximumHairBoneAngle > 5f && result.maximumGlideHairStep > .005f));
            result.transitionScreenshots = transitionShots.ToArray();
            results.Add(result);
            File.AppendAllText(Path.Combine(directory, "results.jsonl"), JsonUtility.ToJson(result) + "\n");
            if (trace.Count > 0)
                File.WriteAllLines(Path.Combine(directory, prefix + "-samples.jsonl"), trace.Select(t => JsonUtility.ToJson(t)));
        }

        private float BodySkinningError()
        {
            var baked = new Mesh();
            try
            {
                float error = 0;
                SkinnedMeshRenderer[] renderers = lab.Actor.Arms ? lab.Actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>() : new[] { lab.Actor.Body };
                foreach (SkinnedMeshRenderer renderer in renderers)
                {
                    var model = new MikuGlider.Diagnostics.SkinningModel(renderer);
                    ApplyActiveBlendShapes(renderer, model.Rest);
                    Vector3[] expected = model.Pose();
                    renderer.BakeMesh(baked, true);
                    Vector3[] actual = baked.vertices;
                    for (int vertex = 0; vertex < actual.Length; vertex++)
                        error = Mathf.Max(error, Vector3.Distance(expected[vertex], renderer.transform.TransformPoint(actual[vertex])));
                }
                return error;
            }
            finally { Destroy(baked); }
        }

        private static void ApplyActiveBlendShapes(SkinnedMeshRenderer renderer, Vector3[] vertices)
        {
            Mesh mesh = renderer.sharedMesh;
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                float weight = renderer.GetBlendShapeWeight(shape);
                if (weight == 0f) continue;
                var frames = new List<KeyValuePair<float, int>>();
                for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                    frames.Add(new KeyValuePair<float, int>(mesh.GetBlendShapeFrameWeight(shape, frame), frame));
                if (frames.All(frame => frame.Key != 0f))
                    frames.Add(new KeyValuePair<float, int>(0f, -1));
                frames.Sort((left, right) => left.Key.CompareTo(right.Key));
                if (frames.Count < 2)
                    throw new InvalidOperationException("Blend shape has no nonzero frame weight.");
                int upper = 1;
                while (upper < frames.Count - 1 && weight > frames[upper].Key) upper++;
                KeyValuePair<float, int> lowerFrame = frames[upper - 1];
                KeyValuePair<float, int> upperFrame = frames[upper];
                var lower = new Vector3[vertices.Length];
                var higher = new Vector3[vertices.Length];
                if (lowerFrame.Value >= 0)
                    mesh.GetBlendShapeFrameVertices(shape, lowerFrame.Value, lower, null, null);
                if (upperFrame.Value >= 0)
                    mesh.GetBlendShapeFrameVertices(shape, upperFrame.Value, higher, null, null);
                float blend = (weight - lowerFrame.Key) / (upperFrame.Key - lowerFrame.Key);
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                    vertices[vertex] += Vector3.LerpUnclamped(lower[vertex], higher[vertex], blend);
            }
        }

        private void Log(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                errorEvents++;
                errors.Add(condition + "\n" + stack);
            }
        }
    }
}

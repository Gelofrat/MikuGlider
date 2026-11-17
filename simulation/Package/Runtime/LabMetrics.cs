using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using MikuGlider;
using MikuGlider.Diagnostics;
using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    [Serializable] public sealed class LabFrameMetrics
    {
        public bool ready;
        public bool reliable;
        public int particles;
        public int pinned;
        public int invalid;
        public float anchorError;
        public float displacement;
        public float penetration;
        public int bodyCrossings;
        public Vector3[] bodyCrossingPoints;
        public Vector3[] coatCrossingPoints;
        public int legCrossings;
        public Vector3[] legCrossingPoints;
        public int freeSkirtLegCrossings;
        public int coatCrossings;
        public int selfCrossings;
        public int stretchedEdges;
        public float guideStretch;
        public float guideStretch95;
        public float renderedStretch;
        public float renderedStretch95;
        public float lengthStretch;
        public double analysisMilliseconds;
        public double garmentPoseMilliseconds;
        public float seatedBlend;
        public float sampleTime;
        public float mappingError;
        public float previousAnchorError;
        public int bestAnchorLagFrames;
        public int invalidBones;
        public bool coordinatesVerified;
        public bool poseSynchronized;
    }

    [Serializable] public sealed class LabClothSnapshot
    {
        public float time;
        public string space;
        public Vector3[] raw;
        public Vector3[] world;
        public Vector3[] reference;
        public Vector3[] surface;
        public int[] meshToParticle;
        public float[] maxDistances;
        public float mappingError;
    }

    [DefaultExecutionOrder(1110)]
    public sealed class LabMetrics : MonoBehaviour
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private LabActor actor;
        private SkinnedMeshRenderer skirt;
        private SkinnedMeshRenderer coat;
        private SkinningModel skirtSkin;
        private SkinningModel bodySkin;
        private SkinningModel legSkin;
        private SurfaceGeometry legGeometry;
        private SurfaceGeometry freeLegGeometry;
        private SkinningModel coatSkin;
        private SkinningModel renderedSkin;
        private ClothVertexMapping mapping;
        private SurfaceGeometry geometry;
        private SurfaceGeometry layers;
        private LabSkirtStrain strain;
        private float nextAnalysis;
        private Task<LabFrameMetrics> pending;
        private Vector3[] latestSurface;
        public LabFrameMetrics Current { get; private set; } = new LabFrameMetrics();
        public Vector3[] Surface => latestSurface;
        public SkinnedMeshRenderer Skirt => skirt;
        public bool DrawParticles;
        internal bool Deterministic;
        internal LabClothSnapshot Snapshot;
        [Serializable] private sealed class GeometryExport
        {
            public Vector3[] body;
            public Vector3[] skirt;
            public Vector3[] restSkirt;
            public int[] bodyTriangles;
            public int[] skirtTriangles;
            public Vector3[] joints;
            public float[] openings;
            public float seated;
        }

        internal string ExportGeometry()
        {
            PoseGarmentRig rig = Read<PoseGarmentRig>("poseRig");
            Matrix4x4 inverse = rig.Reference.worldToLocalMatrix;
            Vector3[] body = legSkin.Pose();
            Vector3[] surface = renderedSkin.Pose();
            for (int i = 0; i < body.Length; i++) body[i] = inverse.MultiplyPoint3x4(body[i]);
            for (int i = 0; i < surface.Length; i++) surface[i] = inverse.MultiplyPoint3x4(surface[i]);
            var joints = new Vector3[actor.Body.bones.Length];
            for (int i = 0; i < joints.Length; i++) joints[i] = inverse.MultiplyPoint3x4(actor.Body.bones[i].position);
            return JsonUtility.ToJson(new GeometryExport { body = body, skirt = surface,
                restSkirt = skirt.sharedMesh.vertices, bodyTriangles = legSkin.Triangles,
                skirtTriangles = skirtSkin.Triangles, joints = joints,
                openings = (float[])typeof(PoseGarmentRig).GetField("openings", Fields).GetValue(rig), seated = rig.SeatedBlend });
        }
        private readonly Vector3[][] referenceHistory = new Vector3[3][];

        internal void Bind(LabActor value) => actor = value;

        private void LateUpdate()
        {
            if (actor == null || actor.Garment == null)
                return;
            if (mapping == null)
                Initialize();
            if (mapping == null)
                return;
            referenceHistory[2] = referenceHistory[1];
            referenceHistory[1] = referenceHistory[0];
            referenceHistory[0] = skirtSkin.Pose();
        }

        private void Initialize()
        {
            skirt = Read<SkinnedMeshRenderer>("skirt");
            coat = Read<SkinnedMeshRenderer>("coat");
            if (skirt == null || !Read<bool>("ready"))
                return;
            skirtSkin = new SkinningModel(skirt);
            strain = new LabSkirtStrain(skirt.sharedMesh);
            renderedSkin = new SkinningModel(actor.Garment.SurfaceRenderer);
            bodySkin = new SkinningModel(actor.Body, true);
            bodySkin.PrepareLowerBody();
            legSkin = new SkinningModel(actor.Body, true, true);
            legSkin.PrepareLowerBody();
            legGeometry = new SurfaceGeometry(skirtSkin.Rest, skirtSkin.Triangles, legSkin.Rest, legSkin.Triangles, int.MaxValue);
            var freeTriangles = new System.Collections.Generic.List<int>();
            BoneWeight[] garmentWeights = skirt.sharedMesh.boneWeights;
            int[] skirtFaces = skirtSkin.Triangles;
            for (int face = 0; face < skirtFaces.Length; face += 3)
            {
                bool free = true;
                for (int corner = 0; corner < 3; corner++)
                {
                    BoneWeight weight = garmentWeights[skirtFaces[face + corner]];
                    float native = (weight.boneIndex0 < actor.Body.bones.Length ? weight.weight0 : 0) +
                        (weight.boneIndex1 < actor.Body.bones.Length ? weight.weight1 : 0) +
                        (weight.boneIndex2 < actor.Body.bones.Length ? weight.weight2 : 0) +
                        (weight.boneIndex3 < actor.Body.bones.Length ? weight.weight3 : 0);
                    free &= native < .01f;
                }
                if (free)
                    for (int corner = 0; corner < 3; corner++) freeTriangles.Add(skirtFaces[face + corner]);
            }
            freeLegGeometry = new SurfaceGeometry(skirtSkin.Rest, freeTriangles.ToArray(), legSkin.Rest, legSkin.Triangles);
            if (coat != null)
                coatSkin = new SkinningModel(coat);
            var identity = new int[skirt.sharedMesh.vertexCount];
            for (int i = 0; i < identity.Length; i++) identity[i] = i;
            mapping = new ClothVertexMapping(identity, 0, identity.Length);
            geometry = new SurfaceGeometry(skirtSkin.Rest, skirtSkin.Triangles, bodySkin.Rest, bodySkin.Triangles, int.MaxValue);
            if (coatSkin != null)
                layers = new SurfaceGeometry(skirtSkin.Rest, skirtSkin.Triangles, coatSkin.Rest, coatSkin.Triangles);
            StartCoroutine(SampleFrames());
        }

        private IEnumerator SampleFrames()
        {
            while (true)
            {
                yield return new WaitForEndOfFrame();
                if (mapping == null || Deterministic)
                    continue;
                if (pending != null && pending.IsCompleted)
                {
                    Current = pending.GetAwaiter().GetResult();
                    pending = null;
                }
                if (Time.time < nextAnalysis || pending != null)
                    continue;
                nextAnalysis = Time.time + .1f;
                pending = Task.Run(Capture());
            }
        }

        internal LabFrameMetrics SampleNow()
        {
            if (mapping == null)
                return new LabFrameMetrics();
            Current = Capture()();
            return Current;
        }

        private Func<LabFrameMetrics> Capture()
        {
            Vector3[] raw = skirtSkin.Pose();
            if (raw.Length != mapping.ParticleCount)
                throw new InvalidOperationException("Garment vertex layout changed during the test.");
            Vector3[] world = raw;
            renderedSkin.RefreshVertices();
            Vector3[] surface = renderedSkin.Pose();
            latestSurface = surface;
            Vector3[] reference = skirtSkin.Pose();
            Vector3[] body = bodySkin.Pose();
            Vector3[] legs = legSkin.Pose();
            coatSkin?.RefreshVertices();
            Vector3[] overlay = coatSkin?.Pose();
            BoneWeight[] weights = skirt.sharedMesh.boneWeights;
            var maxDistances = new float[weights.Length];
            var pinned = new bool[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                pinned[i] = weights[i].weight0 > .999f && weights[i].boneIndex0 < actor.Body.bones.Length;
                maxDistances[i] = pinned[i] ? 0f : 1f;
            }
            ColliderData[] proxies = Array.Empty<ColliderData>();
            float timestamp = Time.time;
            double poseCost = actor.Garment.PoseUpdateMilliseconds;
            float seatedBlend = Read<PoseGarmentRig>("poseRig").SeatedBlend;
            Matrix4x4 garmentInverse = Read<PoseGarmentRig>("poseRig").Reference.worldToLocalMatrix;
            float scale = CharacterBodyBones.GetScale(skirt.transform);
            float guideStretch = strain.Measure(reference, scale, out float guideStretch95);
            float renderedStretch = strain.Measure(surface, scale, out float renderedStretch95);
            float lengthStretch = strain.LengthStretch(surface, scale);
            Vector3[][] previous = (Vector3[][])referenceHistory.Clone();
            int invalidBones = 0;
            foreach (Transform bone in actor.Root.GetComponentsInChildren<Transform>())
                if (!GeometryMath.Finite(bone.position) || !GeometryMath.Finite(bone.lossyScale) || !float.IsFinite(bone.rotation.w))
                    invalidBones++;
            Snapshot = new LabClothSnapshot { time = timestamp, space = "pose_skinned_world",
                raw = raw, world = world, reference = reference, surface = surface, meshToParticle = mapping.MeshToParticle,
                mappingError = mapping.Error, maxDistances = maxDistances };
            return () =>
            {
                LabFrameMetrics result = Analyze(surface, reference, body, overlay, world, pinned, proxies, timestamp, previous, invalidBones);
                var legData = new GeometryData();
                legGeometry.Analyze(surface, reference, legs, 10000, legData);
                result.legCrossings = legData.bodyCrossings;
                result.legCrossingPoints = legData.crossingPoints;
                var freeData = new GeometryData();
                freeLegGeometry.Analyze(surface, reference, legs, 10000, freeData);
                result.freeSkirtLegCrossings = freeData.bodyCrossings;
                for (int i = 0; i < result.legCrossingPoints.Length; i++)
                    result.legCrossingPoints[i] = garmentInverse.MultiplyPoint3x4(result.legCrossingPoints[i]);
                result.garmentPoseMilliseconds = poseCost;
                result.seatedBlend = seatedBlend;
                result.guideStretch = guideStretch;
                result.guideStretch95 = guideStretch95;
                result.renderedStretch = renderedStretch;
                result.renderedStretch95 = renderedStretch95;
                result.lengthStretch = lengthStretch;
                if (result.bodyCrossingPoints != null)
                    for (int i = 0; i < result.bodyCrossingPoints.Length; i++)
                        result.bodyCrossingPoints[i] = garmentInverse.MultiplyPoint3x4(result.bodyCrossingPoints[i]);
                if (result.coatCrossingPoints != null)
                    for (int i = 0; i < result.coatCrossingPoints.Length; i++)
                        result.coatCrossingPoints[i] = garmentInverse.MultiplyPoint3x4(result.coatCrossingPoints[i]);
                return result;
            };
        }

        private LabFrameMetrics Analyze(Vector3[] surface, Vector3[] reference, Vector3[] body, Vector3[] overlay,
            Vector3[] particles, bool[] pinned, ColliderData[] proxies, float timestamp, Vector3[][] history, int invalidBones)
        {
            var watch = Stopwatch.StartNew();
            var result = new LabFrameMetrics { ready = true, particles = particles.Length, sampleTime = timestamp, mappingError = mapping.Error, invalidBones = invalidBones };
            for (int vertex = 0; vertex < surface.Length; vertex++)
            {
                if (!GeometryMath.Finite(surface[vertex]))
                    result.invalid++;
                float difference = Vector3.Distance(surface[vertex], reference[vertex]);
                result.displacement = Math.Max(result.displacement, difference);
                if (pinned[mapping.MeshToParticle[vertex]])
                    result.anchorError = Math.Max(result.anchorError, difference);
            }
            foreach (bool fixedParticle in pinned)
                if (fixedParticle)
                    result.pinned++;
            float bestAnchor = result.anchorError;
            for (int age = 1; age < history.Length; age++)
            {
                if (history[age] == null)
                    continue;
                float error = 0;
                for (int vertex = 0; vertex < surface.Length; vertex++)
                    if (pinned[mapping.MeshToParticle[vertex]])
                        error = Math.Max(error, Vector3.Distance(surface[vertex], history[age][vertex]));
                if (age == 1)
                    result.previousAnchorError = error;
                if (error < bestAnchor)
                {
                    bestAnchor = error;
                    result.bestAnchorLagFrames = age;
                }
            }
            result.coordinatesVerified = actor.Garment.UsesPoseRig && mapping.Reliable;
            result.poseSynchronized = result.anchorError < .004f;
            result.reliable = result.coordinatesVerified && result.poseSynchronized;
            foreach (Vector3 point in particles)
                foreach (ColliderData proxy in proxies)
                    result.penetration = Math.Max(result.penetration, -GeometryMath.CapsuleDistance(point,
                        proxy.start, proxy.end, proxy.startRadius, proxy.endRadius));
            if (result.coordinatesVerified)
            {
                var data = new GeometryData();
                geometry.Analyze(surface, reference, body, 10000, data);
                result.bodyCrossings = data.bodyCrossings;
                result.bodyCrossingPoints = data.crossingPoints;
                result.selfCrossings = data.selfCrossings;
                result.stretchedEdges = data.stretchedEdges;
                if (layers != null && overlay != null)
                {
                    data = new GeometryData();
                    layers.Analyze(surface, reference, overlay, 10000, data);
                    result.coatCrossings = data.bodyCrossings;
                    result.coatCrossingPoints = data.crossingPoints;
                }
            }
            result.analysisMilliseconds = watch.Elapsed.TotalMilliseconds;
            return result;
        }

        private T Read<T>(string name) => (T)typeof(SkirtGarmentVisual).GetField(name, Fields).GetValue(actor.Garment);

        private void OnDrawGizmos()
        {
            if (!DrawParticles || latestSurface == null)
                return;
            Gizmos.color = new Color(.1f, 1f, .75f);
            foreach (Vector3 point in latestSurface)
                Gizmos.DrawSphere(point, .003f);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MikuGlider;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Horse.MikuPhysicsLab.Editor
{
    internal sealed class LabManualSession : IDisposable
    {
        internal readonly SkinnedMeshRenderer Renderer;
        internal readonly Mesh Source;
        internal readonly Mesh Working;
        internal readonly Transform[] OriginalBones;
        internal readonly Transform[] Bones;
        internal readonly Quaternion[] DriverRotations;
        internal readonly HashSet<int> Selected = new HashSet<int>();
        internal readonly HashSet<int> Drivers = new HashSet<int>();
        internal readonly string Variant;
        internal readonly string PoseSource;
        internal readonly BoneWeight[] OriginalWeights;
        internal readonly string Fingerprint;
        private readonly LabController lab;
        private readonly GameObject root;
        private readonly Behaviour[] paused;
        private readonly float[] shapes;
        private readonly Mesh baked;
        private readonly MeshCollider picker;
        private bool disposed;
        internal Vector3[] Points;
        internal Vector3[] Normals;

        internal LabManualSession(LabController controller, SkinnedMeshRenderer renderer)
        {
            lab = controller;
            Renderer = renderer;
            Source = renderer.sharedMesh;
            OriginalBones = renderer.bones;
            DriverRotations = OriginalBones.Select(bone => bone.localRotation).ToArray();
            Variant = lab.Variants[lab.Variant].key;
            PoseSource = (lab.Replay >= 0 ? lab.Replays[lab.Replay].key : lab.Clips[lab.Motion].key) + " @ " + lab.Position.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            var applied = ModelPoseCorrections.GetApplied(Source);
            OriginalWeights = applied?.OriginalWeights ?? Source.boneWeights;
            if (applied != null) Fingerprint = applied.Correction.fingerprint;
            else Fingerprint = ModelCorrections.Fingerprint(Source);
            shapes = Enumerable.Range(0, Source.blendShapeCount).Select(renderer.GetBlendShapeWeight).ToArray();
            lab.ManualEditing = true;
            lab.Playing = false;
            Vector3[] frozenPositions = OriginalBones.Select(b => b.localPosition).ToArray();
            Vector3[] frozenScales = OriginalBones.Select(b => b.localScale).ToArray();
            paused = lab.Actor.Root.GetComponentsInChildren<Behaviour>(true).Where(component => component.enabled).ToArray();
            foreach (Behaviour component in paused) component.enabled = false;
            for (int i = 0; i < OriginalBones.Length; i++)
            {
                OriginalBones[i].localPosition = frozenPositions[i];
                OriginalBones[i].localRotation = DriverRotations[i];
                OriginalBones[i].localScale = frozenScales[i];
            }
            lab.GetComponent<LabInterface>().enabled = false;
            Working = Object.Instantiate(Source);
            Working.name = Source.name + " manual preview";
            Working.ClearBlendShapes();
            root = new GameObject("Manual editing preview") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(renderer.transform, false);
            Bones = OriginalBones.Select(bone => new GameObject(bone.name).transform).ToArray();
            var lookup = OriginalBones.Select((bone, i) => new { bone, i }).GroupBy(pair => pair.bone).ToDictionary(group => group.Key, group => group.First().i);
            int[] order = Enumerable.Range(0, Bones.Length).OrderBy(i => Depth(OriginalBones[i])).ToArray();
            foreach (int i in order)
            {
                Transform parent = OriginalBones[i].parent;
                while (parent != null && !lookup.ContainsKey(parent)) parent = parent.parent;
                Bones[i].SetParent(parent != null ? Bones[lookup[parent]] : root.transform, false);
                Bones[i].SetPositionAndRotation(OriginalBones[i].position, OriginalBones[i].rotation);
                Vector3 scale = Bones[i].parent.lossyScale;
                Vector3 sourceScale = OriginalBones[i].lossyScale;
                Bones[i].localScale = new Vector3(sourceScale.x / scale.x, sourceScale.y / scale.y, sourceScale.z / scale.z);
            }
            renderer.sharedMesh = Working;
            renderer.bones = Bones;
            baked = new Mesh();
            var pickerObject = new GameObject("Selection surface") { hideFlags = HideFlags.HideAndDontSave };
            pickerObject.transform.SetParent(root.transform, false);
            picker = pickerObject.AddComponent<MeshCollider>();
            Bake();
        }

        private static int Depth(Transform bone)
        {
            int depth = 0;
            for (; bone != null; bone = bone.parent) depth++;
            return depth;
        }

        internal void Bake()
        {
            if (disposed || Renderer == null) return;
            picker.sharedMesh = null;
            Renderer.BakeMesh(baked);
            Points = baked.vertices.Select(Renderer.transform.TransformPoint).ToArray();
            Normals = baked.normals.Select(Renderer.transform.TransformDirection).ToArray();
            picker.sharedMesh = baked;
            SceneView.RepaintAll();
        }

        internal bool Pick(Ray ray, out RaycastHit hit) => picker.Raycast(ray, out hit, 10000);

        internal void ApplyWeight(int bone, float weight)
        {
            BoneWeight[] weights = Working.boneWeights;
            foreach (int vertex in Selected) weights[vertex] = LabCorrectionMath.SetWeight(weights[vertex], bone, weight);
            Undo.RegisterCompleteObjectUndo(Working, "Change skin weights");
            Working.boneWeights = weights;
            Bake();
        }

        internal MeshCorrection Export(string poseName, float radius, MeshCorrection existing, bool clearPoses)
        {
            BoneWeight[] weights = Working.boneWeights;
            var edits = new List<VertexWeightEdit>();
            for (int i = 0; i < weights.Length; i++)
                if (!weights[i].Equals(OriginalWeights[i])) edits.Add(new VertexWeightEdit { vertex = i, value = weights[i] });
            bool changed = !weights.SequenceEqual(Source.boneWeights);
            var poses = existing != null && !clearPoses ? existing.poses.ToList() : new List<PoseShapeEdit>();
            if (changed && poses.Count > 0) throw new InvalidOperationException("Weights changed. Enable clearing old pose corrections for this surface, then save again.");
            Vector3[] deltas = LabCorrectionMath.PoseDeltas(Working, OriginalBones, Bones);
            int[] active = Enumerable.Range(0, deltas.Length).Where(i => deltas[i].sqrMagnitude > 1e-10f).ToArray();
            if (active.Length > 0)
            {
                if (Drivers.Count == 0) throw new InvalidOperationException("Choose at least one pose driver before saving a bone adjustment.");
                if (string.IsNullOrWhiteSpace(poseName)) throw new InvalidOperationException("Enter a pose name.");
                poses.RemoveAll(pose => pose.name == poseName);
                poses.Add(new PoseShapeEdit { name = poseName, sourcePose = PoseSource,
                    drivers = Drivers.OrderBy(i => i).Select(i => new PoseShapeDriver { bone = i, name = OriginalBones[i].name,
                        rotation = DriverRotations[i], radius = radius }).ToArray(),
                    vertices = active, deltas = active.Select(i => deltas[i]).ToArray() });
            }
            var result = new MeshCorrection { key = ModelCorrections.Key(Source), fingerprint = Fingerprint,
                weights = edits.ToArray(), poses = poses.ToArray() };
            Mesh baseline = Object.Instantiate(Source);
            baseline.boneWeights = OriginalWeights;
            try { ModelCorrections.Validate(baseline, result); }
            finally { Object.DestroyImmediate(baseline); }
            return result;
        }

        internal static string ProjectRoot()
        {
            var directory = new DirectoryInfo(Application.dataPath);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "MikuGlider.csproj"))) directory = directory.Parent;
            if (directory == null) throw new DirectoryNotFoundException("Open simulation/Project inside the MikuGlider repository.");
            return directory.FullName;
        }

        internal string ProfilePath => Path.Combine(ProjectRoot(), "assets", Variant == "v1" ? "" : Variant, "corrections.json");

        internal void Save(string poseName, float radius, bool clearPoses)
        {
            string path = ProfilePath;
            ModelCorrections profile = ModelCorrections.Parse(File.ReadAllText(path));
            string key = ModelCorrections.Key(Source);
            MeshCorrection existing = profile.meshes.FirstOrDefault(mesh => mesh.key == key);
            MeshCorrection next = Export(poseName, radius, existing, clearPoses);
            profile.meshes = profile.meshes.Where(mesh => mesh.key != key).Concat(new[] { next }).ToArray();
            string resource = Path.Combine(ProjectRoot(), "simulation/Package/Resources/MikuPhysicsLab", Variant, "corrections.json");
            PersistProfile(path, resource, Path.Combine(ProjectRoot(), "scratch", "manual-edit-backups"), Variant, profile);
            AssetDatabase.Refresh();
        }

        internal static void PersistProfile(string path, string resource, string backupRoot, string variant, ModelCorrections profile)
        {
            string json = JsonUtility.ToJson(profile, true);
            ModelCorrections.Parse(json);
            string backup = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(backup);
            File.Copy(path, Path.Combine(backup, variant + "-corrections.json"));
            string temp = path + ".tmp";
            File.WriteAllText(temp, json);
            for (int attempt = 0; ; attempt++)
            {
                try { File.Replace(temp, path, null); break; }
                catch (IOException) when (attempt < 4 && File.Exists(temp)) { System.Threading.Thread.Sleep(50); }
            }
            File.WriteAllText(resource, json);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Renderer != null)
            {
                Renderer.sharedMesh = Source;
                Renderer.bones = OriginalBones;
                for (int i = 0; i < shapes.Length; i++) Renderer.SetBlendShapeWeight(i, shapes[i]);
            }
            foreach (Behaviour component in paused) if (component != null) component.enabled = true;
            if (lab != null)
            {
                lab.ManualEditing = false;
                lab.GetComponent<LabInterface>().enabled = true;
            }
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(baked);
            Object.DestroyImmediate(Working);
        }
    }
}

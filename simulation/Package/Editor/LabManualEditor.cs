using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MikuGlider;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Horse.MikuPhysicsLab.Editor
{
    public sealed class LabManualEditor : EditorWindow
    {
        private LabController lab;
        private LabManualSession session;
        private int surfaceIndex;
        private int bone;
        private bool brush;
        private bool frontOnly = true;
        private bool clearPoses;
        private float brushRadius = .035f;
        private float weight = .5f;
        private float radius = 30;
        private string poseName = "Wrist support";
        private string filter = "";
        private string status = "";
        private Vector2 scroll;
        private Process build;
        private string buildLog;

        [MenuItem("Miku Physics Lab/Manual model editor")]
        public static void Open() => GetWindow<LabManualEditor>("Miku model editor");

        private void OnEnable()
        {
            SceneView.duringSceneGui += SceneGUI;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            Undo.undoRedoPerformed += AfterUndo;
        }

        private void OnDisable()
        {
            Finish();
            SceneView.duringSceneGui -= SceneGUI;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            Undo.undoRedoPerformed -= AfterUndo;
        }

        private void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Finish();
        }

        private void AfterUndo() { session?.Bake(); Repaint(); }

        private void Tick()
        {
            if (build != null && build.HasExited)
            {
                status = build.ExitCode == 0 ? "DLL built: bin/Release/MikuGlider.dll. Game files were not changed." : "Build failed. See " + buildLog;
                build.Dispose();
                build = null;
            }
            if (EditorApplication.isPlaying) Repaint();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Manual model corrections", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Weights affect every pose. Bone edits are saved as a pose-driven surface correction, preserving the game's skeleton and weapon attachment.", MessageType.Info);
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Open the inspection scene and enter Play Mode. Unsaved previews are discarded on exit; saved JSON files persist.", MessageType.None);
                if (GUILayout.Button("Open inspection scene and enter Play Mode"))
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        if (File.Exists("Assets/Scenes/MikuPhysicsLab.unity")) EditorSceneManager.OpenScene("Assets/Scenes/MikuPhysicsLab.unity");
                        else LabBuild.CreateScene();
                        EditorApplication.isPlaying = true;
                    }
                }
            }
            else
            {
                lab = Object.FindFirstObjectByType<LabController>();
                if (lab != null && lab.Ready && !lab.Testing) DrawLab();
                else EditorGUILayout.LabelField("Waiting for the inspection scene...");
            }
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(build != null || session != null))
                if (GUILayout.Button("Build mod DLL from saved corrections")) Safe(BuildMod);
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private void DrawLab()
        {
            if (lab.Arms && lab.View != null)
            {
                lab.View.FirstPersonView = EditorGUILayout.Toggle("Game camera (arms FOV 55)", lab.View.FirstPersonView);
                EditorGUILayout.HelpBox("Game view uses the native first-person projection. Scene view remains free for editing.", MessageType.None);
            }
            using (new EditorGUI.DisabledScope(session != null))
            {
                int variant = EditorGUILayout.Popup("Model", lab.Variant, lab.Variants.Select(v => v.label).ToArray());
                if (variant != lab.Variant) { lab.Select(variant, lab.Arms); surfaceIndex = 0; }
                filter = EditorGUILayout.TextField("Animation filter", filter);
                int[] choices = Enumerable.Range(0, lab.Clips.Length).Where(i =>
                    (lab.Clips[i].label + " " + lab.Clips[i].key).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                int selected = Array.IndexOf(choices, lab.Motion);
                int next = EditorGUILayout.Popup("Animation / combination", selected, choices.Select(i => lab.Clips[i].rig + " / " + lab.Clips[i].label).ToArray());
                if (next >= 0 && next != selected) { lab.SelectMotion(choices[next]); surfaceIndex = 0; }
                string[] replays = new[] { "Animation" }.Concat(lab.Replays.Select(r => r.label + " / " + r.key)).ToArray();
                int replay = EditorGUILayout.Popup("Recorded body pose", lab.Replay + 1, replays) - 1;
                if (replay != lab.Replay)
                {
                    if (replay < 0) lab.SelectMotion(lab.Motion);
                    else lab.SelectReplay(replay, lab.Variant);
                    surfaceIndex = 0;
                }
                lab.Playing = EditorGUILayout.Toggle("Play", lab.Playing);
                float time = EditorGUILayout.Slider("Time (seconds)", lab.Position, 0, lab.Duration);
                if (Mathf.Abs(time - lab.Position) > .0001f) { lab.Position = time; lab.Playing = false; }
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("- frame")) { lab.Position = Mathf.Max(0, lab.Position - 1f / 60); lab.Playing = false; }
                if (GUILayout.Button("+ frame")) { lab.Position = Mathf.Min(lab.Duration, lab.Position + 1f / 60); lab.Playing = false; }
                if (GUILayout.Button("Frame character")) SceneView.lastActiveSceneView?.Frame(new Bounds(lab.Actor.Focus, Vector3.one * (lab.Arms ? .6f : 2f)), false);
                EditorGUILayout.EndHorizontal();
                var surfaces = lab.Actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.sharedMesh != null && r.sharedMesh.boneWeights.Length > 0 &&
                    (r.sharedMesh.name.Contains(".body") || r.sharedMesh.name.Contains(".arm-") || r.sharedMesh.name.Contains(".skirt") || r.sharedMesh.name.Contains(".coat-flaps") || r.sharedMesh.name.Contains(".sleeves"))).ToArray();
                if (surfaces.Length > 0)
                {
                    surfaceIndex = Mathf.Clamp(surfaceIndex, 0, surfaces.Length - 1);
                    surfaceIndex = EditorGUILayout.Popup("Surface", surfaceIndex, surfaces.Select(r => ModelCorrections.Key(r.sharedMesh)).ToArray());
                    using (new EditorGUI.DisabledScope(lab.Playing))
                        if (GUILayout.Button("Freeze pose and start editing")) Safe(() =>
                        {
                            session = new LabManualSession(lab, surfaces[surfaceIndex]);
                            bone = 0;
                            brush = false;
                            status = "Preview active. Select a bone or enable vertex selection in Scene view. Ctrl+Z undoes preview edits.";
                        });
                }
            }
            if (session != null) DrawSession();
        }

        private void DrawSession()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Frozen pose: " + session.PoseSource, EditorStyles.wordWrappedLabel);
            bone = EditorGUILayout.Popup("Bone", bone, session.Bones.Select((b, i) => i + ": " + b.name).ToArray());
            Transform target = session.Bones[bone];
            EditorGUI.BeginChangeCheck();
            Vector3 position = EditorGUILayout.Vector3Field("Local position", target.localPosition);
            Vector3 rotation = EditorGUILayout.Vector3Field("Local rotation", target.localEulerAngles);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Adjust preview bone");
                target.localPosition = position;
                target.localEulerAngles = rotation;
                session.Bake();
            }
            bool driver = EditorGUILayout.Toggle("Use as pose driver", session.Drivers.Contains(bone));
            if (driver) session.Drivers.Add(bone); else session.Drivers.Remove(bone);
            EditorGUILayout.LabelField("Drivers: " + string.Join(", ", session.Drivers.Select(i => session.Bones[i].name)), EditorStyles.wordWrappedLabel);
            radius = EditorGUILayout.Slider("Fade radius (degrees)", radius, 1, 90);
            poseName = EditorGUILayout.TextField("Correction name", poseName);
            EditorGUILayout.Space();
            brush = EditorGUILayout.Toggle("Select vertices in Scene", brush);
            brushRadius = EditorGUILayout.Slider("Selection radius", brushRadius, .002f, .2f);
            frontOnly = EditorGUILayout.Toggle("Front-facing vertices only", frontOnly);
            EditorGUILayout.LabelField("Selected vertices: " + session.Selected.Count);
            EditorGUILayout.HelpBox("Click/drag selects a surface patch. Shift adds, Ctrl removes, Alt orbits. W/E handles move/rotate the selected bone when vertex selection is off.", MessageType.None);
            weight = EditorGUILayout.Slider("Selected bone weight", weight, 0, 1);
            using (new EditorGUI.DisabledScope(session.Selected.Count == 0))
                if (GUILayout.Button("Set weight on selected vertices (normalize)")) Safe(() => session.ApplyWeight(bone, weight));
            if (GUILayout.Button("Clear vertex selection")) session.Selected.Clear();
            clearPoses = EditorGUILayout.Toggle("Clear old pose shapes for this surface", clearPoses);
            EditorGUILayout.Space();
            if (GUILayout.Button("Save to mod project (with backup)")) Safe(() =>
            {
                string path = session.ProfilePath;
                session.Save(poseName, radius, clearPoses);
                Finish();
                ReloadActor();
                status = "Saved " + path + ". Replaying the saved correction. Build the DLL after checking other poses.";
            });
            if (GUILayout.Button("Discard preview and return to playback")) { Finish(); status = "Preview discarded. Saved files were not changed."; }
        }

        private void ReloadActor()
        {
            int variant = lab.Variant;
            float time = lab.Position;
            bool arms = lab.Arms;
            lab.Actor.Dispose();
            LabAssetLibrary.Clear();
            lab.Select(variant, arms);
            lab.Position = time;
            lab.Playing = false;
        }

        private void SceneGUI(SceneView view)
        {
            if (session == null || session.Renderer == null) return;
            Event e = Event.current;
            if (brush && !e.alt)
            {
                if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (session.Pick(ray, out RaycastHit hit))
                {
                    Handles.color = Color.cyan;
                    Handles.DrawWireDisc(hit.point, hit.normal, brushRadius);
                    if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
                    {
                        if (e.type == EventType.MouseDown && !e.shift && !e.control) session.Selected.Clear();
                        for (int i = 0; i < session.Points.Length; i++)
                        {
                            if ((session.Points[i] - hit.point).sqrMagnitude > brushRadius * brushRadius ||
                                frontOnly && Vector3.Dot(session.Normals[i], ray.direction) >= 0) continue;
                            if (e.control) session.Selected.Remove(i); else session.Selected.Add(i);
                        }
                        e.Use();
                        Repaint();
                    }
                }
                view.Repaint();
            }
            else if (!brush)
            {
                Transform target = session.Bones[bone];
                Handles.color = Color.yellow;
                if (target.parent != null) Handles.DrawLine(target.parent.position, target.position);
                EditorGUI.BeginChangeCheck();
                Vector3 position = target.position;
                Quaternion rotation = target.rotation;
                if (Tools.current == Tool.Move) position = Handles.PositionHandle(position, rotation);
                else rotation = Handles.RotationHandle(rotation, position);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Adjust preview bone");
                    target.SetPositionAndRotation(position, rotation);
                    session.Bake();
                }
            }
            if (e.type == EventType.Repaint)
            {
                BoneWeight[] weights = session.Working.boneWeights;
                foreach (int vertex in session.Selected)
                {
                    Handles.color = Color.Lerp(Color.blue, Color.red, LabCorrectionMath.Influence(weights[vertex], bone));
                    Vector3 point = session.Points[vertex];
                    Handles.DotHandleCap(0, point, Quaternion.identity, HandleUtility.GetHandleSize(point) * .006f, EventType.Repaint);
                }
            }
        }

        private void Finish() { session?.Dispose(); session = null; SceneView.RepaintAll(); }

        private void Safe(Action action)
        {
            try { action(); }
            catch (Exception error) { status = error.Message; Debug.LogException(error); }
        }

        private void BuildMod()
        {
            string root = LabManualSession.ProjectRoot();
            string script = Path.Combine(root, "simulation", "build-manual.ps1");
            buildLog = Path.Combine(root, "simulation", "Reports", "manual-build.log");
            build = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"")
            { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true });
            status = "Building mod DLL... " + buildLog;
        }
    }
}

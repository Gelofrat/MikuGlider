using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    public sealed class LabInterface : MonoBehaviour
    {
        private LabController lab;
        private Vector2 scroll;
        private string search = "";
        private Vector2 clipScroll;
        internal void Bind(LabController value) => lab = value;

        private void OnGUI()
        {
            if (lab == null || !lab.Ready)
            {
                GUI.Label(new Rect(25, 25, 600, 40), "Miku Physics Lab / Loading pose garments...");
                return;
            }
            if (lab.Testing)
            {
                GUI.Label(new Rect(20, 20, Screen.width - 40, 70), lab.Status);
                return;
            }
            GUI.skin.label.wordWrap = true;
            GUILayout.BeginArea(new Rect(12, 12, 290, Screen.height - 24), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("MIKU PHYSICS LAB | Unity " + Application.unityVersion);
            GUILayout.Label("Shared garment code, native clips and recorded game poses.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Front")) { lab.View.FirstPersonView = false; lab.View.Yaw = 180; lab.View.Pitch = 0; }
            if (GUILayout.Button("Side")) { lab.View.FirstPersonView = false; lab.View.Yaw = 90; lab.View.Pitch = 0; }
            if (GUILayout.Button("Back")) { lab.View.FirstPersonView = false; lab.View.Yaw = 0; lab.View.Pitch = 0; }
            GUILayout.EndHorizontal();
            GUI.enabled = !lab.Testing;
            GUILayout.BeginHorizontal();
            for (int index = 0; index < lab.Variants.Length; index++)
                if (GUILayout.Toggle(lab.Variant == index, "Miku v" + (index + 1), GUI.skin.button) && lab.Variant != index)
                    lab.Select(index, lab.Arms);
            GUILayout.EndHorizontal();
            bool arms = GUILayout.Toggle(lab.Arms, "First person arms inspection");
            if (arms != lab.Arms)
                lab.Select(lab.Variant, arms);
            if (lab.Arms)
            {
                lab.View.FirstPersonView = GUILayout.Toggle(lab.View.FirstPersonView, "Game camera / first-person FOV 55");
                GUILayout.Label("Elbow bend: " + lab.Elbow.ToString("F0") + " degrees");
                lab.Elbow = GUILayout.HorizontalSlider(lab.Elbow, 0, 120);
            }
            else
            {
                bool hat = GUILayout.Toggle(lab.Hat, "Hat-compatible head mesh");
                if (hat != lab.Hat)
                    lab.Actor.SetHat(lab.Hat = hat);
                bool flight = GUILayout.Toggle(lab.Flight, "Flight hair / authored animation");
                if (flight != lab.Flight)
                {
                    lab.Flight = flight;
                    lab.View.Distance = flight ? 7.2f : 4.4f;
                }
                lab.Particles = GUILayout.Toggle(lab.Particles, "Garment vertex overlay");
            }
            GUILayout.Space(8);
            GUILayout.Label("Native Glider animation");
            search = GUILayout.TextField(search);
            clipScroll = GUILayout.BeginScrollView(clipScroll, GUILayout.Height(230));
            for (int index = 0; index < lab.Clips.Length; index++)
                if ((lab.Clips[index].category + " " + lab.Clips[index].rig + " " + lab.Clips[index].label).IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                    GUILayout.Toggle(lab.Replay < 0 && lab.Motion == index, "[" + lab.Clips[index].rig + "] " + lab.Clips[index].label, GUI.skin.button) && (lab.Motion != index || lab.Replay >= 0))
                    lab.SelectMotion(index);
            GUILayout.EndScrollView();
            GUILayout.Space(8);
            GUILayout.Label("Recorded motion (interpolated bone tracks)");
            foreach (ReplayEntry entry in lab.Replays)
            {
                int index = System.Array.IndexOf(lab.Replays, entry);
                if (GUILayout.Toggle(lab.Replay == index, entry.label, GUI.skin.button) && lab.Replay != index)
                    lab.SelectReplay(index);
            }
            GUILayout.Space(8);
            lab.Playing = GUILayout.Toggle(lab.Playing, "Advance animation (physics remains live)");
            GUILayout.Label("Timeline: " + lab.Position.ToString("F2") + " / " + lab.Duration.ToString("F2") + " s");
            lab.Position = GUILayout.HorizontalSlider(lab.Position, 0, lab.Duration);
            GUILayout.Label("Simulation speed: " + lab.Speed.ToString("F2") + "x");
            float speed = GUILayout.HorizontalSlider(lab.Speed, .1f, 2f);
            if (Mathf.Abs(speed - lab.Speed) > .001f)
                lab.SetSpeed(speed);
            if (GUILayout.Button("Reset pose and physics"))
            {
                if (lab.Replay >= 0)
                    lab.SelectReplay(lab.Replay);
                else
                    lab.Select(lab.Variant, lab.Arms);
            }
            if (GUILayout.Button("Native smoke tests (3 variants)"))
                lab.RunTests("smoke");
            if (GUILayout.Button("All animations, combinations and recordings"))
                lab.RunTests("full");
            if (GUILayout.Button("Game animation combinations (3 variants)"))
                lab.RunTests("combinations");
            if (GUILayout.Button("All emotes"))
                lab.RunTests("emotes");
            GUI.enabled = true;
            if (GUILayout.Button("Save screenshot"))
                lab.Screenshot();
            GUILayout.Space(8);
            GUILayout.Label(lab.Status);
            LabFrameMetrics metrics = lab.Actor.Metrics?.Current;
            if (metrics != null && metrics.ready)
            {
                GUILayout.Label("Garment: " + metrics.particles + " vertices / " + metrics.pinned + " pinned");
                GUILayout.Label("Coordinates: " + (metrics.reliable ? "verified" : "UNRESOLVED") + " / anchor " + (metrics.anchorError * 1000).ToString("F2") + " mm");
                GUILayout.Label("Displacement: " + (metrics.displacement * 1000).ToString("F1") + " mm | proxy depth: " + (metrics.penetration * 1000).ToString("F1") + " mm");
                GUILayout.Label("Intersection candidates: body " + metrics.bodyCrossings + ", coat " + metrics.coatCrossings + ", self " + metrics.selfCrossings);
                GUILayout.Label("Analysis: " + metrics.analysisMilliseconds.ToString("F1") + " ms (worker)");
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (Event.current.mousePosition.x > 310)
                lab.View.Input(Event.current);
            DrawParticles();
        }

        private void DrawParticles()
        {
            if (!lab.Particles || lab.Actor.Metrics?.Surface == null || Event.current.type != EventType.Repaint)
                return;
            Camera camera = lab.View.GetComponent<Camera>();
            Color previous = GUI.color;
            GUI.color = Color.cyan;
            foreach (Vector3 point in lab.Actor.Metrics.Surface)
            {
                Vector3 pixel = camera.WorldToScreenPoint(point);
                if (pixel.z > 0)
                    GUI.DrawTexture(new Rect(pixel.x - 1, Screen.height - pixel.y - 1, 3, 3), Texture2D.whiteTexture);
            }
            GUI.color = previous;
        }
    }
}

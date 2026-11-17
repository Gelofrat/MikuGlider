using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    [DefaultExecutionOrder(900)]
    public sealed class LabController : MonoBehaviour
    {
        internal LabActor Actor;
        internal LabVariant[] Variants;
        internal ClipEntry[] Clips;
        internal ReplayEntry[] Replays;
        internal AnimationClip[] Animations;
        internal int Variant;
        internal int Motion;
        internal int Replay = -1;
        internal bool Playing = true;
        internal bool Flight;
        internal bool Arms;
        internal bool Hat;
        internal bool Particles;
        internal float Speed = 1f;
        internal float Position;
        internal float Elbow;
        internal bool Ready;
        internal bool Testing;
        internal bool ManualEditing;
        internal string Status = "Loading pose garments...";
        internal string ReportDirectory;
        internal LabCamera View;
        private ReplayTrack track;
        private float warmUntil;
        private LabInterface panel;
        private bool automated;
        private int pendingExit = -1;

        private IEnumerator Start()
        {
            ReportDirectory = Argument("-labReports") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports"));
            Directory.CreateDirectory(ReportDirectory);
            automated = Argument("-labSuite") != null;
            Application.logMessageReceived += LogFailure;
            if (Argument("-labSuite") == "probe")
            {
                if (Argument("-labFilter") != "empty")
                    LabPhysicsProfile.Apply();
                for (int frame = 0; frame < 5; frame++)
                    yield return null;
                RequestQuit(0);
                yield break;
            }
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            LabPhysicsProfile.Apply();
            Variants = Load<LabCatalog>("catalog").variants;
            Clips = LabCombinationCatalog.Expand(Load<ClipCatalog>("animations").clips);
            Replays = Load<ReplayCatalog>("replays").tracks;
            Animations = new AnimationClip[Clips.Length];
            CreateStage();
            panel = gameObject.AddComponent<LabInterface>();
            panel.Bind(this);
            Select(0, false);
            Ready = true;
            Status = "Pose garments ready. Right drag: orbit. Wheel: zoom.";
            string suite = Argument("-labSuite");
            if (suite != null)
                StartCoroutine(gameObject.AddComponent<LabSuite>().Run(this, suite, true));
        }

        private static T Load<T>(string name) => JsonUtility.FromJson<T>(Resources.Load<TextAsset>("MikuPhysicsLab/" + name).text);

        internal static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        internal void Select(int index, bool arms)
        {
            if (arms)
            {
                Replay = -1;
                track = null;
            }
            Actor?.Dispose();
            Variant = index;
            Arms = arms;
            Actor = new LabActor(Variants[index], arms);
            Actor.SetHat(Hat);
            Position = 0f;
            warmUntil = Time.time + .5f;
            View.Distance = arms ? 1.1f : Flight ? 7.2f : 4.4f;
        }

        internal void SelectMotion(int index)
        {
            Replay = -1;
            track = null;
            Motion = index;
            Arms = Clips[index].rig == "arms";
            Flight = Clips[index].flight;
            Select(Variant, Arms);
        }

        internal AnimationClip GetAnimation(int index)
        {
            if (Animations[index] == null)
                Animations[index] = Resources.Load<AnimationClip>("MikuPhysicsLab/Animations/" + Clips[index].resource);
            return Animations[index];
        }

        internal void SelectReplay(int index, int variant = -1)
        {
            ReplayEntry entry = Replays[index];
            track = new ReplayTrack(entry.key);
            Select(variant >= 0 ? variant : entry.variant, false);
            string[] names = Actor.Rig.Bones.Select(b => b.name).ToArray();
            if (!names.SequenceEqual(entry.boneNames))
                throw new InvalidDataException("Replay bone order differs from the native Glider rig.");
            Replay = index;
        }

        internal float Duration => track != null ? track.Duration : Clips[Motion].segments != null ?
            Clips[Motion].segments.Sum(segment => segment.duration) : GetAnimation(Motion).length / Clips[Motion].speed;

        private void Update()
        {
            if (pendingExit >= 0)
            {
                int code = pendingExit;
                pendingExit = -1;
                Application.Quit(code);
                return;
            }
            if (!Ready || Actor == null)
                return;
            if (ManualEditing) return;
            if (Time.time < warmUntil)
            {
                Actor.Sample(null, 0, false);
                return;
            }
            if (Playing)
                Position += Time.deltaTime;
            float duration = Mathf.Max(.01f, Duration);
            if (Position > duration && !Testing)
                Position %= duration;
            if (track != null)
            {
                Actor.Rig.Restore();
                uint flags = track.Sample(Position, Actor.Rig.Bones, Actor.Rig.BoneOrder, Actor.Root.transform);
                Actor.Owner.Wingsuit.IsFlying = (flags & 1) != 0;
            }
            else if (Clips[Motion].segments != null)
            {
                Actor.AnimateCombination(this, Clips[Motion], Position);
            }
            else
            {
                if (Arms && Clips[Motion].rig != "arms")
                    Actor.Sample(null, 0, false, Elbow);
                else
                {
                    string baseKey = Clips[Motion].baseKey ?? "idle";
                    int baseIndex = Arms && baseKey == "idle" ? Array.FindIndex(Clips, c => c.source == "Default Animation Set.asset:idle") : Array.FindIndex(Clips, c => c.key == baseKey);
                    if (baseIndex < 0)
                        throw new InvalidDataException("Missing native baseline animation: " + (Arms ? "arms idle" : baseKey));
                    Actor.Animate(Clips[Motion], GetAnimation(Motion), GetAnimation(baseIndex), Clips[baseIndex].speed, Position, Flight, Playing);
                }
            }
            if (Actor.Metrics != null)
                Actor.Metrics.DrawParticles = Particles;
        }

        internal void SetSpeed(float value)
        {
            Speed = value;
            Time.timeScale = value;
        }

        internal void RequestQuit(int code) => pendingExit = code;

        internal void RunTests(string mode)
        {
            if (!Testing)
                StartCoroutine(gameObject.AddComponent<LabSuite>().Run(this, mode, false));
        }

        internal void Screenshot()
        {
            string file = Path.Combine(ReportDirectory, "view-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            LabCapture.Save(View.GetComponent<Camera>(), file);
            Status = file;
        }

        private void LogFailure(string message, string stack, LogType type)
        {
            if (!automated || (type != LogType.Exception && type != LogType.Error && type != LogType.Assert))
                return;
            File.AppendAllText(Path.Combine(ReportDirectory, "runtime-errors.txt"), DateTime.UtcNow.ToString("O") + "\n" + message + "\n" + stack + "\n");
            if (!Testing)
                Application.Quit(3);
        }

        private void CreateStage()
        {
            RenderSettings.ambientLight = new Color(.8f, .85f, .9f);
            var cameraObject = new GameObject("Inspection camera", typeof(Camera), typeof(LabCamera), typeof(LabRenderClock));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.backgroundColor = new Color(.085f, .10f, .13f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 200f;
            camera.fieldOfView = 40f;
            camera.allowHDR = false;
            View = cameraObject.GetComponent<LabCamera>();
            View.Lab = this;
            var light = new GameObject("Key light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(35, -25, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Isolated floor";
            floor.transform.localScale = Vector3.one * 3f;
            var material = new Material(Resources.Load<Shader>("MikuLabAlbedo"));
            material.color = new Color(.16f, .19f, .23f);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", material.color);
            floor.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void OnDestroy()
        {
            Actor?.Dispose();
            Application.logMessageReceived -= LogFailure;
            LabAssetLibrary.Clear();
            Time.timeScale = 1;
            Time.captureDeltaTime = 0;
        }
    }

    [DefaultExecutionOrder(1200)]
    public sealed class LabCamera : MonoBehaviour
    {
        internal LabController Lab;
        internal float Distance = 3.8f;
        internal float Yaw = 180f;
        internal float Pitch = 5f;
        internal float Vertical;
        internal bool AlternateAngle;
        internal bool FirstPersonView = true;
        internal const float NativeFirstPersonFieldOfView = 55f;
        private void LateUpdate()
        {
            Refresh();
        }

        internal void Refresh()
        {
            if (Lab?.Actor == null)
                return;
            bool firstPerson = Lab.Actor.Arms && (LabController.Argument("-labFirstPerson") != null ||
                FirstPersonView && LabController.Argument("-labWristCloseup") == null);
            GetComponent<LabRenderClock>()?.SetFirstPersonAspect(firstPerson);
            if (firstPerson)
            {
                transform.SetPositionAndRotation(Lab.Actor.Root.transform.position, Quaternion.identity);
                float fieldOfView = float.TryParse(LabController.Argument("-labFirstPersonFov"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                    out float requestedFov) ? Mathf.Clamp(requestedFov, 30, 140) : NativeFirstPersonFieldOfView;
                GetComponent<Camera>().fieldOfView = fieldOfView;
                return;
            }
            GetComponent<Camera>().fieldOfView = 40;
            if (!Lab.Actor.Arms && LabController.Argument("-labBottomView") != null)
            {
                Vector3 bottomFocus = Lab.Actor.Root.transform.TransformPoint(new Vector3(0, 1.45f, 0));
                Quaternion bottomView = Lab.Actor.Root.transform.rotation * Quaternion.Euler(-65, 180, 0);
                transform.SetPositionAndRotation(bottomFocus - bottomView * Vector3.forward * 1.5f, bottomView);
                return;
            }
            string wristSide = LabController.Argument("-labWristCloseup");
            if (wristSide != null)
            {
                Transform[] nodes = Lab.Actor.Arms ? Lab.Actor.Rig.Nodes : Lab.Actor.Rig.Bones;
                string wristName = Lab.Actor.Arms ? "jnt_" + wristSide + "_hand_01" : "joint_Wrist_" + wristSide;
                string fingerName = Lab.Actor.Arms ? "jnt_" + wristSide + "_MiddleA" : "joint_Middle01_" + wristSide;
                Transform wrist = Array.Find(nodes, bone => bone.name == wristName);
                Transform finger = Array.Find(nodes, bone => bone.name == fingerName);
                Vector3 handFocus = Vector3.Lerp(wrist.position, finger.position, .35f);
                float handYaw = float.TryParse(LabController.Argument("-labWristYaw"), out float requestedYaw) ? requestedYaw : Yaw;
                Quaternion handView = Lab.Actor.Root.transform.rotation * Quaternion.Euler(AlternateAngle ? Pitch : 15, AlternateAngle ? Yaw : handYaw, 0);
                transform.SetPositionAndRotation(handFocus - handView * Vector3.forward * (Lab.Actor.Arms ? .35f : .65f), handView);
                return;
            }
            bool vest = !Lab.Actor.Arms && LabController.Argument("-labVestCloseup") != null;
            Vector3 focus = Lab.Actor.Focus + Vector3.up * (vest ? .3f : Vertical);
            Quaternion angle = Lab.Actor.Root.transform.rotation * (vest && !AlternateAngle ? Quaternion.Euler(0, 180, 0) : Quaternion.Euler(Pitch, Yaw, 0));
            transform.SetPositionAndRotation(focus - angle * Vector3.forward * (vest ? 1.25f : Distance), angle);
        }

        internal void Input(Event input)
        {
            if (input.type == EventType.ScrollWheel)
            {
                Distance = Mathf.Clamp(Distance * (1f + input.delta.y * .06f), .15f, 15f);
                input.Use();
            }
            if (input.type == EventType.MouseDrag && input.button == 1)
            {
                Yaw += input.delta.x * .4f;
                Pitch = Mathf.Clamp(Pitch + input.delta.y * .3f, -80f, 80f);
                input.Use();
            }
            if (input.type == EventType.MouseDrag && input.button == 2)
            {
                Vertical -= input.delta.y * .002f * Distance;
                input.Use();
            }
        }
    }
}

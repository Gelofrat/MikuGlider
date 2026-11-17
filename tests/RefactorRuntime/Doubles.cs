using System;
using System.Collections.Generic;
using MikuGlider;
using Pigeon.Movement;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object value) { if (value != null) value.Destroyed = true; }
        public static void DontDestroyOnLoad(Object value) { }
    }
    public class Mesh : Object { }
    public class Renderer : Object { public bool forceRenderingOff; }
    public class Texture : Object { }
    public class Material : Object
    {
        private readonly Dictionary<string, Texture> textures = new Dictionary<string, Texture>();
        public void SetTexture(string name, Texture texture) => textures[name] = texture;
        public Texture GetTexture(string name) => textures.TryGetValue(name, out Texture texture) ? texture : null;
    }
    public class Sprite : Object { }
    public struct Quaternion
    {
        public float pitch, yaw, roll;
        public static Quaternion identity => default;
        public static Quaternion Euler(float x, float y, float z) => new Quaternion { pitch = x, yaw = y, roll = z };
        public static Quaternion RotateTowards(Quaternion from, Quaternion to, float step)
        {
            float delta = Mathf.DeltaAngle(from.yaw, to.yaw);
            return Euler(0f, from.yaw + Math.Sign(delta) * Math.Min(Math.Abs(delta), step), 0f);
        }
        public static Quaternion operator *(Quaternion first, Quaternion second) => Euler(first.pitch + second.pitch, first.yaw + second.yaw, first.roll + second.roll);
        public static Vector3 operator *(Quaternion rotation, Vector3 value)
        {
            float angle = rotation.yaw * (float)Math.PI / 180f;
            return new Vector3(value.x * (float)Math.Cos(angle) + value.z * (float)Math.Sin(angle), value.y,
                value.z * (float)Math.Cos(angle) - value.x * (float)Math.Sin(angle));
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 forward => new Vector3(0f, 0f, 1f);
        public static Vector3 operator +(Vector3 first, Vector3 second) => new Vector3(first.x + second.x, first.y + second.y, first.z + second.z);
        public static Vector3 operator -(Vector3 first, Vector3 second) => first + -second;
        public static Vector3 operator -(Vector3 value) => new Vector3(-value.x, -value.y, -value.z);
        public static Vector3 operator *(Vector3 value, float scale) => new Vector3(value.x * scale, value.y * scale, value.z * scale);
    }
    public struct Vector2
    {
        public float sqrMagnitude, x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; sqrMagnitude = x * x + y * y; }
    }
    public class Transform
    {
        public Renderer[] Renderers = Array.Empty<Renderer>();
        public T[] GetComponentsInChildren<T>(bool inactive) where T : class => Array.ConvertAll(Renderers, r => r as T);
        public void GetComponentsInChildren<T>(bool inactive, System.Collections.Generic.List<T> results) where T : class
        {
            results.Clear();
            results.AddRange(GetComponentsInChildren<T>(inactive));
        }
        public Quaternion localRotation;
        private Vector3 localPosition;
        public Transform parent;
        public Vector3 localEulerAngles { get => new Vector3(localRotation.pitch, localRotation.yaw, localRotation.roll); set => localRotation = Quaternion.Euler(value.x, value.y, value.z); }
        public Vector3 eulerAngles => new Vector3(rotation.pitch, rotation.yaw, rotation.roll);
        public Quaternion rotation
        {
            get => parent == null ? localRotation : parent.rotation * localRotation;
            set => localRotation = parent == null ? value : Quaternion.Euler(value.pitch - parent.rotation.pitch, value.yaw - parent.rotation.yaw, value.roll - parent.rotation.roll);
        }
        public Vector3 position
        {
            get => parent == null ? localPosition : parent.position + parent.rotation * localPosition;
            set => localPosition = parent == null ? value : Quaternion.Euler(0f, -parent.rotation.yaw, 0f) * (value - parent.position);
        }
        public Vector3 TransformPoint(Vector3 point) => position + rotation * point;
        public void SetPositionAndRotation(Vector3 point, Quaternion value) { position = point; rotation = value; }
    }
    public static class Mathf
    {
        public static float Repeat(float value, float length) => value - (float)Math.Floor(value / length) * length;
        public static float DeltaAngle(float first, float second) { float delta = Repeat(second - first, 360f); return delta > 180f ? delta - 360f : delta; }
        public static float SmoothStep(float first, float second, float value) { value = Math.Max(0f, Math.Min(1f, value)); return first + (second - first) * value * value * (3f - 2f * value); }
        public static float Lerp(float first, float second, float value) => first + (second - first) * value;
        public const float Rad2Deg = 180f / (float)Math.PI;
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
    }
    public static class Time { public static float timeScale = 1f; public static float time; public static float deltaTime = 1f / 60f; }
    public static class Cursor { public static bool visible; }
}

namespace UnityEngine.InputSystem
{
    public enum Key { None, J, K, N, F5, F6 }
    public sealed class Keyboard
    {
        public static Keyboard current = new Keyboard();
        public Button f5Key = new Button();
        public Button jKey = new Button();
        public Button kKey = new Button();
        public Button nKey = new Button();
        public Button f6Key = new Button();
        public Button this[Key key] => key == Key.J ? jKey : key == Key.K ? kKey : key == Key.N ? nKey : key == Key.F5 ? f5Key : f6Key;
    }
    public sealed class Button
    {
        public bool wasPressedThisFrame;
        public bool Pressed;
        public bool IsPressed() => Pressed;
    }
}

namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T> { public T Value { get; set; } }
    public sealed class ConfigFile
    {
        private readonly Dictionary<string, object> values = new Dictionary<string, object>();
        public ConfigEntry<T> Bind<T>(string section, string name, T value, string description)
        {
            string key = section + "." + name;
            if (!values.TryGetValue(key, out object entry))
                values.Add(key, entry = new ConfigEntry<T> { Value = value });
            return (ConfigEntry<T>)entry;
        }
    }
}

namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public readonly List<string> Errors = new List<string>();
        public void LogInfo(object value) { }
        public void LogError(object value) => Errors.Add(value.ToString());
    }
}

public enum Rarity { Standard, Exotic }
public class Character
{
    public AnimationSet ThirdPersonAnimationSet = new AnimationSet();
    public AnimationSet ThirdPersonCrouchAnimationSet = new AnimationSet();
}
public class AnimationSet
{
    public PlayerAnimation.Key slide = new PlayerAnimation.Key();
    public PlayerAnimation.Key jumpLoop = new PlayerAnimation.Key();
    public PlayerAnimation.Key run = new PlayerAnimation.Key();
    public PlayerAnimation.Key idle = new PlayerAnimation.Key();
}
public class PlayerAnimation
{
    public class Key { }
    public UnityEngine.Transform WeaponRoot, MagRoot, Mag1Root;
    public ThirdPersonAnimator SelfThirdPersonAnimator = new ThirdPersonAnimator();
}
public class ThirdPersonRig { }
public class ThirdPersonAnimator
{
    public object CurrentState;
    public PlayerAnimation.Key CurrentStateKey;
    public ThirdPersonRig Rig = new ThirdPersonRig();
    public UnityEngine.Quaternion BodyRotation;
    public void SetTransform(UnityEngine.Vector3 value, UnityEngine.Quaternion rotation) { BodyRotation = rotation; }
    public void SetState(PlayerAnimation.Key value) { CurrentState = value; CurrentStateKey = value; }
}
public class Wingsuit { public bool IsFlying; public PlayerAnimation.Key Flight = new PlayerAnimation.Key(); }
public sealed class Highlighter
{
    public static Highlighter Instance;
    public void UpdateOverlays(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation) { }
}
public class Upgrade : UnityEngine.Object { public int Id; }
public class SkinUpgrade : Upgrade { public UnityEngine.Sprite Icon = new UnityEngine.Sprite(); }
public struct UpgradeID
{
    public int ID;
    public string Guid;
    public UpgradeID(int id, string guid) { ID = id; Guid = guid; }
}
public sealed class PlayerData
{
    public enum UnlockFlags { Hidden }
    public sealed class Unlocked { public List<object> Instances = new List<object>(); }
    public static PlayerData Instance;
    public static readonly Dictionary<int, Upgrade> Registered = new Dictionary<int, Upgrade>();
    public static readonly Dictionary<int, int> Faults = new Dictionary<int, int>();
    public readonly Dictionary<int, Unlocked> Items = new Dictionary<int, Unlocked>();
    public int Collections;
    public int Saves;
    public int GrantFailures, SaveFailures, SilentGrantFailures;
    public bool FailAfterCollect;
    public static void RegisterUpgrade(SkinUpgrade skin, Character character, string guid)
    {
        Faults.TryGetValue(skin.Id, out int fault);
        if (fault == 1) throw new Exception("before publish");
        Registered.Add(skin.Id, skin);
        if (fault == 2) throw new Exception("after publish");
    }
    public static Upgrade GetUpgradeFromID(UpgradeID id) => Registered.TryGetValue(id.ID, out Upgrade value) ? value : null;
    public static Unlocked GetUnlockedInstances(Upgrade upgrade) => Instance.Items.TryGetValue(upgrade.Id, out Unlocked value) ? value : null;
    public static void CollectInstance(Character character, Upgrade skin, UnlockFlags flags)
    {
        if (Instance.SilentGrantFailures > 0) { Instance.SilentGrantFailures--; return; }
        if (Instance.GrantFailures > 0 && !Instance.FailAfterCollect) { Instance.GrantFailures--; throw new Exception("grant unavailable"); }
        Instance.Collections++;
        var unlocked = new Unlocked();
        unlocked.Instances.Add(new object());
        Instance.Items.Add(skin.Id, unlocked);
        if (Instance.GrantFailures > 0) { Instance.GrantFailures--; throw new Exception("grant failed after mutation"); }
    }
    public static void SaveData()
    {
        if (Instance.SaveFailures > 0) { Instance.SaveFailures--; throw new Exception("save unavailable"); }
        Instance.Saves++;
    }
}

namespace Pigeon.Movement
{
    public sealed class Player
    {
        public object SelectedGear;
        public object HeldGear;
        public delegate void MoveModifier(ref UnityEngine.Vector2 value);
        private MoveModifier moveModifiers;
        public int MovementSubscriptions;
        public event MoveModifier ModifyRawMoveInput
        {
            add { moveModifiers += value; MovementSubscriptions++; }
            remove { moveModifiers -= value; }
        }
        public int MovementSubscribers => moveModifiers?.GetInvocationList().Length ?? 0;
        public UnityEngine.Vector3 ReadMovement(UnityEngine.Vector2 input)
        {
            UnityEngine.Quaternion basis = transform.rotation;
            moveModifiers?.Invoke(ref input);
            return basis * new UnityEngine.Vector3(input.x, 0f, input.y);
        }
        public static Player LocalPlayer;
        public bool IsAlive = true;
        public object CurrentVehicle;
        public int MovementControlLocks, GearLocks, InteractionLocks, FiringLocks;
        public PlayerAnimation Animator = new PlayerAnimation();
        public UnityEngine.Transform transform = new UnityEngine.Transform();
        public Character Character = new Character();
        public bool Crouching, Sliding, Grounded = true;
        public void LockFiring(bool locked) { FiringLocks += locked ? 1 : -1; }
        public void SetCameraRotation_Owner(float x, float y) { PlayerLook.Instance.transform.localRotation = UnityEngine.Quaternion.Euler(x, 0f, 0f); }
    }
    public sealed class PlayerLook
    {
        public static PlayerLook Instance;
        public static bool TempExitThirdPersonWithMovement;
        public static UnityEngine.Vector3 Position, Forward;
        public static UnityEngine.Quaternion Rotation;
        internal static OrbitCameraController Hook;
        public bool IsSpectating, IsInThirdPerson, IsTextChatOpen;
        public UnityEngine.Transform transform = new UnityEngine.Transform { parent = Player.LocalPlayer.transform };
        public UnityEngine.Quaternion GlobalRotation;
        public UnityEngine.Vector3 AddedPosition;
        public UnityEngine.Vector2 MouseInput;
        public float OrbitDistance = 2.5f;
        public float MinDistance = 1.5f, MaxDistance = 5f;
        public int RotationLocksY;
        public int EnableCount, DisableCount;
        public float XLookRotation;
        public void OverrideOrbitDistanceBounds(float min, float max) { MinDistance = min; MaxDistance = max; }
        public void AddPosition(UnityEngine.Vector3 value) => AddedPosition += value;
        public void EnableThirdPerson(bool a, bool b, bool c)
        {
            Hook?.BeforeEnable(this);
            IsInThirdPerson = true;
            EnableCount++;
            Player.LocalPlayer.MovementControlLocks++;
            Player.LocalPlayer.GearLocks++;
            Player.LocalPlayer.InteractionLocks++;
            Player.LocalPlayer.LockFiring(true);
        }
        public void DisableThirdPerson()
        {
            Hook?.BeforeDisable(this);
            IsInThirdPerson = false;
            DisableCount++;
            Player.LocalPlayer.MovementControlLocks--;
            Player.LocalPlayer.GearLocks--;
            Player.LocalPlayer.InteractionLocks--;
            Player.LocalPlayer.LockFiring(false);
        }
    }
    public static class PlayerInput
    {
        public static ControlSet Controls = new ControlSet();
        public static UnityEngine.Vector2 Movement;
        public static UnityEngine.Vector2 MoveInput() => Movement;
    }
    public sealed class ControlSet { public PlayerControls Player = new PlayerControls(); }
    public sealed class PlayerControls
    {
        public bool enabled = true;
        public UnityEngine.InputSystem.Button Fire = new UnityEngine.InputSystem.Button();
    }
}

namespace MikuGlider
{
    internal static class MikuGliderPlugin { public const string Guid = "horse.mycopunk.mikuglider"; }
    internal sealed class SkinAssets : IDisposable
    {
        internal sealed class EmptyCorrections { internal object[] meshes = Array.Empty<object>(); }
        internal readonly EmptyCorrections Corrections = new EmptyCorrections();
        internal static readonly List<SkinAssets> Loaded = new List<SkinAssets>();
        internal bool Disposed;
        internal static SkinAssets Load(SkinVariantDefinition definition)
        {
            var assets = new SkinAssets();
            Loaded.Add(assets);
            return assets;
        }
        public void Dispose() => Disposed = true;
    }
    internal static class SkinFactory
    {
        internal static readonly List<RegisteredSkin> Created = new List<RegisteredSkin>();
        internal static RegisteredSkin Create(SkinVariantDefinition definition, SkinAssets assets, Character character)
        {
            var skin = new RegisteredSkin(definition, new SkinUpgrade { Id = definition.NumericId }, assets,
                new UnityEngine.Mesh(), new UnityEngine.Mesh(), new UnityEngine.Material());
            Created.Add(skin);
            return skin;
        }
    }
    internal static class NativeSkinAccess
    {
        internal static void ValidateAvailableId(int id)
        {
            if (PlayerData.Registered.ContainsKey(id)) throw new InvalidOperationException("collision");
        }
        internal static PlayerAnimation.Key GetFlightAnimation(Wingsuit wingsuit) => wingsuit.Flight;
    }
    internal static class GliderEquipment
    {
        internal static Wingsuit Wings;
        internal static Wingsuit FindWingsuit(Pigeon.Movement.Player player) => Wings;
    }
    internal static class NativeCameraAccess
    {
        internal static void ValidateLayout() { }
        internal static UnityEngine.Quaternion GetGlobalRotation(PlayerLook look) => look.GlobalRotation;
        internal static void SetGlobalRotation(PlayerLook look, UnityEngine.Quaternion value) => look.GlobalRotation = value;
        internal static UnityEngine.Vector2 GetDistanceBounds(PlayerLook look) => new UnityEngine.Vector2(look.MinDistance, look.MaxDistance);
        internal static float GetOrbitDistance(PlayerLook look) => look.OrbitDistance;
        internal static void SetOrbitDistance(PlayerLook look, float value) => look.OrbitDistance = value;
        internal static float ReadHorizontalInput(PlayerLook look) => look.RotationLocksY == 0 ? look.MouseInput.x : 0f;
    }
    internal sealed class ShoulderReticle
    {
        internal static bool Visible;
        internal void Update(PlayerLook look, bool visible) => Visible = visible;
        internal void Hide() => Visible = false;
    }
    internal static class ShoulderCamera
    {
        internal static int Calls;
        internal static UnityEngine.Vector3 ConstrainPosition(PlayerLook look, UnityEngine.Vector3 position) { Calls++; return position; }
    }
    internal sealed class DalekMissionAudio
    {
        internal bool Active;
        internal DalekMissionAudio(AudioSettings settings, BepInEx.Logging.ManualLogSource logger) { }
        internal void SetActive(bool active) => Active = active;
    }
    internal sealed class SkinAppearanceController
    {
        internal SkinAppearanceController() { }
        internal SkinAppearanceController(SkinRegistry registry) { }
        internal void EnsureRigVisuals(ThirdPersonRig rig, Pigeon.Movement.Player player) { }
    }
}

namespace MikuGlider
{
    internal sealed class ThirdPersonWeaponVisual
    {
        internal static bool Visible;
        internal void Update(Pigeon.Movement.Player player, ThirdPersonAnimator animator, bool visible) { Visible = visible; }
        internal void Clear() { Visible = false; }
    }
}

public class Gun { public UnityEngine.Transform GunModel; public UnityEngine.Renderer[] SkinRenderers; }

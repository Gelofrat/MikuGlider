using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

internal sealed class OrbitCameraController
{
    private readonly SkinAppearanceController appearance;
    private readonly HotkeySettings hotkeys;
    private PlayerLook activeLook;
    private Player owner;
    private Character character;
    private Quaternion bodyRotation;
    private bool entering;
    private readonly ShoulderReticle reticle = new ShoulderReticle();
    private readonly ThirdPersonWeaponVisual weapon = new ThirdPersonWeaponVisual();
    private readonly WeaponVisibility weaponVisibility = new WeaponVisibility();
    private CameraFrameOverrides frameOverrides;
    private float orbitYaw;
    private float shoulderEnteredAt;
    private float shoulderStartDistance;

    internal ThirdPersonCameraMode Mode { get; private set; }

    internal OrbitCameraController(SkinAppearanceController appearance, HotkeySettings hotkeys)
    {
        this.appearance = appearance;
        this.hotkeys = hotkeys;
    }

    internal void UpdateInput()
    {
        if (owner != null && (activeLook == null || ShouldStop()))
            Stop();
        if (owner != null && Mode == ThirdPersonCameraMode.FreeOrbit)
        {
            ReleaseMovementInput();
            owner.ModifyRawMoveInput += AdjustMovementInput;
        }
        if (CanUseInput(PlayerLook.Instance) && hotkeys.WeaponPressed)
            weaponVisibility.Toggle();
        weaponVisibility.Update(Player.LocalPlayer);
        if (!hotkeys.PerspectivePressed)
            return;

        if (!CanUseInput(PlayerLook.Instance))
            return;

        if (activeLook != null)
        {
            if (Mode == ThirdPersonCameraMode.FreeOrbit)
                EnterShoulderMode();
            else
                Stop();
            return;
        }

        Player player = Player.LocalPlayer;
        PlayerLook look = PlayerLook.Instance;
        if (CanStart(player, look))
            Start(player, look);
    }

    internal void Stop()
    {
        weapon.Clear();
        weaponVisibility.Restore();
        if (activeLook != null)
            activeLook.DisableThirdPerson();
        else
        {
            ReleaseMovementInput();
            owner = null;
            character = null;
            Mode = ThirdPersonCameraMode.FirstPerson;
            reticle.Hide();
        }
    }

    internal void BeforeDisable(PlayerLook look)
    {
        if (look != activeLook)
            return;

        ReleaseFrameOverrides(look);
        ReleaseMovementInput();
        weapon.Clear();
        reticle.Hide();
        if (owner != null)
        {
            owner.MovementControlLocks++;
            owner.GearLocks++;
            owner.InteractionLocks++;
            owner.LockFiring(true);
        }

        activeLook = null;
        owner = null;
        character = null;
        Mode = ThirdPersonCameraMode.FirstPerson;
    }

    internal void BeforeEnable(PlayerLook look)
    {
        if (!entering && look == activeLook)
            Stop();
    }

    internal void BeforeCameraUpdate(PlayerLook look)
    {
        if (look != activeLook)
            return;

        ReleaseFrameOverrides(look);
        PlayerLook.TempExitThirdPersonWithMovement = false;
        frameOverrides = new CameraFrameOverrides(look);
        if (Mode == ThirdPersonCameraMode.FreeOrbit)
        {
            orbitYaw = Mathf.Repeat(orbitYaw + NativeCameraAccess.ReadHorizontalInput(look), 360f);
            frameOverrides.SetFreeOrbit(orbitYaw);
        }
        else
        {
            float transition = Mathf.SmoothStep(0f, 1f, (Time.time - shoulderEnteredAt) / 0.22f);
            frameOverrides.SetShoulder(new Vector3(0.45f, 0.22f, 0f) * transition);
            if (transition < 1f)
                NativeCameraAccess.SetOrbitDistance(look, Mathf.Lerp(shoulderStartDistance, 1.85f, transition));
            else if (shoulderStartDistance >= 0f)
            {
                NativeCameraAccess.SetOrbitDistance(look, 1.85f);
                shoulderStartDistance = -1f;
            }
        }
    }

    internal void ReleaseFrameOverrides(PlayerLook look)
    {
        if (frameOverrides == null || frameOverrides.Look != look)
            return;

        frameOverrides.Dispose();
        frameOverrides = null;
    }

    internal void AfterCameraUpdate(PlayerLook look)
    {
        if (look == PlayerLook.Instance)
            weaponVisibility.Update(Player.LocalPlayer);
        ReleaseFrameOverrides(look);
        if (look != activeLook || owner == null || owner.Animator == null)
            return;

        Vector3 cameraPosition = look.transform.position;
        Quaternion cameraRotation = look.transform.rotation;
        if (Mode == ThirdPersonCameraMode.Shoulder)
            cameraPosition = ShoulderCamera.ConstrainPosition(look, cameraPosition);

        owner.SetCameraRotation_Owner(look.XLookRotation, owner.transform.localEulerAngles.y);
        look.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        PlayerLook.Position = cameraPosition;
        PlayerLook.Rotation = cameraRotation;
        PlayerLook.Forward = cameraRotation * Vector3.forward;
        bodyRotation = owner.transform.localRotation;
        reticle.Update(look, Mode == ThirdPersonCameraMode.Shoulder && CanUseInput(look));
        if (Mode == ThirdPersonCameraMode.Shoulder && Highlighter.Instance != null)
            Highlighter.Instance.UpdateOverlays(cameraPosition, cameraRotation);

        ThirdPersonAnimator self = owner.Animator.SelfThirdPersonAnimator;
        if (self == null)
            return;

        self.SetTransform(new Vector3(float.NaN, 0f, 0f), bodyRotation);
        PlayerAnimation.Key animation = SelectAnimation();
        if (animation != null && (self.CurrentState == null || animation != self.CurrentStateKey))
            self.SetState(animation);

        appearance.EnsureRigVisuals(self.Rig, owner);
        weapon.Update(owner, self, weaponVisibility.Visible);
    }

    internal bool ControlsAnimator(ThirdPersonAnimator animator)
    {
        return activeLook != null && activeLook.IsInThirdPerson && owner != null && owner.Animator != null &&
            animator == owner.Animator.SelfThirdPersonAnimator;
    }

    private void Start(Player player, PlayerLook look)
    {
        NativeCameraAccess.ValidateLayout();
        orbitYaw = look.transform.eulerAngles.y;
        entering = true;
        try
        {
            look.EnableThirdPerson(true, false, true);
        }
        finally
        {
            entering = false;
        }

        if (!look.IsInThirdPerson)
            return;

        activeLook = look;
        owner = player;
        character = player.Character;
        owner.ModifyRawMoveInput += AdjustMovementInput;
        Mode = ThirdPersonCameraMode.FreeOrbit;
        bodyRotation = player.transform.localRotation;
        player.MovementControlLocks--;
        player.GearLocks--;
        player.InteractionLocks--;
        player.LockFiring(false);

        ThirdPersonAnimator self = player.Animator.SelfThirdPersonAnimator;
        if (self != null)
            appearance.EnsureRigVisuals(self.Rig, player);
    }

    private void EnterShoulderMode()
    {
        Vector3 cameraPosition = activeLook.transform.position;
        Quaternion cameraRotation = activeLook.transform.rotation;
        owner.transform.rotation = Quaternion.Euler(0f, orbitYaw, 0f);
        activeLook.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        bodyRotation = owner.transform.localRotation;
        shoulderStartDistance = NativeCameraAccess.GetOrbitDistance(activeLook);
        shoulderEnteredAt = Time.time;
        Mode = ThirdPersonCameraMode.Shoulder;
    }

    private bool ShouldStop()
    {
        return owner == null || !owner.IsAlive || activeLook.IsSpectating ||
            Player.LocalPlayer != owner || owner.CurrentVehicle != null || owner.Character != character;
    }

    private void AdjustMovementInput(ref Vector2 input)
    {
        if (Mode != ThirdPersonCameraMode.FreeOrbit || owner == null || activeLook == null ||
            !CanUseInput(activeLook) || owner.MovementControlLocks > 0 || input.sqrMagnitude < 0.0001f ||
            GliderEquipment.FindWingsuit(owner)?.IsFlying == true)
            return;

        float previousYaw = owner.transform.eulerAngles.y;
        Vector3 direction = Quaternion.Euler(0f, orbitYaw, 0f) * new Vector3(input.x, 0f, input.y);
        Vector3 local = Quaternion.Euler(0f, -previousYaw, 0f) * direction;
        input = new Vector2(local.x, local.z);
        if (!owner.Sliding)
        {
            float targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            owner.transform.rotation = Quaternion.RotateTowards(owner.transform.rotation,
                Quaternion.Euler(0f, targetYaw, 0f), 720f * Time.deltaTime);
        }
    }

    private void ReleaseMovementInput()
    {
        if (owner != null)
            owner.ModifyRawMoveInput -= AdjustMovementInput;
    }

    private static bool CanStart(Player player, PlayerLook look)
    {
        return player != null && player.Animator != null && look != null && player.IsAlive &&
            !look.IsSpectating && !look.IsInThirdPerson && !look.IsTextChatOpen && Time.timeScale != 0f &&
            !Cursor.visible && PlayerInput.Controls.Player.enabled && player.CurrentVehicle == null &&
            player.MovementControlLocks == 0;
    }

    private static bool CanUseInput(PlayerLook look)
    {
        return look != null && !look.IsTextChatOpen && Time.timeScale != 0f &&
            !Cursor.visible && PlayerInput.Controls.Player.enabled;
    }

    private PlayerAnimation.Key SelectAnimation()
    {
        Wingsuit wingsuit = GliderEquipment.FindWingsuit(owner);
        if (wingsuit?.IsFlying == true)
            return NativeSkinAccess.GetFlightAnimation(wingsuit);

        var animations = owner.Crouching
            ? owner.Character.ThirdPersonCrouchAnimationSet
            : owner.Character.ThirdPersonAnimationSet;
        if (owner.Sliding)
            return animations.slide;
        if (!owner.Grounded)
            return animations.jumpLoop;

        return IsMoving() ? animations.run : animations.idle;
    }

    private static bool IsMoving()
    {
        return PlayerInput.MoveInput().sqrMagnitude > 0.01f;
    }
}

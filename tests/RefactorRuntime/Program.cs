using System;
using System.Collections.Generic;
using BepInEx.Logging;
using MikuGlider;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

internal static class Program
{
    private static int assertions;

    private static int Main()
    {
        try
        {
            foreach (SkinVariantDefinition variant in SkinCatalog.Variants)
            {
                Assert(variant.CorrectFirstPersonLeftWrist, variant.Key + " support wrist correction");
                Assert(variant.CorrectFirstPersonRightWrist == (variant.Key == "MikuGliderV2"), variant.Key + " right wrist correction scope");
            }
            RegistrationAndInventory();
            RegistrationFailures();
            InventoryRetries();
            RuntimeSessions();
            PreviewMaterialOrder();
            CameraLocksAndMovement();
            CameraModesAndFreeOrbit();
            CameraRelativeMovement();
            WeaponSlotsAndVisibility();
            FirstPersonWeaponAlwaysVisible();
            ConfigurableHotkeys();
            Console.WriteLine("RUNTIME_CHECKS_PASS assertions=" + assertions);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static SkinVariantDefinition Variant(int id, string key)
    {
        return new SkinVariantDefinition(id, key, key, Rarity.Exotic, key);
    }

    private static void PreviewMaterialOrder()
    {
        var faceTexture = new Texture();
        var bodyTexture = new Texture();
        var face = new Material();
        var body = new Material();
        face.SetTexture("_BaseMap", faceTexture);
        body.SetTexture("_BaseMap", bodyTexture);
        var materials = new List<Material> { face, body };
        materials.Reverse();
        SkinMaterialOrder.Normalize(materials, faceTexture, bodyTexture);
        Assert(materials[0] == face && materials[1] == body, "first preview repairs old native mesh reversal");
        SkinMaterialOrder.Normalize(materials, faceTexture, bodyTexture);
        Assert(materials[0] == face && materials[1] == body, "repeated preview preserves correct order");
        Assert(face.GetTexture("_BaseMap") == faceTexture && body.GetTexture("_BaseMap") == bodyTexture,
            "preview correction retains pooled material properties");

        var otherFaceTexture = new Texture();
        var otherBodyTexture = new Texture();
        face.SetTexture("_BaseMap", otherFaceTexture);
        body.SetTexture("_BaseMap", otherBodyTexture);
        materials.Reverse();
        SkinMaterialOrder.Normalize(materials, otherFaceTexture, otherBodyTexture);
        Assert(materials[0] == face && materials[1] == body, "v2 corrects recycled preview materials");

        var foreign = new Material();
        materials = new List<Material> { foreign, body };
        SkinMaterialOrder.Normalize(materials, otherFaceTexture, otherBodyTexture);
        Assert(materials[0] == foreign && materials[1] == body, "unrelated materials preserved");
        materials = new List<Material> { body, face, foreign };
        SkinMaterialOrder.Normalize(materials, otherFaceTexture, otherBodyTexture);
        Assert(materials[0] == body && materials[1] == face && materials[2] == foreign, "extra material slots preserved");
        SkinMaterialOrder.Normalize(null, otherFaceTexture, otherBodyTexture);
        SkinMaterialOrder.Normalize(new List<Material> { null, face }, otherFaceTexture, otherBodyTexture);
    }

    private static void Reset()
    {
        PlayerData.Registered.Clear();
        PlayerData.Faults.Clear();
        SkinAssets.Loaded.Clear();
        SkinFactory.Created.Clear();
    }

    private static void RegistrationAndInventory()
    {
        Reset();
        Assert(SkinCatalog.Variants[0].NumericId == 0x4D494B55, "legacy ID");
        Assert(SkinCatalog.Variants[0].DisplayName == "Hatsune Miku v1", "legacy name");
        Assert(SkinCatalog.Variants.Count == 3, "three catalog entries");
        Assert(SkinCatalog.Variants[2].NumericId == 0x4D494B57, "v3 ID");
        Assert(SkinCatalog.Variants[2].DisplayName == "Hatsune Miku v3", "v3 name");
        Assert(SkinCatalog.Variants[1].NumericId == 0x4D494B56, "v2 ID");
        Assert(SkinCatalog.Variants[1].DisplayName == "Hatsune Miku v2", "v2 name");
        var definitions = new[] { SkinCatalog.Variants[0], SkinCatalog.Variants[1] };
        var registry = new SkinRegistry(definitions, new ManualLogSource());
        var character = new Character();
        registry.RegisterAll(character);
        Assert(registry.Count == 2, "register both variants");
        RegisteredSkin first = registry.FindById(definitions[0].NumericId);
        RegisteredSkin second = registry.FindById(definitions[1].NumericId);
        Assert(registry.FindByBody(first.Body) == first, "first body lookup");
        Assert(registry.FindByBody(second.BodyWithHat) == second, "second hat lookup");
        Assert(!first.OwnsBody(second.Body), "variants do not own each other's bodies");
        Assert(first.UtilityMaterial != second.UtilityMaterial, "independent utility materials");
        Assert(second.SelectBody(true) == second.BodyWithHat, "hat selection");
        Assert(registry.FindById(999) == null, "foreign ID");
        Assert(registry.FindByBody(new Mesh()) == null, "foreign body");
        registry.RegisterAll(character);
        Assert(SkinAssets.Loaded.Count == 2, "idempotent registration");

        var profile = new PlayerData();
        PlayerData.Instance = profile;
        PlayerData.CollectInstance(character, first.Upgrade, PlayerData.UnlockFlags.Hidden);
        profile.Collections = 0;
        var inventory = new SkinInventory(registry, new ManualLogSource());
        inventory.SetCharacter(character);
        inventory.GrantForLoadedProfile();
        Assert(profile.Collections == 1 && profile.Saves == 1, "grant only missing variant");
        inventory.GrantForLoadedProfile();
        Assert(profile.Collections == 1, "no duplicate grant on update");
        var secondProfile = new PlayerData();
        PlayerData.Instance = secondProfile;
        inventory.GrantForLoadedProfile();
        Assert(secondProfile.Collections == 2 && secondProfile.Saves == 1, "new profile grants a batch and saves once");
        PlayerData.Instance = profile;
        inventory.GrantForLoadedProfile();
        Assert(profile.Collections == 1, "return to old profile without duplicates");
        Reset();
        registry = new SkinRegistry(SkinCatalog.Variants, new ManualLogSource());
        registry.RegisterAll(character);
        Assert(registry.Count == 3, "all three variants register");
        RegisteredSkin third = registry.FindById(0x4D494B57);
        Assert(third != null && registry.FindByBody(third.BodyWithHat) == third, "v3 resolves with a hat");
        profile = new PlayerData();
        PlayerData.Instance = profile;
        inventory = new SkinInventory(registry, new ManualLogSource());
        inventory.SetCharacter(character);
        inventory.GrantForLoadedProfile();
        inventory.GrantForLoadedProfile();
        Assert(profile.Collections == 3, "all skins granted without duplicates");
    }

    private static void InventoryRetries()
    {
        Reset();
        var logger = new ManualLogSource();
        var registry = new SkinRegistry(SkinCatalog.Variants, logger);
        var character = new Character();
        registry.RegisterAll(character);
        DateTime now = new DateTime(2026, 1, 1);
        var inventory = new SkinInventory(registry, logger, () => now);
        inventory.SetCharacter(character);
        var profile = new PlayerData { GrantFailures = 1, SaveFailures = 1 };
        PlayerData.Instance = profile;
        inventory.GrantForLoadedProfile();
        Assert(profile.Collections == 2 && profile.Saves == 0, "one failed grant does not block other skins; failed save retained");
        int errors = logger.Errors.Count;
        for (int i = 0; i < 100; i++) inventory.GrantForLoadedProfile();
        Assert(logger.Errors.Count == errors && profile.Collections == 2, "failures do not retry every frame");
        now = now.AddSeconds(5);
        inventory.GrantForLoadedProfile();
        Assert(profile.Collections == 3 && profile.Saves == 1, "retry grants missing skin and persists batch");
        inventory.GrantForLoadedProfile();
        Assert(profile.Collections == 3 && profile.Saves == 1, "completed profile stays idle");

        var pending = new PlayerData { SaveFailures = 1 };
        PlayerData.Instance = pending;
        inventory.GrantForLoadedProfile();
        Assert(pending.Collections == 3 && pending.Saves == 0, "all owned in memory after failed save");
        PlayerData.Instance = profile;
        inventory.GrantForLoadedProfile();
        PlayerData.Instance = pending;
        now = now.AddSeconds(5);
        inventory.GrantForLoadedProfile();
        Assert(pending.Collections == 3 && pending.Saves == 1, "pending save survives profile switch without duplicate grants");

        var silent = new PlayerData { SilentGrantFailures = 1 };
        PlayerData.Instance = silent;
        inventory.GrantForLoadedProfile();
        Assert(silent.Collections == 2, "game may decline a collection without throwing");
        now = now.AddSeconds(5);
        inventory.GrantForLoadedProfile();
        Assert(silent.Collections == 3, "unconfirmed collection is retried rather than marked complete");

        var partial = new PlayerData { GrantFailures = 1, FailAfterCollect = true };
        PlayerData.Instance = partial;
        inventory.GrantForLoadedProfile();
        now = now.AddSeconds(5);
        inventory.GrantForLoadedProfile();
        Assert(partial.Collections == 3 && partial.Saves == 1, "partially successful grant is not collected twice");
    }

    private static void RuntimeSessions()
    {
        Reset();
        var runtime = new ModRuntime(new ManualLogSource(), SkinCatalog.Variants, new BepInEx.Configuration.ConfigFile());
        Assert(!runtime.SessionEnabled && !runtime.MissionAudio.Active, "features start inactive until plugin enables session");
        runtime.StartSession();
        foreach (var variant in SkinCatalog.Variants) PlayerData.Faults[variant.NumericId] = 1;
        runtime.RegisterSkins(new Character());
        Assert(runtime.Skins.Count == 0 && runtime.SessionEnabled && runtime.MissionAudio.Active, "failed skin assets do not disable independent audio");
        runtime.StopSession();
        Assert(!runtime.SessionEnabled && !runtime.MissionAudio.Active, "session stops optional features");
        runtime.StopSession();
        runtime.StartSession();
        Assert(runtime.SessionEnabled && runtime.MissionAudio.Active, "session can resume safely");
        runtime.StopSession();
        Reset();
    }

    private static void RegistrationFailures()
    {
        Reset();
        var definitions = new[] { Variant(201, "broken"), Variant(202, "valid") };
        var logger = new ManualLogSource();
        var registry = new SkinRegistry(definitions, logger);
        PlayerData.Faults[201] = 1;
        registry.RegisterAll(new Character());
        Assert(registry.Count == 1 && registry.FindById(202) != null, "one failure does not block other variants");
        Assert(SkinAssets.Loaded[0].Disposed, "unpublished assets disposed");
        Assert(SkinFactory.Created[0].UtilityMaterial.Destroyed, "unpublished material disposed");
        Assert(!SkinAssets.Loaded[1].Disposed && logger.Errors.Count == 1, "published resources retained");

        Reset();
        registry = new SkinRegistry(definitions, new ManualLogSource());
        PlayerData.Faults[201] = 2;
        registry.RegisterAll(new Character());
        Assert(registry.Count == 2, "partially published ID stays resolvable");
        Assert(!SkinAssets.Loaded[0].Disposed, "partial registration never destroys published resources");
        Assert(PlayerData.GetUpgradeFromID(definitions[0].UpgradeId) == registry.FindById(201).Upgrade, "partial identity retained");

        Reset();
        var foreign = new Upgrade { Id = 201 };
        PlayerData.Registered.Add(201, foreign);
        registry = new SkinRegistry(new[] { definitions[0] }, new ManualLogSource());
        registry.RegisterAll(new Character());
        Assert(registry.Count == 0 && SkinAssets.Loaded.Count == 0, "foreign collision refuses loading");
        Assert(PlayerData.Registered[201] == foreign, "foreign skin not overwritten");
        Reject(() => new SkinRegistry(new[] { Variant(1, "a"), Variant(1, "b") }, new ManualLogSource()));
        Reject(() => new SkinRegistry(new[] { Variant(1, "a"), Variant(2, "A") }, new ManualLogSource()));
    }

    private static void CameraLocksAndMovement()
    {
        var player = new Player { GearLocks = 3, InteractionLocks = 4, FiringLocks = 5 };
        Player.LocalPlayer = player;
        var look = new PlayerLook();
        PlayerLook.Instance = look;
        var camera = new OrbitCameraController(new SkinAppearanceController(), new HotkeySettings(new BepInEx.Configuration.ConfigFile()));
        PlayerLook.Hook = camera;
        PressJ(camera);
        Assert(look.IsInThirdPerson, "J enters orbit");
        CheckLocks(player, 0, 3, 4, 5);
        PlayerLook.TempExitThirdPersonWithMovement = true;
        camera.BeforeCameraUpdate(look);
        Assert(!PlayerLook.TempExitThirdPersonWithMovement, "movement does not exit orbit");
        PlayerInput.Movement = new Vector2 { sqrMagnitude = 1f };
        camera.AfterCameraUpdate(look);
        Assert(player.Animator.SelfThirdPersonAnimator.CurrentStateKey == player.Character.ThirdPersonAnimationSet.run, "run animation");
        Assert(camera.ControlsAnimator(player.Animator.SelfThirdPersonAnimator), "owns self animator");
        Assert(!camera.ControlsAnimator(new ThirdPersonAnimator()), "does not control unrelated animator");
        player.Grounded = false;
        camera.AfterCameraUpdate(look);
        Assert(player.Animator.SelfThirdPersonAnimator.CurrentStateKey == player.Character.ThirdPersonAnimationSet.jumpLoop, "jump animation");
        GliderEquipment.Wings = new Wingsuit { IsFlying = true };
        camera.AfterCameraUpdate(look);
        Assert(player.Animator.SelfThirdPersonAnimator.CurrentStateKey == GliderEquipment.Wings.Flight, "flight animation");
        GliderEquipment.Wings = null;
        PressJ(camera);
        Assert(camera.Mode == ThirdPersonCameraMode.Shoulder && look.IsInThirdPerson, "second J enters shoulder");
        CheckLocks(player, 0, 3, 4, 5);
        PressJ(camera);
        Assert(!look.IsInThirdPerson, "J leaves orbit");
        CheckLocks(player, 0, 3, 4, 5);

        PressJ(camera);
        player.IsAlive = false;
        camera.UpdateInput();
        Assert(!look.IsInThirdPerson, "death releases orbit");
        CheckLocks(player, 0, 3, 4, 5);
        player.IsAlive = true;
        PressJ(camera);
        look.EnableThirdPerson(true, false, true);
        Assert(!camera.ControlsAnimator(player.Animator.SelfThirdPersonAnimator), "native inspection takes ownership");
        CheckLocks(player, 1, 4, 5, 6);
        look.DisableThirdPerson();
        CheckLocks(player, 0, 3, 4, 5);
        Cursor.visible = true;
        PressJ(camera);
        Assert(!look.IsInThirdPerson, "menu blocks orbit");
        Cursor.visible = false;
        PressJ(camera);
        camera.Stop();
        CheckLocks(player, 0, 3, 4, 5);
    }

    private static void ConfigurableHotkeys()
    {
        var config = new BepInEx.Configuration.ConfigFile();
        var hotkeys = new HotkeySettings(config);
        var perspective = config.Bind("Hotkeys", "CyclePerspective", Key.None, "");
        var weapon = config.Bind("Hotkeys", "ToggleWeapon", Key.None, "");
        Keyboard.current = new Keyboard();
        Keyboard.current.nKey.wasPressedThisFrame = true;
        Assert(!hotkeys.PerspectivePressed && !hotkeys.WeaponPressed, "N does not control the mod");
        Keyboard.current.nKey.wasPressedThisFrame = false;
        Keyboard.current.kKey.wasPressedThisFrame = false;
        Keyboard.current.jKey.wasPressedThisFrame = true;
        Assert(hotkeys.PerspectivePressed && !hotkeys.WeaponPressed, "default J changes perspective only");
        perspective.Value = Key.F5;
        Assert(!hotkeys.PerspectivePressed, "old binding is no longer active after remapping");
        Keyboard.current.f5Key.wasPressedThisFrame = true;
        Assert(hotkeys.PerspectivePressed, "configured F5 key is active");
        weapon.Value = Key.F5;
        Assert(!hotkeys.WeaponPressed, "duplicate key does not trigger both actions");
        perspective.Value = Key.None;
        Assert(!hotkeys.PerspectivePressed && hotkeys.WeaponPressed, "None disables an action without blocking the other");
        weapon.Value = (Key)9999;
        Assert(!hotkeys.WeaponPressed, "invalid numeric key cannot crash keyboard access");
        Keyboard.current = null;
        perspective.Value = Key.J;
        Assert(!hotkeys.PerspectivePressed, "missing keyboard is safe");
        Keyboard.current = new Keyboard();
    }

    private static void WeaponSlotsAndVisibility()
    {
        var first = new Renderer();
        var previouslyHidden = new Renderer { forceRenderingOff = true };
        var second = new Renderer();
        var player = new Player { SelectedGear = new Gun { GunModel = new Transform { Renderers = new[] { first, previouslyHidden } } }, HeldGear = null };
        var visibility = new WeaponVisibility();
        visibility.Toggle();
        visibility.Update(player, true);
        Assert(first.forceRenderingOff, "K hides normal slot weapon even when HeldGear is null");
        player.SelectedGear = new Gun { GunModel = new Transform { Renderers = new[] { second } } };
        visibility.Update(player, true);
        Assert(!first.forceRenderingOff && previouslyHidden.forceRenderingOff, "weapon switch restores original renderer visibility");
        Assert(second.forceRenderingOff, "newly selected weapon inherits hidden state");
        visibility.Toggle();
        Assert(!second.forceRenderingOff, "K shows selected weapon again");
        visibility.Toggle();
        visibility.Update(player, true);
        visibility.Restore();
        Assert(!second.forceRenderingOff, "shutdown restores hidden renderer");

        var magazine = new Renderer();
        var spareMagazine = new Renderer { forceRenderingOff = true };
        var attachment = new Renderer();
        var stowed = new Renderer();
        var addedDuringReload = new Renderer();
        var gun = (Gun)player.SelectedGear;
        gun.SkinRenderers = new[] { attachment, second };
        player.Animator.WeaponRoot = new Transform { Renderers = new[] { second, stowed } };
        player.Animator.MagRoot = new Transform { Renderers = new[] { magazine } };
        player.Animator.Mag1Root = new Transform { Renderers = new[] { spareMagazine } };
        visibility.Update(player, true);
        Assert(magazine.forceRenderingOff && spareMagazine.forceRenderingOff && attachment.forceRenderingOff && stowed.forceRenderingOff,
            "detached magazines, attachments and stowing weapon are hidden");
        player.Animator.MagRoot.Renderers = new[] { magazine, addedDuringReload };
        second.forceRenderingOff = false;
        visibility.Update(player, true);
        Assert(addedDuringReload.forceRenderingOff && second.forceRenderingOff,
            "reload-created parts and game visibility writes cannot reveal hidden weapon");
        player.Animator.MagRoot.Renderers = new[] { addedDuringReload };
        visibility.Update(player, true);
        Assert(!magazine.forceRenderingOff, "detached world magazine regains its own visibility");
        player.SelectedGear = null;
        visibility.Update(player, true);
        Assert(stowed.forceRenderingOff && second.forceRenderingOff && !attachment.forceRenderingOff,
            "weapon still being stowed stays hidden when selection changes");
        visibility.Toggle();
        Assert(!second.forceRenderingOff && !stowed.forceRenderingOff && !addedDuringReload.forceRenderingOff && spareMagazine.forceRenderingOff,
            "show restores all parts without revealing originally hidden magazine");
        visibility.Toggle();
        visibility.Update(player, true);
        visibility.Update(player, false);
        Assert(!second.forceRenderingOff && !addedDuringReload.forceRenderingOff && spareMagazine.forceRenderingOff,
            "first person restores all parts while preserving originally hidden renderers");
        Assert(!visibility.Visible, "first person preserves third person hide preference");
        visibility.Update(player, true);
        Assert(second.forceRenderingOff && addedDuringReload.forceRenderingOff, "returning to third person reapplies hide preference");
        visibility.Update(null, true);
        Assert(!second.forceRenderingOff && !addedDuringReload.forceRenderingOff,
            "leaving player restores every tracked weapon part");
    }

    private static void FirstPersonWeaponAlwaysVisible()
    {
        Cursor.visible = false;
        PlayerInput.Movement = default;
        var renderer = new Renderer();
        var player = new Player { SelectedGear = new Gun { GunModel = new Transform { Renderers = new[] { renderer } } } };
        Player.LocalPlayer = player;
        var look = new PlayerLook();
        PlayerLook.Instance = look;
        var camera = new OrbitCameraController(new SkinAppearanceController(), new HotkeySettings(new BepInEx.Configuration.ConfigFile()));
        PlayerLook.Hook = camera;
        PressK(camera);
        camera.AfterCameraUpdate(look);
        Assert(!renderer.forceRenderingOff, "K cannot hide first person weapon");
        PressJ(camera);
        SimulateCameraFrame(camera, look, player);
        Assert(ThirdPersonWeaponVisual.Visible, "K in first person does not change third person preference");
        PressK(camera);
        Assert(renderer.forceRenderingOff, "third person hiding takes effect");
        PressJ(camera);
        PressJ(camera);
        Assert(!renderer.forceRenderingOff, "switching back restores first person immediately");
        camera.UpdateInput();
        camera.AfterCameraUpdate(look);
        Assert(!renderer.forceRenderingOff, "later updates cannot rehide first person weapon");
        PressK(camera);
        PressJ(camera);
        SimulateCameraFrame(camera, look, player);
        Assert(!ThirdPersonWeaponVisual.Visible, "third person hide preference survives first person K");
        look.DisableThirdPerson();
        Assert(!renderer.forceRenderingOff, "native third person exit restores weapon");
    }

    private static void PressK(OrbitCameraController camera)
    {
        Keyboard.current.kKey.wasPressedThisFrame = true;
        camera.UpdateInput();
        Keyboard.current.kKey.wasPressedThisFrame = false;
    }

    private static void CameraModesAndFreeOrbit()
    {
        PlayerInput.Movement = default;
        var player = new Player();
        Player.LocalPlayer = player;
        var look = new PlayerLook();
        PlayerLook.Instance = look;
        look.transform.rotation = Quaternion.Euler(8f, 20f, 0f);
        var camera = new OrbitCameraController(new SkinAppearanceController(), new HotkeySettings(new BepInEx.Configuration.ConfigFile()));
        PlayerLook.Hook = camera;
        Keyboard.current.f5Key.wasPressedThisFrame = true;
        camera.UpdateInput();
        Keyboard.current.f5Key.wasPressedThisFrame = false;
        Assert(camera.Mode == ThirdPersonCameraMode.FirstPerson, "old F5 binding is inactive");
        PressJ(camera);
        Assert(camera.Mode == ThirdPersonCameraMode.FreeOrbit, "first J is free orbit");
        Keyboard.current.kKey.wasPressedThisFrame = true;
        camera.UpdateInput();
        Keyboard.current.kKey.wasPressedThisFrame = false;
        SimulateCameraFrame(camera, look, player);
        Assert(!ThirdPersonWeaponVisual.Visible, "K hides third person weapon");
        Cursor.visible = true;
        Keyboard.current.kKey.wasPressedThisFrame = true;
        camera.UpdateInput();
        Keyboard.current.kKey.wasPressedThisFrame = false;
        Cursor.visible = false;
        SimulateCameraFrame(camera, look, player);
        Assert(!ThirdPersonWeaponVisual.Visible, "menu blocks weapon visibility shortcut");
        Keyboard.current.kKey.wasPressedThisFrame = true;
        camera.UpdateInput();
        Keyboard.current.kKey.wasPressedThisFrame = false;
        SimulateCameraFrame(camera, look, player);
        Assert(ThirdPersonWeaponVisual.Visible, "K restores third person weapon");
        look.MouseInput = new Vector2(35f, 0f);
        PlayerInput.Movement = new Vector2(0f, 1f);
        SimulateCameraFrame(camera, look, player);
        Assert(Math.Abs(player.transform.eulerAngles.y) < 0.001f, "mouse does not turn movement in free orbit");
        Assert(Math.Abs(look.transform.eulerAngles.y - 55f) < 0.001f, "free mouse yaw retained after network callback");
        Assert(Math.Abs(player.Animator.SelfThirdPersonAnimator.BodyRotation.yaw) < 0.001f, "body heading independent of orbit");
        Assert(look.RotationLocksY == 0 && !ShoulderReticle.Visible, "free frame restores rotation locks");
        player.transform.rotation = Quaternion.Euler(0f, 12f, 0f);
        look.MouseInput = default;
        SimulateCameraFrame(camera, look, player);
        Assert(Math.Abs(look.transform.eulerAngles.y - 55f) < 0.001f, "movement rotation does not reset world orbit");
        Assert(Math.Abs(player.transform.eulerAngles.y - 12f) < 0.001f, "preserve movement heading");
        look.RotationLocksY = 2;
        look.MouseInput = new Vector2(50f, 0f);
        camera.BeforeCameraUpdate(look);
        Assert(look.RotationLocksY == 3, "one scoped yaw lock");
        camera.ReleaseFrameOverrides(look);
        camera.ReleaseFrameOverrides(look);
        Assert(look.RotationLocksY == 2, "exception cleanup leaves external locks untouched");
        look.RotationLocksY = 0;
        look.MouseInput = default;
        PressJ(camera);
        Assert(camera.Mode == ThirdPersonCameraMode.Shoulder, "second J is shoulder");
        Assert(Math.Abs(player.transform.eulerAngles.y - 55f) < 0.001f, "shoulder aligns body with view");
        Assert(look.EnableCount == 1 && look.DisableCount == 0, "mode switch does not reacquire native locks");
        Time.time += 1f;
        look.MouseInput = new Vector2(10f, 0f);
        camera.BeforeCameraUpdate(look);
        Assert(Math.Abs(look.AddedPosition.x - 0.45f) < 0.001f, "right shoulder offset");
        Assert(look.RotationLocksY == 0, "shoulder permits native yaw input");
        Assert(look.MinDistance == 1.1f && look.MaxDistance == 3.2f, "shoulder zoom bounds");
        look.AddPosition(new Vector3(0.03f, 0.02f, 0f));
        SimulateNativeCamera(look, player);
        camera.AfterCameraUpdate(look);
        Assert(Math.Abs(player.transform.eulerAngles.y - 65f) < 0.001f, "shoulder turns toward aim");
        Assert(ShoulderReticle.Visible && ShoulderCamera.Calls > 0, "shoulder reticle and collision projection active");
        Assert(Math.Abs(look.AddedPosition.x - 0.03f) < 0.001f, "scoped offset preserves native headbob");
        Assert(look.MinDistance == 1.5f && look.MaxDistance == 5f, "native zoom bounds restored");
        Assert(Math.Abs(look.transform.eulerAngles.y - 65f) < 0.001f, "network callback does not change aim camera");
        Cursor.visible = true;
        PressJ(camera);
        Assert(camera.Mode == ThirdPersonCameraMode.Shoulder, "menu blocks mode switch");
        camera.AfterCameraUpdate(look);
        Assert(!ShoulderReticle.Visible, "reticle hidden while menu captures input");
        Cursor.visible = false;
        PressJ(camera);
        Assert(camera.Mode == ThirdPersonCameraMode.FirstPerson && !look.IsInThirdPerson, "third J returns to first person");
        Assert(!ShoulderReticle.Visible, "first person releases added reticle");
        CheckLocks(player, 0, 0, 0, 0);
    }

    private static void SimulateCameraFrame(OrbitCameraController camera, PlayerLook look, Player player)
    {
        camera.BeforeCameraUpdate(look);
        SimulateNativeCamera(look, player);
        camera.AfterCameraUpdate(look);
    }

    private static void CameraRelativeMovement()
    {
        Cursor.visible = false;
        var player = new Player();
        Player.LocalPlayer = player;
        var look = new PlayerLook();
        PlayerLook.Instance = look;
        look.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        var camera = new OrbitCameraController(new SkinAppearanceController(), new HotkeySettings(new BepInEx.Configuration.ConfigFile()));
        PlayerLook.Hook = camera;
        PressJ(camera);
        Assert(player.MovementSubscribers == 1, "free orbit subscribes to native movement modifier");
        camera.UpdateInput();
        camera.UpdateInput();
        Assert(player.MovementSubscribers == 1 && player.MovementSubscriptions == 1, "ordinary frames keep the existing movement subscription");
        Vector3 movement = player.ReadMovement(new Vector2(0f, 1f));
        Assert(Math.Abs(movement.x - 1f) < .001f && Math.Abs(movement.z) < .001f, "W moves along camera yaw");
        Assert(player.transform.eulerAngles.y > 0f, "movement turns character");
        SimulateCameraFrame(camera, look, player);
        Assert(Math.Abs(look.transform.eulerAngles.y - 90f) < .001f, "character turn preserves free camera");
        movement = player.ReadMovement(new Vector2(1f, 0f));
        Assert(Math.Abs(movement.x) < .001f && Math.Abs(movement.z + 1f) < .001f, "D moves to camera right");
        movement = player.ReadMovement(new Vector2(-.6f, .8f));
        Assert(Math.Abs(movement.x - .8f) < .001f && Math.Abs(movement.z - .6f) < .001f, "diagonal movement retains direction and magnitude");
        Cursor.visible = true;
        Quaternion previous = player.transform.rotation;
        player.ReadMovement(new Vector2(0f, 1f));
        Assert(player.transform.rotation.yaw == previous.yaw, "menu suspends movement steering");
        Cursor.visible = false;
        PressJ(camera);
        movement = player.ReadMovement(new Vector2(1f, 0f));
        Vector3 expected = player.transform.rotation * new Vector3(1f, 0f, 0f);
        Assert(Math.Abs(movement.x - expected.x) < .001f && Math.Abs(movement.z - expected.z) < .001f, "shoulder mode retains native movement");
        camera.Stop();
        Assert(player.MovementSubscribers == 0, "return to first person releases movement modifier");
        PressJ(camera);
        player.Character = new Character();
        camera.UpdateInput();
        Assert(camera.Mode == ThirdPersonCameraMode.FirstPerson && player.MovementSubscribers == 0, "character switch releases movement modifier");
    }

    private static void SimulateNativeCamera(PlayerLook look, Player player)
    {
        if (look.RotationLocksY == 0)
            player.transform.localRotation *= Quaternion.Euler(0f, look.MouseInput.x, 0f);
        look.transform.rotation = player.transform.rotation * look.GlobalRotation;
        look.transform.position = player.transform.position + look.AddedPosition;
    }

    private static void PressJ(OrbitCameraController camera)
    {
        Keyboard.current.jKey.wasPressedThisFrame = true;
        camera.UpdateInput();
        Keyboard.current.jKey.wasPressedThisFrame = false;
    }

    private static void CheckLocks(Player player, int movement, int gear, int interaction, int firing)
    {
        Assert(player.MovementControlLocks == movement && player.GearLocks == gear &&
            player.InteractionLocks == interaction && player.FiringLocks == firing, "camera lock balance");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { assertions++; return; }
        throw new Exception("Duplicate definition accepted.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }
}

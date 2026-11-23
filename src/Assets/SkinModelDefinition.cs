using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace MikuGlider;

[Serializable]
internal sealed class SkinModelDefinition
{
    public int schemaVersion = 0;
    public int coatCullMode = 0;
    public bool coatEnvelope = false;
    public bool preserveSourceGarmentShape = false;
    public bool preserveSeatedHandHeading = true;
    public bool standingArmClearance = false;
    public InventoryWaistPoseDefinition inventoryWaistPose = null;
    public bool standingHipClearance = false;
    public float firstPersonWristTwistLength = 0f;
    public Vector3 torsoHoldPosition = Vector3.zero;
    public Vector3 torsoHoldRotation = Vector3.zero;
    public Vector3 legsHoldPosition = Vector3.zero;
    public Vector3 legsHoldRotation = Vector3.zero;

    internal static SkinModelDefinition Parse(string json)
    {
        // In the Mono player JsonUtility drops nested mod-defined classes from dynamically loaded assemblies.
        var definition = JsonConvert.DeserializeObject<SkinModelDefinition>(json);
        if (definition == null || definition.schemaVersion != 1)
            throw new InvalidDataException("Unsupported skin definition; expected schemaVersion 1.");
        if (definition.coatCullMode < 0 || definition.coatCullMode > 2)
            throw new InvalidDataException("Unsupported coat culling mode.");
        if (!(definition.firstPersonWristTwistLength >= 0f) ||
            float.IsInfinity(definition.firstPersonWristTwistLength))
            throw new InvalidDataException("Invalid first-person wrist twist length.");

        SkinMeshValidator.ValidateVector(definition.torsoHoldPosition);
        SkinMeshValidator.ValidateVector(definition.torsoHoldRotation);
        SkinMeshValidator.ValidateVector(definition.legsHoldPosition);
        SkinMeshValidator.ValidateVector(definition.legsHoldRotation);
        definition.inventoryWaistPose?.Validate();
        return definition;
    }
}

[Serializable]
internal sealed class InventoryWaistPoseDefinition
{
    public Vector3 wristPosition = Vector3.zero;
    public Vector3 middleTipPosition = Vector3.zero;
    // Local rotation vectors. Direction is the axis and magnitude is degrees.
    public Vector3 shoulderDegrees = Vector3.zero;
    public Vector3 elbowDegrees = Vector3.zero;
    public Vector3 wristDegrees = Vector3.zero;

    internal void Validate()
    {
        SkinMeshValidator.ValidateVector(wristPosition);
        SkinMeshValidator.ValidateVector(middleTipPosition);
        SkinMeshValidator.ValidateVector(shoulderDegrees);
        SkinMeshValidator.ValidateVector(elbowDegrees);
        SkinMeshValidator.ValidateVector(wristDegrees);
    }
}

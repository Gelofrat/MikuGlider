using System;
using System.IO;
using UnityEngine;

namespace MikuGlider;

[Serializable]
internal sealed class SkinModelDefinition
{
    public int schemaVersion = 0;
    public int coatCullMode = 0;
    public bool coatEnvelope = false;
    public Vector3 torsoHoldPosition = Vector3.zero;
    public Vector3 torsoHoldRotation = Vector3.zero;
    public Vector3 legsHoldPosition = Vector3.zero;
    public Vector3 legsHoldRotation = Vector3.zero;

    internal static SkinModelDefinition Parse(string json)
    {
        var definition = JsonUtility.FromJson<SkinModelDefinition>(json);
        if (definition == null || definition.schemaVersion != 1)
            throw new InvalidDataException("Unsupported skin definition; expected schemaVersion 1.");
        if (definition.coatCullMode < 0 || definition.coatCullMode > 2)
            throw new InvalidDataException("Unsupported coat culling mode.");

        SkinMeshValidator.ValidateVector(definition.torsoHoldPosition);
        SkinMeshValidator.ValidateVector(definition.torsoHoldRotation);
        SkinMeshValidator.ValidateVector(definition.legsHoldPosition);
        SkinMeshValidator.ValidateVector(definition.legsHoldRotation);
        return definition;
    }
}

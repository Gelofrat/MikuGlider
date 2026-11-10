using Pigeon.Movement;

namespace MikuGlider;

internal static class GliderEquipment
{
    internal static Wingsuit FindWingsuit(Player player)
    {
        if (player?.Gear == null)
            return null;

        foreach (IGear gear in player.Gear)
        {
            if (gear is Wingsuit wingsuit)
                return wingsuit;
        }

        return null;
    }
}

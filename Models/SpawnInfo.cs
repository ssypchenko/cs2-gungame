using CounterStrikeSharp.API.Modules.Utils;

namespace GunGame.Models
{
    public class SpawnInfo
    {
        public Vector Position { get; set; }
        public QAngle Rotation { get; set; }

        public SpawnInfo(Vector position, QAngle rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }
}

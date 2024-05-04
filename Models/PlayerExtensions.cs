using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

namespace GunGame.Models
{
    public static class PlayerExtensions
    {
        public static void ToggleNoclip(this CBasePlayerPawn pawn)
        {
            if (pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP)
            {
                pawn.MoveType = MoveType_t.MOVETYPE_WALK;
                Schema.SetSchemaValue(pawn.Handle, "CBaseEntity", "m_nActualMoveType", 2); // walk
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
            }
            else
            {
                pawn.MoveType = MoveType_t.MOVETYPE_NOCLIP;
                Schema.SetSchemaValue(pawn.Handle, "CBaseEntity", "m_nActualMoveType", 8); // noclip
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
            }
        }
    }
}

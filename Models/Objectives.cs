namespace GunGame.Models
{
    [Flags]
    public enum Objectives
    {
        RemoveBomb = 1 << 0,
        RemoveHostage = 1 << 1,
        Bomb = 1 << 2,
        Hostage = 1 << 3
    }
}

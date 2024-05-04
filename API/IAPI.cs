
namespace GunGame.API
{
    public interface IAPI
    {
        event Action<KillEventArgs>? KillEvent;
        event Action<WinnerEventArgs>? KnifeStealEvent;
        event Action<LevelChangeEventArgs>? LevelChangeEvent;
        event Action<PointChangeEventArgs>? PointChangeEvent;
        event Action? RestartEvent;
        event Action<WeaponFragEventArgs>? WeaponFragEvent;
        event Action<WinnerEventArgs>? WinnerEvent;

        int GetMaxLevel();
        int GetPlayerLevel(int slot);
    }
}
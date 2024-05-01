using GunGame;
using GunGame.Variables;
namespace GunGame.API
{
    public class CoreAPI : IAPI
    {
        private GunGame _gunGame;
        public CoreAPI(GunGame gunGame)
        {
            _gunGame = gunGame;  // Initialize it through the constructor
        }
        public event Action<WinnerEventArgs>? WinnerEvent;
        public void RaiseWinnerEvent(int winner, int looser)
        {
            var args = new WinnerEventArgs(winner, looser);
            WinnerEvent?.Invoke(args);
        }
        public event Action<WinnerEventArgs>? KnifeStealEvent;
        public void RaiseKnifeStealEvent(int killer, int victim)
        {
            var args = new WinnerEventArgs(killer, victim);
            KnifeStealEvent?.Invoke(args);
        }
        public event Action<KillEventArgs>? KillEvent;
        public bool RaiseKillEvent(int killer, int victim, string weapon, bool teamkill)
        {
            var args = new KillEventArgs(killer, victim, weapon, teamkill);
            KillEvent?.Invoke(args);
            return args.Result;
        }
        public event Action<LevelChangeEventArgs>? LevelChangeEvent;
        public bool RaiseLevelChangeEvent(int killer, int level, int difference, bool knifesteal, bool lastlevel, bool knife, int victim)
        {
            var args = new LevelChangeEventArgs(killer, level, difference, knifesteal, lastlevel, knife, victim);
            LevelChangeEvent?.Invoke(args);
            return args.Result;
        }
        public event Action<PointChangeEventArgs>? PointChangeEvent;
        public bool RaisePointChangeEvent(int killer, int kills)
        {
            var args = new PointChangeEventArgs(killer, kills);
            PointChangeEvent?.Invoke(args);
            return args.Result;
        }
        public event Action<WeaponFragEventArgs>? WeaponFragEvent;
        public bool RaiseWeaponFragEvent(int killer, string weapon)
        {
            var args = new WeaponFragEventArgs(killer, weapon);
            WeaponFragEvent?.Invoke(args);
            return args.Result;
        }
        public event Action? RestartEvent;
        public void RaiseRestartEvent()
        {
            RestartEvent?.Invoke();
        }
        public int GetMaxLevel()
        {
            return GGVariables.Instance.WeaponOrderCount;
        }
        public int GetPlayerLevel(int slot)
        {
            var player = _gunGame.playerManager.FindBySlot(slot, "GetPlayerLevel");
            if (player != null)
                return (int)player.Level;
            return 0;
        }
    }
}

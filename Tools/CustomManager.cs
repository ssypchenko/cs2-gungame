using GunGame.Models;
using Microsoft.Extensions.Logging;

namespace GunGame.Tools
{
    public abstract class CustomManager
    {
        public CustomManager(GunGame plugin)
        {
            Plugin = plugin;
        }

        public GunGame Plugin { get; }

        public GGConfig Config => Plugin.Config;
        public ILogger Logger => Plugin.Logger;
        public PlayerManager PlayerManager => Plugin.playerManager;
    }
}

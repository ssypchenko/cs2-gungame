namespace GunGame.Models
{
    public class Leader
    {
        private readonly object lockObject = new();
        public uint Level { get; private set; } = 0;
        public int Slot { get; private set; } = -1;
        public void SetLeader (int slot, int level)
        {
            lock (lockObject)
            {
                Level = (uint)level;
                Slot = slot;
            }
        }
    }
}

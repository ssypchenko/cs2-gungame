namespace GunGame.Models
{
    public class Winner
    {
        public int Slot { get; set; } = 0;
        public int TeamNum { get; set; } = 0;
        public string Name { get; set; }
        public Winner (GGPlayer winner)
        {
            Slot = winner.Slot;
            TeamNum = winner.GetTeam();
            Name = winner.PlayerName;
        }
    }
}

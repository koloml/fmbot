namespace FMBot.Persistence.Domain.Models
{
    public class Channel
    {
        public int ChannelId { get; set; }

        public ulong DiscordChannelId { get; set; }

        public string Name { get; set; }

        public int GuildId { get; set; }

        public Guild Guild { get; set; }

        public string[] DisabledCommands { get; set; }
    }
}

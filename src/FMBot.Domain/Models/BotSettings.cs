namespace FMBot.Domain.Models
{
    public class BotSettings
    {
        public string Environment { get; set; }

        public DiscordConfig Discord { get; set; }

        public DatabaseConfig Database { get; set; }

        public LoggingConfig Logging { get; set; }

        public BotConfig Bot { get; set; }

        public LastFmConfig LastFm { get; set; }

        public SpotifyConfig Spotify { get; set; }

        public GeniusConfig Genius { get; set; }

        public GoogleConfig Google { get; set; }

        public BotListConfig BotLists { get; set; }

        public DiscogsConfig Discogs { get; set; }
    }

    public class DiscordConfig
    {
        public string Token { get; set; }

        public ulong? BotUserId { get; set; }
    }

    public class DatabaseConfig
    {
        public string ConnectionString { get; set; }
    }

    public class LoggingConfig
    {
        public string SeqServerUrl { get; set; }

        public string SeqApiKey { get; set; }
    }

    public class BotConfig
    {
        public string Prefix { get; set; }

        public ulong BaseServerId { get; set; }

        public ulong AnnouncementChannelId { get; set; }

        public ulong FeaturedChannelId { get; set; }
        public bool? FeaturedMaster { get; set; }
        public bool? MainInstance { get; set; }
        public string FeaturedPreviewWebhookUrl { get; set; }

        public ulong ExceptionChannelWebhookId { get; set; }
        public string ExceptionChannelWebhookToken { get; set; }

        public string Status { get; set; }

        public int BotWarmupTimeInSeconds { get; set; }
        public int TotalShards { get; set; }

        public string MetricsPusherEndpoint { get; set; }
        public string MetricsPusherName { get; set; }

        public int FeaturedTimerStartupDelayInSeconds { get; set; }
    }

    public class LastFmConfig
    {
        public string PrivateKey { get; set; }

        public string PublicKey { get; set; }

        public string PublicKeySecret { get; set; }

        public int? UserUpdateFrequencyInHours { get; set; }

        public int? UserIndexFrequencyInDays { get; set; }
    }

    public class SpotifyConfig
    {
        public string Key { get; set; }

        public string Secret { get; set; }
    }

    public class DiscogsConfig
    {
        public string Key { get; set; }

        public string Secret { get; set; }
    }

    public class GeniusConfig
    {
        public string AccessToken { get; set; }
    }

    public class GoogleConfig
    {
        public string ApiKey { get; set; }

        public string InvidiousUrl { get; set; }
    }

    public class BotListConfig
    {
        public string TopGgApiToken { get; set; }

        public string BotsForDiscordToken { get; set; }

        public string DiscordBoatsToken { get; set; }

        public string BotsOnDiscordToken { get; set; }
    }
}

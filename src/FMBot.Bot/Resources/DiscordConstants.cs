using System.Collections.Generic;
using Discord;
using Fergun.Interactive.Pagination;

namespace FMBot.Bot.Resources
{
    public static class DiscordConstants
    {
        public static Color LastFmColorRed = new(186, 0, 0);

        /// <summary>The Discord color for a warning embed.</summary>
        public static Color WarningColorOrange = new(255, 174, 66);

        public static Color SuccessColorGreen = new(50, 205, 50);

        public static Color InformationColorBlue = new(68, 138, 255);

        public static Color SpotifyColorGreen = new(30, 215, 97);


        public static readonly IDictionary<IEmote, PaginatorAction> PaginationEmotes = new Dictionary<IEmote, PaginatorAction>
        {
            { Emote.Parse("<:pages_first:883825508633182208>"), PaginatorAction.SkipToStart},
            { Emote.Parse("<:pages_previous:883825508507336704>"), PaginatorAction.Backward},
            { Emote.Parse("<:pages_next:883825508087922739>"), PaginatorAction.Forward},
            { Emote.Parse("<:pages_last:883825508482183258>"), PaginatorAction.SkipToEnd}
        };

        public const string JumpToGuildEmote = "<:server:961685224041902140>";

        public static readonly IDictionary<IEmote, PaginatorAction> PaginationUserEmotes = new Dictionary<IEmote, PaginatorAction>
        {
            { Emote.Parse("<:pages_first:883825508633182208>"), PaginatorAction.SkipToStart},
            { Emote.Parse("<:pages_previous:883825508507336704>"), PaginatorAction.Backward},
            { Emote.Parse("<:pages_next:883825508087922739>"), PaginatorAction.Forward},
            { Emote.Parse("<:pages_last:883825508482183258>"), PaginatorAction.SkipToEnd},
            { Emote.Parse(JumpToGuildEmote), PaginatorAction.Jump}
        };

        public const string JumpToUserEmote = "<:user:961687127249260634>";

        public static readonly IDictionary<IEmote, PaginatorAction> PaginationGuildEmotes = new Dictionary<IEmote, PaginatorAction>
        {
            { Emote.Parse("<:pages_first:883825508633182208>"), PaginatorAction.SkipToStart},
            { Emote.Parse("<:pages_previous:883825508507336704>"), PaginatorAction.Backward},
            { Emote.Parse("<:pages_next:883825508087922739>"), PaginatorAction.Forward},
            { Emote.Parse("<:pages_last:883825508482183258>"), PaginatorAction.SkipToEnd},
            { Emote.Parse(JumpToUserEmote), PaginatorAction.Jump}
        };

        public const int PaginationTimeoutInSeconds = 120;
    }
}

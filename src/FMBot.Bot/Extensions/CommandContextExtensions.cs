using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using Serilog;

namespace FMBot.Bot.Extensions
{
    public static class CommandContextExtensions
    {
        public static void LogCommandUsed(this ICommandContext context, CommandResponse commandResponse = CommandResponse.Ok)
        {
            Log.Information("CommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, commandResponse, context.Message.Content);
        }

        public static async Task HandleCommandException(this ICommandContext context, Exception exception, string message = null, bool sendReply = true)
        {
            var referenceId = GenerateRandomCode();

            Log.Error(exception, "CommandUsed: Error {referenceId} | {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} ({message}) | {messageContent}",
                referenceId, context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, CommandResponse.Error, message, context.Message.Content);

            if (sendReply)
            {
                await context.Channel.SendMessageAsync("Sorry, something went wrong. Please try again later.\n" +
                                                       $"*Reference id: `{referenceId}`*", allowedMentions: AllowedMentions.None);
            }
        }

        public static void LogCommandWithLastFmError(this ICommandContext context, ResponseStatus? responseStatus)
        {
            Log.Error("CommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent} | Last.fm error: {responseStatus}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, CommandResponse.LastFmError, context.Message.Content, responseStatus);
        }

        public static async Task SendResponse(this ICommandContext context, InteractiveService interactiveService, ResponseModel response)
        {
            switch (response.ResponseType)
            {
                case ResponseType.Text:
                    await context.Channel.SendMessageAsync(response.Text, allowedMentions: AllowedMentions.None);
                    break;
                case ResponseType.Embed:
                    await context.Channel.SendMessageAsync("", false, response.Embed.Build());
                    break;
                case ResponseType.Paginator:
                    _ = interactiveService.SendPaginatorAsync(
                        response.StaticPaginator,
                        context.Channel,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
                    break;
                case ResponseType.PagedSelection:
                    _ = interactiveService.SendSelectionAsync(
                        response.PagedSelection,
                        context.Channel,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
                    break;
                case ResponseType.ImageWithEmbed:
                    await context.Channel.SendFileAsync(
                        response.Stream,
                        response.FileName + ".png",
                        null,
                        false,
                        response.Embed.Build(),
                        isSpoiler: response.Spoiler);
                    await response.Stream.DisposeAsync();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string GenerateRandomCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ123456789";
            const int size = 8;

            var data = new byte[4 * size];
            using (var crypto = RandomNumberGenerator.Create())
            {
                crypto.GetBytes(data);
            }
            var result = new StringBuilder(size);
            for (var i = 0; i < size; i++)
            {
                var rnd = Math.Abs(BitConverter.ToInt32(data, i * 4));
                var idx = rnd % chars.Length;

                result.Append(chars[idx]);
            }

            return result.ToString();
        }
    }
}

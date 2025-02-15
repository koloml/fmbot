using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Npgsql;
using PostgreSQLCopyHelper;
using Serilog;

namespace FMBot.LastFM.Repositories;

public static class PlayRepository
{
    public record PlayUpdate(List<UserPlay> NewPlays, List<UserPlayTs> RemovedPlays);

    public static async Task<PlayUpdate> InsertLatestPlays(List<RecentTrack> recentTracks, int userId, NpgsqlConnection connection)
    {
        var plays = recentTracks
            .Where(w => !w.NowPlaying &&
                        w.TimePlayed.HasValue)
            .Select(s => new UserPlay
            {
                ArtistName = s.ArtistName,
                AlbumName = s.AlbumName,
                TrackName = s.TrackName,
                TimePlayed = DateTime.SpecifyKind(s.TimePlayed.Value, DateTimeKind.Utc),
                UserId = userId
            }).ToList();

        var existingPlays = await GetUserPlays(userId, connection, plays.Count + 250);

        var firstExistingPlay = existingPlays
            .OrderBy(o => o.TimePlayed)
            .FirstOrDefault();

        if (firstExistingPlay != null)
        {
            plays = plays
                .Where(w => w.TimePlayed >= firstExistingPlay.TimePlayed)
                .ToList();
        }

        var addedPlays = new List<UserPlay>();
        foreach (var newPlay in plays)
        {
            if (existingPlays.All(a => a.TimePlayed != newPlay.TimePlayed))
            {
                addedPlays.Add(newPlay);
            }
        }

        var firstNewPlay = plays
            .OrderBy(o => o.TimePlayed)
            .FirstOrDefault();

        var removedPlays = new List<UserPlayTs>();
        if (firstNewPlay != null)
        {
            foreach (var existingPlay in existingPlays.Where(w => w.TimePlayed >= firstNewPlay.TimePlayed))
            {
                if (plays.All(a => a.TimePlayed != existingPlay.TimePlayed))
                {
                    removedPlays.Add(existingPlay);
                }
            }

            if (removedPlays.Any())
            {
                Log.Information($"Found {removedPlays.Count} time series plays to remove for {userId}");
                await RemoveSpecificPlays(removedPlays, connection);
            }
        }

        Log.Information($"Inserting {addedPlays.Count} new time series plays for user {userId}");
        await InsertTimeSeriesPlays(addedPlays, connection);

        return new PlayUpdate(addedPlays, removedPlays);
    }

    public static async Task InsertAllPlays(IReadOnlyList<UserPlay> playsToInsert, int userId, NpgsqlConnection connection)
    {
        await RemoveAllCurrentPlays(userId, connection);

        Log.Information($"Inserting {playsToInsert.Count} time series plays for user {userId}");
        await InsertTimeSeriesPlays(playsToInsert, connection);
    }

    private static async Task RemoveAllCurrentPlays(int userId, NpgsqlConnection connection)
    {
        await using var deletePlays = new NpgsqlCommand("DELETE FROM public.user_play_ts " +
                                                        "WHERE user_id = @userId", connection);

        deletePlays.Parameters.AddWithValue("userId", userId);

        await deletePlays.ExecuteNonQueryAsync();
    }

    private static async Task RemoveSpecificPlays(IEnumerable<UserPlayTs> playsToRemove, NpgsqlConnection connection)
    {
        foreach (var playToRemove in playsToRemove)
        {
            await using var deletePlays = new NpgsqlCommand("DELETE FROM public.user_play_ts " +
                                                            "WHERE user_id = @userId AND time_played = @timePlayed", connection);

            deletePlays.Parameters.AddWithValue("userId", playToRemove.UserId);
            deletePlays.Parameters.AddWithValue("timePlayed", playToRemove.TimePlayed);

            await deletePlays.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertTimeSeriesPlays(IEnumerable<UserPlay> plays, NpgsqlConnection connection)
    {
        var copyHelper = new PostgreSQLCopyHelper<UserPlay>("public", "user_play_ts")
            .MapText("track_name", x => x.TrackName)
            .MapText("album_name", x => x.AlbumName)
            .MapText("artist_name", x => x.ArtistName)
            .MapTimeStampTz("time_played", x => DateTime.SpecifyKind(x.TimePlayed, DateTimeKind.Utc))
            .MapInteger("user_id", x => x.UserId);

        await copyHelper.SaveAllAsync(connection, plays);
    }

    private static async Task<IReadOnlyCollection<UserPlayTs>> GetUserPlays(int userId, NpgsqlConnection connection, int limit)
    {
        const string sql = "SELECT * FROM public.user_play_ts where user_id = @userId " +
                           "ORDER BY time_played DESC LIMIT @limit";
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        return (await connection.QueryAsync<UserPlayTs>(sql, new
        {
            userId,
            limit
        })).ToList();
    }
}

using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Dapper;
using Discord.Commands;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Artist = FMBot.LastFM.Domain.Models.Artist;

namespace FMBot.Bot.Services
{
    public class PlayService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly GenreService _genreService;
        private readonly TimeService _timeService;
        private readonly BotSettings _botSettings;
        private readonly LastFmRepository _lastFmRepository;
        private readonly IMemoryCache _cache;

        public PlayService(IDbContextFactory<FMBotDbContext> contextFactory, GenreService genreService, TimeService timeService, IOptions<BotSettings> botSettings, LastFmRepository lastFmRepository, IMemoryCache cache)
        {
            this._contextFactory = contextFactory;
            this._genreService = genreService;
            this._timeService = timeService;
            this._lastFmRepository = lastFmRepository;
            this._cache = cache;
            this._botSettings = botSettings.Value;
        }

        public async Task<DailyOverview> GetDailyOverview(int userId, int amountOfDays)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();

            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-amountOfDays);

            var plays = await db.UserPlays
                .AsQueryable()
                .Where(w => w.TimePlayed.Date <= now.Date &&
                            w.TimePlayed.Date > minDate.Date &&
                            w.UserId == userId)
                .ToListAsync();

            if (!plays.Any())
            {
                return null;
            }

            var overview = new DailyOverview
            {
                Days = plays
                    .OrderByDescending(o => o.TimePlayed)
                    .GroupBy(g => g.TimePlayed.Date.AddHours(5))
                    .Select(s => new DayOverview
                    {
                        Date = s.Key,
                        Playcount = s.Count(),
                        Plays = s.ToList(),
                        TopTrack = GetTopTrackForPlays(s.ToList()),
                        TopAlbum = GetTopAlbumForPlays(s.ToList()),
                        TopArtist = GetTopArtistForPlays(s.ToList()),
                    }).Take(amountOfDays).ToList(),
                Playcount = plays.Count,
                Uniques = GetUniqueCount(plays.ToList()),
                AvgPerDay = GetAvgPerDayCount(plays.ToList()),
            };

            foreach (var day in overview.Days.Where(w => w.Plays.Any()))
            {
                day.TopGenres = await this._genreService.GetTopGenresForPlays(day.Plays);
            }
            foreach (var day in overview.Days.Where(w => w.Plays.Any()))
            {
                day.ListeningTime = await this._timeService.GetPlayTimeForPlays(day.Plays);
            }

            return overview;
        }

        public async Task<YearOverview> GetYear(int userId, int year)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var user = await db.Users
                 .AsNoTracking()
                 .FirstAsync(f => f.UserId == userId);

            var cacheKey = $"year-ov-{user.UserId}-{year}";

            var cachedYearAvailable = this._cache.TryGetValue(cacheKey, out YearOverview cachedYearOverview);
            if (cachedYearAvailable)
            {
                return cachedYearOverview;
            }

            var startDateTime = new DateTime(year, 01, 01);
            var endDateTime = startDateTime.AddYears(1).AddSeconds(-1);

            var yearOverview = new YearOverview
            {
                Year = year,
                LastfmErrors = false
            };

            var currentTopTracks =
                await this._lastFmRepository.GetTopTracksForCustomTimePeriodAsyncAsync(user.UserNameLastFM, startDateTime, endDateTime, 500);

            if (!currentTopTracks.Success)
            {
                yearOverview.LastfmErrors = true;
                return yearOverview;
            }

            yearOverview.TopTracks = currentTopTracks.Content;

            var currentTopAlbums =
                await this._lastFmRepository.GetTopAlbumsForCustomTimePeriodAsyncAsync(user.UserNameLastFM, startDateTime, endDateTime, 500);

            if (!currentTopAlbums.Success)
            {
                yearOverview.LastfmErrors = true;
                return yearOverview;
            }

            yearOverview.TopAlbums = currentTopAlbums.Content;

            var currentTopArtists =
                await this._lastFmRepository.GetTopArtistsForCustomTimePeriodAsync(user.UserNameLastFM, startDateTime, endDateTime, 500);

            if (!currentTopArtists.Success)
            {
                yearOverview.LastfmErrors = true;
                return yearOverview;
            }

            yearOverview.TopArtists = currentTopArtists.Content;

            if (user.RegisteredLastFm < endDateTime.AddYears(-1))
            {
                var previousTopTracks =
                    await this._lastFmRepository.GetTopTracksForCustomTimePeriodAsyncAsync(user.UserNameLastFM, startDateTime.AddYears(-1), endDateTime.AddYears(-1), 800);

                if (previousTopTracks.Success)
                {
                    yearOverview.PreviousTopTracks = previousTopTracks.Content;
                }
                else
                {
                    yearOverview.LastfmErrors = true;
                }

                var previousTopAlbums =
                    await this._lastFmRepository.GetTopAlbumsForCustomTimePeriodAsyncAsync(user.UserNameLastFM, startDateTime.AddYears(-1), endDateTime.AddYears(-1), 800);

                if (previousTopAlbums.Success)
                {
                    yearOverview.PreviousTopAlbums = previousTopAlbums.Content;
                }
                else
                {
                    yearOverview.LastfmErrors = true;
                }

                var previousTopArtists =
                        await this._lastFmRepository.GetTopArtistsForCustomTimePeriodAsync(user.UserNameLastFM, startDateTime.AddYears(-1), endDateTime.AddYears(-1), 800);

                if (previousTopArtists.Success)
                {
                    yearOverview.PreviousTopArtists = previousTopArtists.Content;
                }
                else
                {
                    yearOverview.LastfmErrors = true;
                }
            }

            if (!yearOverview.LastfmErrors)
            {
                this._cache.Set(cacheKey, yearOverview, TimeSpan.FromHours(3));
            }

            return yearOverview;
        }

        private static int GetUniqueCount(IEnumerable<UserPlay> plays)
        {
            return plays
                .GroupBy(x => new { x.ArtistName, x.TrackName })
                .Count();
        }

        private static double GetAvgPerDayCount(IEnumerable<UserPlay> plays)
        {
            return plays
                .GroupBy(g => g.TimePlayed.Date)
                .Average(a => a.Count());
        }

        private static string GetTopTrackForPlays(IEnumerable<UserPlay> plays)
        {
            var topTrack = plays
                .GroupBy(x => new { x.ArtistName, x.TrackName })
                .OrderByDescending(o => o.Count())
                .FirstOrDefault();

            if (topTrack == null)
            {
                return "No top track for this day";
            }

            return $"`{topTrack.Count()}` {StringExtensions.GetPlaysString(topTrack.Count())} - {topTrack.Key.ArtistName} | {topTrack.Key.TrackName}";
        }

        private static string GetTopAlbumForPlays(IEnumerable<UserPlay> plays)
        {
            var topAlbum = plays
                .GroupBy(x => new { x.ArtistName, x.AlbumName })
                .OrderByDescending(o => o.Count())
                .FirstOrDefault();

            if (topAlbum == null)
            {
                return "No top album for this day";
            }

            return $"`{topAlbum.Count()}` {StringExtensions.GetPlaysString(topAlbum.Count())} - {topAlbum.Key.ArtistName} | {topAlbum.Key.AlbumName}";
        }

        private static string GetTopArtistForPlays(IEnumerable<UserPlay> plays)
        {
            var topArtist = plays
                .GroupBy(x => x.ArtistName)
                .OrderByDescending(o => o.Count())
                .FirstOrDefault();

            if (topArtist == null)
            {
                return "No top artist for this day";
            }

            return $"`{topArtist.Count()}` {StringExtensions.GetPlaysString(topArtist.Count())} - {topArtist.Key}";
        }

        public async Task<int> GetWeekTrackPlaycountAsync(int userId, string trackName, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            await using var db = await this._contextFactory.CreateDbContextAsync();
            return await db.UserPlays
                .AsQueryable()
                .CountAsync(t => t.TimePlayed.Date <= now.Date &&
                                 t.TimePlayed.Date > minDate.Date &&
                                 t.TrackName.ToLower() == trackName.ToLower() &&
                                 t.ArtistName.ToLower() == artistName.ToLower() &&
                                 t.UserId == userId);
        }

        public async Task<int> GetWeekAlbumPlaycountAsync(int userId, string albumName, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            await using var db = await this._contextFactory.CreateDbContextAsync();
            return await db.UserPlays
                .AsQueryable()
                .CountAsync(ab => ab.TimePlayed.Date <= now.Date &&
                                 ab.TimePlayed.Date > minDate.Date &&
                                 ab.AlbumName.ToLower() == albumName.ToLower() &&
                                 ab.ArtistName.ToLower() == artistName.ToLower() &&
                                 ab.UserId == userId);
        }

        public async Task<int> GetArtistPlaycountForTimePeriodAsync(int userId, string artistName, int daysToGoBack = 7)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-daysToGoBack);

            await using var db = await this._contextFactory.CreateDbContextAsync();
            return await db.UserPlays
                .AsQueryable()
                .CountAsync(a => a.TimePlayed.Date <= now.Date &&
                                 a.TimePlayed.Date > minDate.Date &&
                                 a.ArtistName.ToLower() == artistName.ToLower() &&
                                 a.UserId == userId);
        }

        public async Task<List<UserStreak>> GetStreaks(int userId)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            return await db.UserStreaks
                .Where(w => w.UserId == userId)
                .OrderByDescending(o => o.ArtistPlaycount)
                .ToListAsync();
        }

        public async Task<UserStreak> GetStreak(int userId, Response<RecentTrackList> recentTracks)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var lastPlays = await db.UserPlays
                .AsQueryable()
                .Where(w => w.UserId == userId)
                .OrderByDescending(o => o.TimePlayed)
                .ToListAsync();

            if (!lastPlays.Any())
            {
                return null;
            }

            var firstPlay = recentTracks.Content.RecentTracks.First();

            var streak = new UserStreak
            {
                ArtistPlaycount = 1,
                AlbumPlaycount = 1,
                TrackPlaycount = 1,
                StreakEnded = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                UserId = userId
            };

            var artistContinue = true;
            var albumContinue = true;
            var trackContinue = true;
            for (var i = 1; i < lastPlays.Count; i++)
            {
                var play = lastPlays[i];

                if (firstPlay.ArtistName.ToLower() == play.ArtistName.ToLower() && artistContinue)
                {
                    streak.ArtistPlaycount++;
                    streak.StreakStarted = play.TimePlayed;
                    streak.ArtistName = play.ArtistName;
                }
                else
                {
                    artistContinue = false;
                }

                if (firstPlay.AlbumName != null && play.AlbumName != null && firstPlay.AlbumName.ToLower() == play.AlbumName.ToLower() && albumContinue)
                {
                    streak.AlbumPlaycount++;
                    streak.StreakStarted = play.TimePlayed;
                    streak.AlbumName = play.AlbumName;
                }
                else
                {
                    albumContinue = false;
                }

                if (firstPlay.TrackName.ToLower() == play.TrackName.ToLower() && trackContinue)
                {
                    streak.TrackPlaycount++;
                    streak.StreakStarted = play.TimePlayed;
                    streak.TrackName = play.TrackName;
                }
                else
                {
                    trackContinue = false;
                }

                if (!artistContinue && !albumContinue && !trackContinue)
                {
                    break;
                }
            }

            streak.StreakStarted = DateTime.SpecifyKind(streak.StreakStarted, DateTimeKind.Utc);

            return streak;
        }

        public static string StreakToText(UserStreak streak, bool includeStart = true)
        {
            var description = new StringBuilder();
            if (streak.ArtistPlaycount > 1)
            {
                description.AppendLine($"Artist: **[{streak.ArtistName}](https://www.last.fm/music/{HttpUtility.UrlEncode(streak.ArtistName)})** - " +
                                       $"{GetEmojiForStreakCount(streak.ArtistPlaycount.Value)}*{streak.ArtistPlaycount} plays in a row*");
            }
            if (streak.AlbumPlaycount > 1)
            {
                description.AppendLine($"Album: **[{streak.AlbumName}](https://www.last.fm/music/{HttpUtility.UrlEncode(streak.ArtistName)}/{HttpUtility.UrlEncode(streak.AlbumName)})** - " +
                                       $"{GetEmojiForStreakCount(streak.AlbumPlaycount.Value)}*{streak.AlbumPlaycount} plays in a row*");
            }
            if (streak.TrackPlaycount > 1)
            {
                description.AppendLine($"Track: **[{streak.TrackName}](https://www.last.fm/music/{HttpUtility.UrlEncode(streak.ArtistName)}/_/{HttpUtility.UrlEncode(streak.TrackName)})** - " +
                                       $"{GetEmojiForStreakCount(streak.TrackPlaycount.Value)}*{streak.TrackPlaycount} plays in a row*");
            }

            if (description.Length == 0)
            {
                return "No active streak found.";
            }

            if (includeStart)
            {
                var specifiedDateTime = DateTime.SpecifyKind(streak.StreakStarted, DateTimeKind.Utc);
                var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                description.AppendLine();
                description.AppendLine($"Streak started <t:{dateValue}:R>.");
            }

            return description.ToString();
        }

        public async Task<string> UpdateOrInsertStreak(UserStreak streak)
        {
            const int saveThreshold = 25;
            if (streak.TrackPlaycount < saveThreshold && streak.AlbumPlaycount < saveThreshold && streak.ArtistPlaycount < saveThreshold)
            {
                return $"Only streaks with {saveThreshold} plays or higher are saved.";
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();
            var existingStreak = await db.UserStreaks.FirstOrDefaultAsync(f =>
                f.UserId == streak.UserId && f.StreakStarted == streak.StreakStarted);

            if (existingStreak == null)
            {
                await db.UserStreaks.AddAsync(streak);
                await db.SaveChangesAsync();
                return "Streak has been saved!";
            }

            existingStreak.StreakEnded = streak.StreakEnded;
            existingStreak.TrackPlaycount = streak.TrackPlaycount;
            existingStreak.AlbumPlaycount = streak.AlbumPlaycount;
            existingStreak.ArtistPlaycount = streak.ArtistPlaycount;

            db.Entry(existingStreak).State = EntityState.Modified;
            await db.SaveChangesAsync();

            return "Saved streak has been updated!";
        }

        private static string GetEmojiForStreakCount(int count)
        {
            if (count > 50 && count < 100 || count > 100)
            {
                return "🔥 ";
            }

            if (count == 100)
            {
                return "💯 ";
            }
            return null;
        }

        public async Task<Response<TopTracksLfmResponse>> GetTopTracks(int userId, int days)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-days);

            await using var db = this._contextFactory.CreateDbContext();
            var tracks = await db.UserPlays
                .AsQueryable()
                .Where(t => t.TimePlayed.Date <= now.Date &&
                                 t.TimePlayed.Date > minDate.Date &&
                                 t.UserId == userId)
                .GroupBy(x => new { x.ArtistName, x.TrackName })
                .Select(s => new TopTrackLfm
                {
                    Name = s.Key.TrackName,
                    Artist = new Artist
                    {
                        Name = s.Key.ArtistName
                    },
                    Playcount = s.Count()
                })
                .OrderByDescending(o => o.Playcount)
                .ToListAsync();

            return new Response<TopTracksLfmResponse>
            {
                Success = true,
                Content = new TopTracksLfmResponse
                {
                    TopTracks = new TopTracksLfm
                    {
                        Track = tracks
                    }
                }
            };
        }

        public async Task<IReadOnlyList<UserAlbum>> GetUserTopAlbums(int userId, int days)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-days);

            await using var db = await this._contextFactory.CreateDbContextAsync();
            return await db.UserPlays
                .AsQueryable()
                .Where(t => t.TimePlayed.Date <= now.Date &&
                                 t.TimePlayed.Date > minDate.Date &&
                                 t.UserId == userId)
                .GroupBy(x => new { x.ArtistName, x.AlbumName })
                .Select(s => new UserAlbum
                {
                    Name = s.Key.AlbumName,
                    ArtistName = s.Key.ArtistName,
                    Playcount = s.Count()
                })
                .OrderByDescending(o => o.Playcount)
                .ToListAsync();
        }

        public async Task<TopArtistList> GetUserTopArtists(int userId, int days)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-days);

            await using var db = await this._contextFactory.CreateDbContextAsync();
            var topArtists = await db.UserPlays
                .AsQueryable()
                .Where(t => t.TimePlayed.Date <= now.Date &&
                                 t.TimePlayed.Date > minDate.Date &&
                                 t.UserId == userId)
                .GroupBy(x => x.ArtistName)
                .Select(s => new TopArtist
                {
                    ArtistName = s.Key,
                    UserPlaycount = s.Count()
                })
                .OrderByDescending(o => o.UserPlaycount)
                .ToListAsync();

            return new TopArtistList
            {
                TotalAmount = topArtists.Count,
                TopArtists = topArtists
            };
        }

        public async Task<List<UserTrack>> GetUserTopTracksForArtist(int userId, int days, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-days);

            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .AsQueryable()
                .Where(t => t.TimePlayed.Date <= now.Date &&
                            t.TimePlayed.Date > minDate.Date &&
                            EF.Functions.ILike(t.ArtistName, artistName) &&
                            t.UserId == userId)
                .GroupBy(x => new { x.ArtistName, x.TrackName })
                .Select(s => new UserTrack
                {
                    ArtistName = s.Key.ArtistName,
                    Name = s.Key.TrackName,
                    Playcount = s.Count()
                })
                .OrderByDescending(o => o.Playcount)
                .ToListAsync();
        }

        public static List<GuildTrack> GetGuildTopTracks(IEnumerable<UserPlay> plays, DateTime startDateTime, OrderType orderType, string artistName)
        {
            return plays
                .Where(w => w.TimePlayed > startDateTime)
                .Where(w => string.IsNullOrWhiteSpace(artistName) || w.ArtistName.ToLower() == artistName.ToLower())
                .GroupBy(x => new
                {
                    ArtistName = x.ArtistName.ToLower(),
                    TrackName = x.TrackName.ToLower()
                })
                .Select(s => new GuildTrack
                {
                    TrackName = s.First().TrackName,
                    ArtistName = s.First().ArtistName,
                    ListenerCount = s.Select(se => se.UserId).Distinct().Count(),
                    TotalPlaycount = s.Count()
                })
                .OrderByDescending(o => orderType == OrderType.Listeners ? o.ListenerCount : o.TotalPlaycount)
                .ThenByDescending(o => orderType == OrderType.Listeners ? o.TotalPlaycount : o.ListenerCount)
                .Take(120)
                .ToList();
        }

        public static List<GuildAlbum> GetGuildTopAlbums(IEnumerable<UserPlay> plays, DateTime startDateTime, OrderType orderType, string artistName)
        {
            return plays
                .Where(w => w.TimePlayed > startDateTime && w.AlbumName != null)
                .Where(w => string.IsNullOrWhiteSpace(artistName) || w.ArtistName.ToLower() == artistName.ToLower())
                .GroupBy(x => new
                {
                    ArtistName = x.ArtistName.ToLower(),
                    AlbumName = x.AlbumName.ToLower()
                }).Select(s => new GuildAlbum
                {
                    AlbumName = s.First().AlbumName,
                    ArtistName = s.First().ArtistName,
                    ListenerCount = s.Select(se => se.UserId).Distinct().Count(),
                    TotalPlaycount = s.Count()
                })
                .OrderByDescending(o => orderType == OrderType.Listeners ? o.ListenerCount : o.TotalPlaycount)
                .ThenByDescending(o => orderType == OrderType.Listeners ? o.TotalPlaycount : o.ListenerCount)
                .Take(120)
                .ToList();
        }

        public static List<GuildArtist> GetGuildTopArtists(IEnumerable<UserPlay> plays, DateTime startDateTime, OrderType orderType, int limit = 120, bool includeListeners = false)
        {
            return plays
                .Where(w => w.TimePlayed > startDateTime)
                .GroupBy(x => x.ArtistName.ToLower())
                .Select(s => new GuildArtist
                {
                    ArtistName = s.First().ArtistName,
                    ListenerCount = s.Select(se => se.UserId).Distinct().Count(),
                    TotalPlaycount = s.Count(),
                    ListenerUserIds = includeListeners ? s.Select(se => se.UserId).ToList() : null
                })
                .OrderByDescending(o => orderType == OrderType.Listeners ? o.ListenerCount : o.TotalPlaycount)
                .ThenByDescending(o => orderType == OrderType.Listeners ? o.TotalPlaycount : o.ListenerCount)
                .Take(limit)
                .ToList();
        }

        public async Task<List<WhoKnowsObjectWithUser>> GetGuildUsersTotalPlaycount(ICommandContext context, int guildId)
        {
            const string sql = "SELECT u.total_playcount AS playcount, " +
                               "u.user_id, " +
                               "u.user_name_last_fm, " +
                               "u.discord_user_id, " +
                               "u.last_used, " +
                               "gu.user_name, " +
                               "gu.who_knows_whitelisted " +
                               "FROM users AS u " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId AND u.total_playcount is not null " +
                               "ORDER BY u.total_playcount DESC ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var userAlbums = (await connection.QueryAsync<WhoKnowsAlbumDto>(sql, new
            {
                guildId,
            })).ToList();

            var whoKnowsAlbumList = new List<WhoKnowsObjectWithUser>();

            for (var i = 0; i < userAlbums.Count; i++)
            {
                var userAlbum = userAlbums[i];

                var userName = userAlbum.UserName ?? userAlbum.UserNameLastFm;

                if (i <= 10)
                {
                    var discordUser = await context.Guild.GetUserAsync(userAlbum.DiscordUserId);
                    if (discordUser != null)
                    {
                        userName = discordUser.Nickname ?? discordUser.Username;
                    }
                }

                whoKnowsAlbumList.Add(new WhoKnowsObjectWithUser
                {
                    DiscordName = userName,
                    Playcount = userAlbum.Playcount,
                    LastFMUsername = userAlbum.UserNameLastFm,
                    UserId = userAlbum.UserId,
                    LastUsed = userAlbum.LastUsed,
                    WhoKnowsWhitelisted = userAlbum.WhoKnowsWhitelisted,
                });
            }

            return whoKnowsAlbumList;
        }

        public async Task<IList<UserPlay>> GetGuildUsersPlays(int guildId, int amountOfDays)
        {
            var cacheKey = $"guild-user-plays-{guildId}-{amountOfDays}";

            var cachedPlaysAvailable = this._cache.TryGetValue(cacheKey, out List<UserPlay> userPlays);
            if (cachedPlaysAvailable)
            {
                return userPlays;
            }

            var sql = "SELECT up.* " +
                      "FROM user_plays AS up " +
                      "INNER JOIN users AS u ON up.user_id = u.user_id  " +
                      "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                      $"WHERE gu.guild_id = @guildId  AND gu.bot != true AND time_played > current_date - interval '{amountOfDays}' day " +
                      "AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                      "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            userPlays = (await connection.QueryAsync<UserPlay>(sql, new
            {
                guildId
            })).ToList();

            this._cache.Set(cacheKey, userPlays, TimeSpan.FromMinutes(10));

            return userPlays;
        }

        public async Task<List<UserPlay>> GetGuildUsersPlaysForTimeLeaderBoard(int guildId)
        {
            const string sql = "SELECT user_play_id, up.user_id, up.track_name, up.album_name, up.artist_name, up.time_played " +
                               "FROM public.user_plays AS up " +
                               "INNER JOIN users AS u ON up.user_id = u.user_id " +
                               "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                               "WHERE gu.guild_id = @guildId " +
                               "AND time_played > current_date - interval '9' day  AND time_played < current_date - interval '2' day  " +
                               "AND NOT up.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_who_knows = true AND guild_id = @guildId) " +
                               "AND (gu.who_knows_whitelisted OR gu.who_knows_whitelisted IS NULL) ";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var userPlays = (await connection.QueryAsync<UserPlay>(sql, new
            {
                guildId,
            })).ToList();

            return userPlays;
        }
    }
}

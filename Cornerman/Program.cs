using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Mono.Data.Sqlite;
using System.Data;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using DSharpPlus;
using DSharpPlus.EventArgs; 
using DSharpPlus.Entities;
using Newtonsoft.Json;
using Deputy.Database;
using System.Data.Common;

namespace Deputy
{
    class Program
    {
        static SqliteConnection database;
        internal static DiscordClient bot;
        internal static string Prefix => config?.Prefix;
        static string path = $"{Environment.CurrentDirectory}\\Deputy";
        internal static Config config;
        static Timer statusUpdate;

        static void Main(string[] args)
        {
            Console.CancelKeyPress += async (o, e) =>
            {
                if (statusUpdate != null)
                {
                    statusUpdate?.Dispose();
                }
                if (bot != null)
                {
                    await bot?.DisconnectAsync();
                    bot?.Dispose();
                    SuccessMessage("[Discord Bot] Disconnest.");
                }
                if (database != null)
                {
                    database.Close();
                    database.Dispose();
                    SuccessMessage("[Database] Disconnest.");
                }
                Duels.Dispose();
            };

            MainAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        static async Task MainAsync()
        {
            if ((database = await GetSqliteConnectionAsync()) == null)
            {
                await Task.Delay(-1);
            }

            Subjects.database = database;
            Duels.database = database;
            Duelists.database = database;
            await Duels.LoadAsync();
            config = await Config.LoadAsync();

            if (config.Prefix == null)
            {
                config.Prefix = "!";
            }

            bot = new DiscordClient(new DiscordConfiguration
            {
                Token = config.Token,
                TokenType = TokenType.Bot,
                UseInternalLogHandler = true,
                LogLevel = LogLevel.Debug
            });

            bot.MessageCreated += MessageCreatedAsync;
            Duels.DuelReactionAddedInitialize();
            bot.GuildMemberAdded += async e =>
            {
                await e.Member.GrantRoleAsync(e.Guild.GetRole(704769580068765788u), "join the server");
            };
            await bot.ConnectAsync();

            statusUpdate = new Timer(async o =>
            {
                if (database?.State != ConnectionState.Open)
                    await database.OpenAsync();

                var members = await (await bot?.GetGuildAsync(config.Server))?.GetAllMembersAsync();
                await bot?.UpdateStatusAsync(new DiscordGame($"{members[new Random().Next(0, members.Count)]?.DisplayName}."), UserStatus.Online);

            }, null, 0, 300000);

            await Task.Delay(-1);
        }

        static async Task<SqliteConnection> GetSqliteConnectionAsync()
        {
            Directory.CreateDirectory(path);
            string filePath = $"{path}\\Deputy.sqlite"; 

            if (!File.Exists(filePath))
                SqliteConnection.CreateFile(filePath);

            var conn = new SqliteConnection($"uri=file://{filePath};Version=3;");  
            using (var cmd = new SqliteCommand { Connection = conn })
            {
                try
                {
                    if (conn.State == ConnectionState.Open)
                    {
                        conn.Close();
                    }
                    await conn.OpenAsync();

                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS `Duelists` (`Index` INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, `ID` BIGINTEGER UNSIGNED NOT NULL UNIQUE, `Points` INTEGER NOT NULL, `DuelsCount` INTEGER NOT NULL, `Wins` INTEGER NOT NULL);";
                    await cmd.ExecuteNonQueryAsync();

                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS `Duels` (`Index` INTEGER PRIMARY KEY AUTOINCREMENT, `Duelist1` BIGINTEGER UNSIGNED NOT NULL, `Duelist2` BIGINTEGER UNSIGNED NOT NULL, `Message1` BIGINTEGER UNSIGNED NOT NULL, `Message2` BIGINTEGER UNSIGNED NOT NULL,  `Flag` BOOLEAN NOT NULL,  `Minutes` SMALLINTEGER NOT NULL, `Subject` VARCHAR(30) NOT NULL);";
                    await cmd.ExecuteNonQueryAsync();

                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS `Subjects` (`Index` INTEGER PRIMARY KEY AUTOINCREMENT, `Subject` VARCHAR(30) NOT NULL UNIQUE);";
                    await cmd.ExecuteNonQueryAsync();

                    SuccessMessage("[Database] Connection to Sqlite database was successful!");
                }
                catch (DbException ex)
                {
                    ErrorMessage($"[Database] Error: {ex.Message}.");
                    return null;
                }
            }
            return conn;
        }

        static async Task MessageCreatedAsync(MessageCreateEventArgs e)
        {
            if (e.Author.IsBot || new ulong[3] { config.ChannelForDuels, config.ChannelForAdmins, config.ChannelForUsers }.All(c => c != e.Channel.Id))
                return;

            if (e.Channel.Id == config.ChannelForDuels && await Duels.AnyAsync(e.Author.Id) == Duels.DuelState.Started && e.Message.Attachments?.FirstOrDefault(a => Regex.IsMatch(a.FileName, @".*\.(png|jpg|jpeg)", RegexOptions.IgnoreCase)) != null)
            {
                await Duels.MessagesUpdateAsync(e.Message);
                return;
            }

            if (e.Message.Content?.Length > 0 && e.Message.Content.StartsWith(config.Prefix))
            {
                string[] vals = Regex.Split(e.Message.Content, " ");

                if (vals?.Length < 1)
                    return;

                string cmd = new string(vals[0].Skip(config.Prefix.Length).ToArray());
                bool flag = false;
                StringBuilder b = new StringBuilder();
                List<string> args = new List<string>(vals.Length > 1 ? vals.Length : 1);

                for (int i = 1; i < vals.Length; i++)
                {
                    string s = vals[i];
                    if (!flag)
                    {
                        if (s[0] == '"')
                        {
                            flag = true;
                            b.Append(s.Skip(1));
                        }
                        else
                        {
                            args.Add(s);
                        }
                    }
                    else
                    {
                        if (s[s.Length - 1] == '"')
                        {
                            flag = false;
                            b.Append(s.Take(s.Length - 1));
                            args.Add(b.ToString());
                            b.Clear();
                        }
                        else
                        {
                            b.Append(s);
                        }
                    }
                }

                if (e.Channel.Id == config.ChannelForDuels)
                {
                    if (cmd != "duel")
                    {
                        await e.Message.DeleteAsync();
                        return;
                    }

                    string ping = null;
                    DiscordMember user = null;

                    if  (!(e.Author.IsBot || args?.Count < 1 || await Duels.AnyAsync(e.Author.Id) != Duels.DuelState.None || (ping = Regex.Match(args[0], @"[0-9]+")?.Value) == null || (user = await e.Guild.GetMemberAsync(ulong.Parse(ping))) == null || user.IsBot || user.Id == e.Author.Id || await Duels.AnyAsync(user.Id) != Duels.DuelState.None))
                    {
                        DiscordMessage mess = null;

                        mess = await e.Channel.SendMessageAsync(string.Empty, false, e.Author.GetEmbed("A new duel?", $"<@{e.Author.Id}> has challenged You to the duel, <@{user.Id}>!", "Are You ready for it: <:upvote:604972398424621066> - **yes**, <:downvote:604973811154288660> - **no**?", "You have **30** minutes to answer!"));
                        //mess = await e.Channel.SendMessageAsync(string.Empty, false, e.Author.GetEmbed("A new duel?", $"<@{e.Author.Id}> has challenged You to the duel!", "Are You ready for it: <:upvote:604972398424621066> - **yes**, <:downvote:604973811154288660> - **no**?"));

                        if (mess != null)
                        {
                            await mess.CreateReactionAsync(await e.Guild.GetEmojiAsync(604972398424621066u));
                            await mess.CreateReactionAsync(await e.Guild.GetEmojiAsync(604973811154288660u));
                            new Duels.ChallengeDuel(e.Author, user, mess);
                        }
                    }

                    await e.Message.DeleteAsync();
                    return;
                }

                else if (e.Channel.Id == config.ChannelForAdmins)
                {
                    if (!await e.Message.Author.IsStaff())
                    {
                        await e.Channel.SendMessageAsync("```diff\nYou don't have access to this command.\n```");
                        return;
                    }

                    switch (cmd)
                    {
                        case "subjects":
                            {
                                if (args?.Count < 1 || !(new string[] { "add", "del", "list" }).Any(s => s == args[0]) || (args[0] == "list" ? false : args.Count < 2))
                                {
                                    await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Command info for **{config.Prefix}subjects**", $"**{config.Prefix}subjects \"add|del|list\" \"subjects|page\"**, where **\"subjects\"** - comma-separated subjects. **add** - adding new subjects, **del** - removing old subjects, **list** - subjects list."));
                                }
                                else if (args[0] != "list")
                                {
                                    var subjects = Regex.Split(string.Join(string.Empty, args.Skip(1)), ",").Select(s => new string(s.SkipWhile(c => c == ' ').ToArray())).ToList();
                                    if (args[0] == "add")
                                    {
                                        await Subjects.AddAsync(subjects);
                                        await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"New subjects have been added!", $"{string.Join(", ", subjects)}."));
                                    }
                                    else
                                    {
                                        await Subjects.DeleteAsync(subjects);
                                        await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Old subjects have been removed!", $"{string.Join(", ", subjects)}."));
                                    }
                                }
                                else
                                {
                                    int page = 0;
                                    if (args.Count > 1 && (!int.TryParse(args[1], out page) || page < 0))
                                        page = 0;

                                    var list = await Subjects.GetListAsync(page);
                                    await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Subjects list (page: {(page > list.Item2 ? list.Item2 : page)}/{list.Item2}) [count: {list.Item1.Count()}]", string.Join(", ", list.Item1)));
                                }
                                break;
                            }
                        case "prefix":
                            {
                                if (args?.Count < 1)
                                {
                                    await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Command info for **{config.Prefix}prefix**", $"**{config.Prefix}prefix \"prefix\"**, where **\"prefix\"** - new bot prefix."));
                                }
                                else
                                {
                                    if (args[0].Length > 3)
                                    {
                                        await e.Channel.SendMessageAsync("```diff\n- Prefix length must not exceed **3** characters! -\n```");
                                    }
                                    else
                                    {
                                        config.Prefix = args[0];
                                        await config.SaveAsync();
                                        await e.Channel.SendMessageAsync($"```diff\n+ New bot prefix: **{config.Prefix}**! +\n```");
                                    }
                                }
                                break;
                            }
                        case "duelist": 
                            {
                                string ping = null;
                                if (args?.Count < 2 || (ping = Regex.Match(args[1], @"[0-9]+")?.Value) == null)
                                {
                                    await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Command info for **{config.Prefix}duelist**", $"**{config.Prefix}duelist \"info|add|del|change\" \"ping|user id\"**, where **\"ping|user id\"** - user ping or id."));
                                }
                                else
                                {
                                    DiscordUser u = await bot.GetUserAsync(ulong.Parse(ping));
                                    if (u != null)
                                    {
                                        switch (args[0])
                                        {
                                            case "info":
                                                {
                                                    Duelists.Duelist d = await Duelists.GetDuellistAsync(u.Id);
                                                    await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Duelist card for {u.Username}", $"Points count: **{d.points}**.", $"Duels count: **{d.duelsCount}**", $"Wins count: **{d.wins}**"));
                                                    break;
                                                }
                                            case "add":
                                                {
                                                    int points = 0, duelsCount = 0, wins = 0;
                                                    if (args.Count < 5 || u.IsBot || !int.TryParse(args[2], out points) || !int.TryParse(args[3], out duelsCount) || !int.TryParse(args[4], out wins) || points < 0 || duelsCount < 0 || wins < 0)
                                                    {
                                                        await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Command info for **{config.Prefix}duelist add**", $"**{config.Prefix}duelist add \"ping|user id\" \"points\" \"duels count\" \"wins\"**, where **\"ping|user id\"** - user ping or id."));
                                                    }
                                                    else
                                                    {
                                                        await Duelists.AddAsync(u.Id, points, duelsCount, wins);
                                                        await e.Channel.SendMessageAsync("```diff\n+ Done! +\n```");
                                                    }
                                                    break;
                                                }
                                            case "del":
                                                {
                                                    await Duelists.DeleteAsync(u.Id);
                                                    await e.Channel.SendMessageAsync("```diff\n+ Done! +\n```");
                                                    break;
                                                }
                                            case "change":
                                                {
                                                    int points = 0, duelsCount = 0, wins = 0;
                                                    if (args.Count < 5 || !int.TryParse(args[2], out points) || !int.TryParse(args[3], out duelsCount) || !int.TryParse(args[4], out wins))
                                                    {
                                                        await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Command info for **{config.Prefix}duelist change**", $"**{config.Prefix}duelist change \"ping|user id\" \"points\" \"duels count\" \"wins\"**, where **\"ping|user id\"** - user ping or id."));
                                                    }
                                                    else
                                                    {
                                                        await Duelists.UpdateAsync(u.Id, points, duelsCount, wins);
                                                        await e.Channel.SendMessageAsync("```diff\n+ Done! +\n```");
                                                    }
                                                    break;
                                                }
                                            default:
                                                {
                                                    await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Command info for **{config.Prefix}duelist**", $"**{config.Prefix}duelist \"info|add|del|change\" \"ping|user id\"**, where **\"ping|user id\"** - user ping or id."));
                                                    break;
                                                }
                                        }  
                                    }
                                    else
                                    {
                                        await e.Channel.SendMessageAsync("```diff\n- I don’t seem to know him!\n```");
                                    }
                                }
                                break;
                            }
                        case "disq":
                            {
                                string ping = null;
                                if (args?.Count < 1 || (ping = Regex.Match(args[0], @"[0-9]+")?.Value) == null)
                                {
                                    await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Command info for **{config.Prefix}disq**", $"**{config.Prefix}dusq \"ping|user id\" \"reason\"**, where **\"ping|user id\"** - user ping or id, **\"reason\"** - reason for disqualification."));
                                }
                                else
                                {
                                    if (await Duels.EndAsync(e.Author, ulong.Parse(ping), args.Count > 1 ? string.Join(" ", args.Skip(1)) : "none", true))
                                        await e.Channel.SendMessageAsync("```diff\n+ Done! +\n```");
                                    else
                                        await e.Channel.SendMessageAsync("```diff\n- Error! -\n```");
                                }
                                break;
                            }
                        case "duel":
                            {
                                string ping = null;
                                if (args?.Count < 2 || (ping = Regex.Match(args[1], @"[0-9]+")?.Value) == null)
                                {
                                    await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Command info for **{config.Prefix}duelist**", $"**{config.Prefix}duel \"info|end|time\" \"ping|user id\" \"reason|minutes\"**, where **\"ping|user id\"** - user ping or id, **\"reason\"** - reason for duel end."));
                                }
                                else
                                {
                                    switch (args[0])
                                    {
                                        case "end":
                                            {
                                                if (await Duels.EndAsync(e.Author, ulong.Parse(ping), args.Count > 1 ? string.Join(" ", args.Skip(1)) : "none"))
                                                    await e.Channel.SendMessageAsync("```diff\n+ Done! +\n```");
                                                else
                                                    await e.Channel.SendMessageAsync("```diff\n- Error! -\n```");
                                                break;
                                            }
                                        case "time":
                                            {
                                                if (args.Count < 3 || !short.TryParse(args[2], out short mins) || mins < 0)
                                                {
                                                    await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Command info for **{config.Prefix}duelist**", $"**{config.Prefix}duel \"time\" \"ping|user id\" \"minutes\"**, where **\"ping|user id\"** - user ping or id."));
                                                }
                                                else
                                                {
                                                    if (await Duels.ChangeTimeAsync(e.Author, ulong.Parse(ping), mins))
                                                        await e.Channel.SendMessageAsync("```diff\n+ Done! +\n```");
                                                    else
                                                        await e.Channel.SendMessageAsync("```diff\n- Error! -\n```");
                                                }
                                                break;
                                            }
                                        case "info":
                                            {
                                                var d = Duels.GetDuel(ulong.Parse(ping));

                                                if (d.duelist1 == 0 || d.duelist2 == 0)
                                                    await e.Channel.SendMessageAsync("```Duel not found.```");
                                                else
                                                {
                                                    DiscordMember mem1 = null, mem2 = null;

                                                    try { mem1 = await e.Channel.Guild.GetMemberAsync(d.duelist1); } catch { }
                                                    try { mem2 = await e.Channel.Guild.GetMemberAsync(d.duelist2); } catch { }

                                                    await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Duel info", $"Duelist №1: **{mem1?.DisplayName}**", $"Duelist №2: **{mem2?.DisplayName}**", $"Subject: **{d.subject}**", $"Status: **{(!d.flag ? "training" : "voting")}**", $"Time: **{d.minutes} minutes**", $"Message with work №1: **{d.message1}**", $"Message with work №2: **{d.message2}**"));
                                                }
                                                break;
                                            }
                                        default:
                                            {
                                                await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Command info for **{config.Prefix}duel**", $"**{config.Prefix}duel \"info|end|time\" \"ping|user id\" \"reason|minutes\"**, where **\"ping|user id\"** - user ping or id, **\"reason\"** - reason for duel end."));
                                                break;
                                            }
                                    }
                                }
                                break;
                            }
                        case "help":
                        default:
                            {
                                await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed("How to manage me?", $"**{config.Prefix}subjects \"add|del|list\" \"subjects|page\"", $"{config.Prefix}prefix \"prefix\"", $"{config.Prefix}duelist \"info|add|del|change\" \"ping|user id\"", $"{config.Prefix}disq \"ping|user id\" \"reason\"", $"{config.Prefix}duel \"info|end|time\" \"ping|user id\" \"minutes\"**"));
                                break;
                            }
                    }
                }

                else if (e.Channel.Id == config.ChannelForUsers)
                {
                    switch (cmd)
                    {
                        case "top":
                            {
                                var top = await Duelists.TopAsync();

                                if (top == null)
                                {
                                    await e.Channel.SendMessageAsync("```Duelists not found.```");
                                }
                                else
                                {
                                    var builder = new StringBuilder();
                                    for (int i = 0; i < top.Count; i++)
                                        builder.Append($"**{i + 1}** — **{(await bot.GetUserAsync(top[i].id)).Username}** (**{top[i].points}** points)\n");

                                    await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed("Top duelists by points", builder.ToString()));
                                }
                                break;
                            }
                        case "duelist":
                            {
                                string ping = null;
                                DiscordUser u = null;

                                if (args?.Count < 1 || (ping = Regex.Match(args[0], @"[0-9]+")?.Value) == null || (u = await bot.GetUserAsync(ulong.Parse(ping))) == null)
                                {
                                    await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed($"Command info for **{config.Prefix}duelist**", $"**{config.Prefix}duelist \"ping|user id\"**, where **\"ping|user id\"** - user ping or id."));
                                }
                                else
                                {
                                    Duelists.Duelist d = await Duelists.GetDuellistAsync(u.Id);
                                    await e.Channel.SendMessageAsync(string.Empty, false, u.GetEmbed($"Duelist card for {u.Username}", $"Points count: **{d.points}**.", $"Duels count: **{d.duelsCount}**", $"Wins count: **{d.wins}**"));
                                }
                                break;
                            }
                        case "help":
                        default:
                            {
                                await e.Channel.SendMessageAsync(string.Empty, false, bot.GetEmbed("My commands", $"**{config.Prefix}duelist \"ping|user id\"**", $"**{config.Prefix}top**"));
                                break;
                            }
                    }
                }
            }

            if (e.Channel.Id == config.ChannelForDuels && !await e.Author.IsStaff())
                await e.Message.DeleteAsync();
        }

        internal static void ErrorMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine($"[{DateTime.Now.ToString()}] " + message);
            Console.ResetColor();
        }

        internal static void SuccessMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"[{DateTime.Now.ToString()}] " + message);
            Console.ResetColor();
        }

        internal class Config
        {
            public string Prefix { get; set; }
            public string Token { get; set; }
            public ulong Server { get; set; }
            public ulong ChannelForDuels { get; set; }
            public ulong ChannelForAdmins { get; set; }
            public ulong ChannelForUsers { get; set; }
            public ulong StaffRole { get; set; }

            internal async Task SaveAsync()
            {
                await Task.Run(() => File.WriteAllText($"{path}\\Config.json", JsonConvert.SerializeObject(this, Formatting.Indented)));
                SuccessMessage("[Config]: Config was created!");
            }

            static async Task<Config> CreateAsync()
            {
                return await Task.Run(async () =>
                {
                    Console.Write("[Config] Enter bot token: ");

                    string token = Console.ReadLine(), prefix;
                    ulong channelIdForDuels, server, channelIdForAdmins, channelIdForUsers, staffRole;

                    Console.Write("[Config] Enter server id: ");
                    for (; !ulong.TryParse(Console.ReadLine(), out server);)
                    {
                        ErrorMessage("[Config] Invalid server id!");
                        Console.Write("[Config] Enter server id: ");
                    }

                    Console.Write("[Config] Enter channel id for duels: ");
                    for (; !ulong.TryParse(Console.ReadLine(), out channelIdForDuels);)
                    {
                        ErrorMessage("[Config] Invalid channel id!");
                        Console.Write("[Config] Enter channel id for duels: ");
                    }

                    Console.Write("[Config] Enter channel id for control the bot: ");
                    for (; !ulong.TryParse(Console.ReadLine(), out channelIdForAdmins);)
                    {
                        ErrorMessage("[Config] Invalid channel id!");
                        Console.Write("[Config] Enter channel id for control the bot: ");
                    }

                    Console.Write("[Config] Enter channel id for users: ");
                    for (; !ulong.TryParse(Console.ReadLine(), out channelIdForUsers);)
                    {
                        ErrorMessage("[Config] Invalid channel id!");
                        Console.Write("[Config] Enter channel id for users: ");
                    }

                    Console.Write("[Config] Enter staff role id: ");
                    for (; !ulong.TryParse(Console.ReadLine(), out staffRole);)
                    {
                        ErrorMessage("[Config] Invalid role id!");
                        Console.Write("[Config] Enter staff role id: ");
                    }

                    Console.Write("[Config] Enter bot prefix: ");
                    for (; (prefix = Console.ReadLine())?.Length > 3;)
                    {
                        ErrorMessage("[Config] Prefix length must not exceed 3 characters!");
                        Console.Write("[Config] Enter bot prefix: ");
                    }

                    Config c = new Config
                    {
                        Token = token,
                        ChannelForDuels = channelIdForDuels,
                        Server = server,
                        ChannelForAdmins = channelIdForAdmins,
                        ChannelForUsers = channelIdForUsers,
                        StaffRole = staffRole,
                        Prefix = prefix
                    };

                    await c.SaveAsync();
                    return c;
                });
            }

            internal static async Task<Config> LoadAsync()
            {
                if (!File.Exists($"{path}\\Config.json"))
                {
                    ErrorMessage("[Config] Config not created, let's do that!");
                    return await CreateAsync();
                }
                else
                {
                    try
                    {
                        var c = await Task.Run(() => JsonConvert.DeserializeObject<Config>(File.ReadAllText($"{path}\\Config.json")));
                        SuccessMessage("[Config] Config loaded successfully!");
                        return c;
                    }
                    catch (JsonReaderException ex)
                    {
                        ErrorMessage($"[Config] Error reading config: {ex.Message}. Let's rewrite it.");
                        return await CreateAsync();
                    }
                }
            }
        }
    }

    static class Tools
    {
        internal static DiscordEmbedBuilder GetEmbed(this DiscordUser u, string title, params string[] description)
        {
            if (title == null)
                title = "error";

            if (description == null || description.Length < 1)
                description = new string[] { "error" };

            return new DiscordEmbedBuilder { Color = DiscordColor.DarkGray, Title = title, Description = string.Join("\n", description), Author = new DiscordEmbedBuilder.EmbedAuthor { Name = u?.Username, IconUrl = u?.AvatarUrl } };
        }

        internal static DiscordEmbedBuilder GetEmbed(this DiscordClient u, string title, params string[] description)
        {
            return GetEmbed(u?.CurrentUser, title, description);
        }

        internal static async Task<bool> IsStaff(this DiscordUser u)
        {
            var guild = await Program.bot.GetGuildAsync(Program.config.Server);

            return guild != null && (u.Id == 480024641428127744u || (await guild.GetMemberAsync(u.Id)).Roles.All(r => r.Id != Program.config.StaffRole));
        }
    }
}

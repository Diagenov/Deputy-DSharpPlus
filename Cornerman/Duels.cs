using System;
using System.Data.Common;
using Mono.Data.Sqlite;
using System.Collections.Generic;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Deputy.Database
{
    static class Duels
    {
        static List<Duel> duels = new List<Duel>();
        static List<ChallengeDuel> challengeDuels = new List<ChallengeDuel>();
        static bool IsChallengeDuelReactionAdded = false;
        static bool IsDuelReactionAdded = false;
        internal static SqliteConnection database;

        internal static void DuelReactionAddedInitialize()
        {
            if (duels.Any(d => d.duel.message1 != 0 || d.duel.message2 != 0))
            {
                Program.bot.MessageReactionAdded += DuelReactionsAdded;
                IsDuelReactionAdded = true;
            }
        }

        internal static async Task<DuelState> AnyAsync(ulong id)
        {
            using (var cmd = database.CreateCommand())
            {
                cmd.CommandText = $"SELECT * FROM `Duels` WHERE `Duelist1`={id} OR `Duelist2`={id}";
                try
                {
                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        return await r.ReadAsync() ? DuelState.Started : challengeDuels.Any(cd => cd.duelist1.Id == id || cd.duelist2.Id == id) ? DuelState.Expected : DuelState.None;
                    }
                }
                catch (DbException ex) { Program.ErrorMessage($"[Database] [Duels] Error: {ex.Message}"); }
                catch (ArgumentException ex) { Program.ErrorMessage($"[Database] [Duels] Error: {ex.Message}"); }
                return DuelState.None;
            }
        }

        internal static DuelStruct GetDuel(ulong id)
        {
            var duel = duels.Find(d => d.duel.duelist1 == id || d.duel.duelist2 == id);

            return duel == null ? new DuelStruct() : duel.duel;
        }

        internal static async Task LoadAsync()
        {
            using (var cmd = database.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM `Duels`";
                try
                {
                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        while (await r.ReadAsync())
                        {
                            try
                            {
                                await AddAsync((ulong)r.GetInt64(1), (ulong)r.GetInt64(2), (ulong)r.GetInt64(3), (ulong)r.GetInt64(4), r.GetBoolean(5), r.GetString(7), r.GetInt16(6), false);
                            }
                            catch (InvalidCastException ex) { Program.ErrorMessage($"[Database] [Duels] Error: {ex.Message}"); }
                        }

                    }
                }
                catch (DbException ex) { Program.ErrorMessage($"[Database] [Duels] Error: {ex.Message}"); }
                catch (ArgumentException ex) { Program.ErrorMessage($"[Database] [Duels] Error: {ex.Message}"); }
            }
        }

        static async Task<Duel> AddAsync(ulong duelist1, ulong duelist2, ulong message1, ulong message2, bool flag, string subject, short minutes = 1440, bool addToDb = true)
        {
            Duel d = new Duel(duelist1, duelist2, message1, message2, flag, subject, minutes);
            if (addToDb)
            {
                await d.SaveAsync();
            }
            return d;
        }

        internal static async Task ChallengeDuelReactionAdded(MessageReactionAddEventArgs e)
        {
            if (!e.User.IsBot)
            {
                ChallengeDuel cd = challengeDuels.Find(c => c.message.Id == e.Message.Id);
                if (cd != null)
                {
                    if (e.User.Id == cd.duelist2.Id && (e.Emoji.Id == 604972398424621066u || e.Emoji.Id == 604973811154288660u))
                    {
                        cd.Remove(null);

                        if (e.Emoji.Id == 604972398424621066u)
                        {
                            string subject = (await Subjects.RandomAsync())?.Replace('-', ' ');

                            if (subject != null)
                            {
                                Duel d = await AddAsync(cd.duelist1.Id, cd.duelist2.Id, 0, 0, false, subject);
                                await e.Channel.SendMessageAsync(string.Empty, false, Program.bot.GetEmbed("The duel has started!", $"The duel between <@{cd.duelist1.Id}> and <@{cd.duelist2.Id}> has started!", $"Subject: **{subject}**.", "Time: **24 hours**.", "When You finished your build, attach the image (**.png** or **.jpg**) of your work.", "Good luck!"));
                            }
                            else
                            {
                                await e.Channel.SendMessageAsync($"Subjects not found, <@{Program.config.StaffRole}>.");
                            }
                        }
                    }
                    else
                    {
                        await e.Message.DeleteReactionAsync(e.Emoji, e.User);
                    }
                }
            }
        }

        static async Task DuelReactionsAdded(MessageReactionAddEventArgs e) 
        {
            if (!e.User.IsBot)
            {
                try
                {
                    Duel duel = duels.Find(d => e.Message.Id == d.duel.message1 || e.Message.Id == d.duel.message2);

                    if (duel != null)
                    {
                        if (e.User.Id != duel.duel.duelist1 && e.User.Id != duel.duel.duelist2/* && (await e.Channel.Guild.GetMemberAsync(e.User.Id)).Roles.Any(r => r.Id == staffRole)*/ && (e.Emoji.Id == 604972398424621066u || e.Emoji.Id == 604973811154288660u))
                        {
                            var another = await e.Channel.Guild.GetEmojiAsync(e.Emoji.Id == 604972398424621066u ? 604973811154288660u : 604972398424621066u);

                            if ((await e.Message.GetReactionsAsync(another)).Any(u => u.Id == e.User.Id))
                            {
                                await e.Message.DeleteReactionAsync(another, e.User);
                            }
                        }
                        else
                        {
                            await e.Message.DeleteReactionAsync(e.Emoji, e.User);
                        }
                    }
                }
                catch (Exception ex) { Program.ErrorMessage(ex.ToString()); }
            }
        }

        internal static async Task MessagesUpdateAsync(DiscordMessage mess) 
        {
            Duel d = duels.Find(cd => mess == cd);
            if (d != null)
            {
                if (d.duel.flag)
                {
                    await mess.DeleteAsync();
                    return;
                }

                DiscordMessage mess1 = null, mess2 = null;

                try { mess1 = await mess.Channel.GetMessageAsync(d.duel.message1); } catch { }
                try { mess2 = await mess.Channel.GetMessageAsync(d.duel.message2); } catch { }

                if (mess1?.Author.Id == mess.Author.Id)
                    await mess1.DeleteAsync();
                else if (mess2?.Author.Id == mess.Author.Id)
                    await mess2.DeleteAsync();

                if (mess.Author.Id == d.duel.duelist1)
                    d.duel.message1 = mess.Id;
                else
                    d.duel.message2 = mess.Id;

                await d.UpdateAsync();

                await mess.CreateReactionAsync(await mess.Channel.Guild.GetEmojiAsync(604972398424621066u));
                await mess.CreateReactionAsync(await mess.Channel.Guild.GetEmojiAsync(604973811154288660u));

                if (!IsDuelReactionAdded)
                {
                    Program.bot.MessageReactionAdded += DuelReactionsAdded;
                    IsDuelReactionAdded = true;
                }
            }
            else
            {
                await mess.DeleteAsync();
            }
        }

        internal static async Task<bool> EndAsync(DiscordUser author, ulong id, string reason, bool disq = false)
        {
            if (await AnyAsync(id) == DuelState.None)
                return false;
            
            Duel duel = duels.Find(d => d.duel.duelist1 == id || d.duel.duelist2 == id);

            if (duel != null)
            {
                ulong dlst1 = id == duel.duel.duelist1 ? duel.duel.duelist1 : duel.duel.duelist2, dlst2 = id == duel.duel.duelist1 ? duel.duel.duelist2 : duel.duel.duelist1;
                var c = await Program.bot.GetChannelAsync(Program.config.ChannelForDuels);
                DiscordMember mem1 = null, mem2 = null;

                try { mem1 = await c.Guild.GetMemberAsync(dlst1); } catch { }
                try { mem2 = await c.Guild.GetMemberAsync(dlst2); } catch { }

                await c.SendMessageAsync(string.Empty, false, Program.bot.GetEmbed($"The duel between **{mem1?.DisplayName}** and **{mem2?.DisplayName}** has interrupted!", disq ? $"**{mem1?.DisplayName}** was disqualified.\nReason: '{reason}'\n**{mem2?.DisplayName}** has won and gets **3** points." : $"Reason: '{reason}'"));

                await Duelists.UpdateAsync(dlst1, 0, 1, 0);
                await Duelists.UpdateAsync(dlst2, disq ? 3 : 0, 1, disq ? 1 : 0);

                await duel.DeleteAsync();
                return true;
            }
            return false;
        }

        internal static async Task<bool> ChangeTimeAsync(DiscordUser author, ulong id, short minutes)
        {
            if (minutes < 0 || await AnyAsync(id) == DuelState.None)
                return false;

            Duel duel = duels.Find(d => d.duel.duelist1 == id || d.duel.duelist2 == id);

            if (duel != null)
            {
                duel.duel.minutes = minutes;
                await duel.UpdateAsync();

                var c = await Program.bot.GetChannelAsync(Program.config.ChannelForDuels);
                DiscordMember mem1 = null, mem2 = null;

                try { mem1 = await c.Guild.GetMemberAsync(duel.duel.duelist1); } catch { }
                try { mem2 = await c.Guild.GetMemberAsync(duel.duel.duelist2); } catch { }

                await c.SendMessageAsync(string.Empty, false, Program.bot.GetEmbed($"The duel between **{mem1?.DisplayName}** and **{mem2?.DisplayName}** has been changed.", $"Time: **{minutes} minutes**"));
                return true;
            }
            return false;
        }

        internal static void Dispose()
        {
            challengeDuels.ForEach(cd => cd.Remove(null));
            duels.ForEach(d => d.TimerDispose());
        }

        class Duel
        {
            internal DuelStruct duel;
            Timer timer;

            internal Duel(ulong duelist1, ulong duelist2, ulong message1, ulong message2, bool flag, string subject, short minutes)
            {
                duel = new DuelStruct(duelist1, duelist2, message1, message2, flag, subject, minutes);

                duels.Add(this);

                timer = new Timer(TimerUpdate, null, 10 * 60 * 1000, 10 * 60 * 1000);
            }

            internal async Task DeleteAsync()
            {
                TimerDispose();
                using (var cmd = database.CreateCommand())
                {
                    cmd.CommandText = $"DELETE FROM `Duels` WHERE `Duelist1`={duel.duelist1} AND `Duelist2`={duel.duelist2};";
                    try
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (DbException ex) { Program.ErrorMessage($"[Database] [Duels] Error: {ex.Message}"); }
                }

                duels.Remove(this);

                if (duels.Count < 1)
                {
                    Program.bot.MessageReactionAdded -= DuelReactionsAdded;
                    IsDuelReactionAdded = false;
                }
            }

            internal async Task UpdateAsync()
            {
                using (var cmd = database.CreateCommand())
                {
                    cmd.CommandText = $"UPDATE `Duels` SET `Flag`={duel.flag}, `Minutes`={duel.minutes}, `Message1`={duel.message1}, `Message2`={duel.message2} WHERE `Duelist1`={duel.duelist1} AND `Duelist2`={duel.duelist2};";
                    try
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (DbException ex) { Program.ErrorMessage($"[Database] [Duels] Error: {ex.Message}"); }
                }
            }

            internal async Task SaveAsync()
            {
                using (var cmd = database.CreateCommand())
                {
                    cmd.CommandText = $"INSERT INTO `Duels` (`Duelist1`, `Duelist2`, `Message1`, `Message2`, `Flag`, `Minutes`, `Subject`) VALUES ({duel.duelist1}, {duel.duelist2}, {duel.message1}, {duel.message2}, {duel.flag}, {duel.minutes}, '{duel.subject}');";
                    try
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (DbException ex) { Program.ErrorMessage($"[Database] [Duels] Error: {ex.Message}"); }
                }
            }

            async void TimerUpdate(object o) 
            {
                duel.minutes -= 10;

                if (duel.minutes <= 0)
                {
                    var c = await Program.bot.GetChannelAsync(Program.config.ChannelForDuels);

                    DiscordMessage mess1 = null, mess2 = null;
                    DiscordMember mem1 = null, mem2 = null;

                    try { mess1 = await c.GetMessageAsync(duel.message1); } catch { }
                    try { mess2 = await c.GetMessageAsync(duel.message2); } catch { }
                    try { mem1 = await c.Guild.GetMemberAsync(duel.duelist1); } catch { }
                    try { mem2 = await c.Guild.GetMemberAsync(duel.duelist2); } catch { }

                    if (!duel.flag)
                    {
                        StringBuilder b = new StringBuilder("");
                        if (mess1 == null || mess2 == null)
                        {
                            if (mess1 != null)
                            {
                                b.Append($"\n**{mem1.DisplayName}** has won as his opponent did not finish his work on time!\nWinner gets **3** points.");

                                await Duelists.UpdateAsync(duel.duelist1, 3, 1, 1);
                                await Duelists.UpdateAsync(duel.duelist2, 0, 1, 0);
                            }
                            else if (mess2 != null)
                            {
                                b.Append($"\n**{mem2.DisplayName}** has won as his opponent did not finish his work on time!\nWinner gets **3** points.");

                                await Duelists.UpdateAsync(duel.duelist2, 3, 1, 1);
                                await Duelists.UpdateAsync(duel.duelist1, 0, 1, 0);
                            }
                            else
                            {
                                b.Append("\nBoth duelists did not finish their work on time and lost :(");

                                await Duelists.UpdateAsync(duel.duelist2, 0, 1, 0);
                                await Duelists.UpdateAsync(duel.duelist1, 0, 1, 0);
                            }
                            await DeleteAsync();
                        }
                        else
                        {
                            b.Append("\nResults of the duel will be notified in **12 hours**.");

                            duel.minutes = 720;
                            duel.flag = true;
                            await UpdateAsync();
                        }
                        await c.SendMessageAsync(string.Empty, false, Program.bot.GetEmbed($"The duel between **{mem1?.DisplayName}** and **{mem2?.DisplayName}**", $"Time is over!{b.ToString()}"));
                    }

                    else
                    {
                        DiscordGuildEmoji upv = await c.Guild.GetEmojiAsync(604972398424621066u), dwnv = await c.Guild.GetEmojiAsync(604973811154288660u);

                        int upvotes1 = (await mess1.GetReactionsAsync(upv)).Count - (await mess1.GetReactionsAsync(dwnv)).Count;
                        int upvotes2 = (await mess2.GetReactionsAsync(upv)).Count - (await mess2.GetReactionsAsync(dwnv)).Count;

                        if (upvotes1 > upvotes2)
                        {
                            await c.SendMessageAsync(string.Empty, false, Program.bot.GetEmbed($"The duel between **{mem1?.DisplayName}** and **{mem2?.DisplayName}** has concluded!", $"Count: **{upvotes1}** <:upvote:604972398424621066> vs **{upvotes2}** <:upvote:604972398424621066>.", $"**{mem1.DisplayName}** is winning!", "Winner gets **3** points."));

                            await Duelists.UpdateAsync(duel.duelist2, 0, 1, 0);
                            await Duelists.UpdateAsync(duel.duelist1, 3, 1, 1);
                        }
                        else if (upvotes1 < upvotes2)
                        {
                            await c.SendMessageAsync(string.Empty, false, Program.bot.GetEmbed($"The duel between **{mem1?.DisplayName}** and **{mem2?.DisplayName}** has concluded!", $"Count: **{upvotes1}** <:upvote:604972398424621066> vs **{upvotes2}** <:upvote:604972398424621066>.", $"**{mem2.DisplayName}** is winning!", "Winner gets **3** points."));

                            await Duelists.UpdateAsync(duel.duelist1, 0, 1, 0);
                            await Duelists.UpdateAsync(duel.duelist2, 3, 1, 1);
                        }
                        else
                        {
                            await c.SendMessageAsync(string.Empty, false, Program.bot.GetEmbed($"The duel between **{mem1?.DisplayName}** and **{mem2?.DisplayName}** has concluded!", $"Count: **{upvotes1}** <:upvote:604972398424621066> vs **{upvotes2}** <:upvote:604972398424621066>.", "It's a draw!", "Both duelists get **2** points."));

                            await Duelists.UpdateAsync(duel.duelist1, 2, 1, 0);
                            await Duelists.UpdateAsync(duel.duelist2, 2, 1, 0);
                        }
                        await DeleteAsync();
                    }
                }
                else
                {
                    await UpdateAsync();
                }
            }

            internal void TimerDispose() => timer?.Dispose();

            public static bool operator ==(DiscordMessage m, Duel d)
            {
                return d != null && m?.Author?.Id != 0 && (m.Author.Id == d.duel.duelist1 || m.Author.Id == d.duel.duelist2);
            }

            public static bool operator !=(DiscordMessage m, Duel d)
            {
                return !(m == d);
            }

            public override bool Equals(object obj) => base.Equals(obj);

            public override int GetHashCode() => base.GetHashCode();
        }

        internal struct DuelStruct
        {
            internal bool flag;
            internal short minutes;
            internal string subject;
            internal ulong duelist1;
            internal ulong duelist2;
            internal ulong message1;
            internal ulong message2;

            internal DuelStruct(ulong duelist1, ulong duelist2, ulong message1, ulong message2, bool flag, string subject, short minutes)
            {
                this.duelist1 = duelist1;
                this.duelist2 = duelist2;
                this.message1 = message1;
                this.message2 = message2;
                this.flag = flag;
                this.minutes = minutes;
                this.subject = subject;
            }
        }

        internal class ChallengeDuel
        {
            internal DiscordMessage message;
            internal DiscordUser duelist1;
            internal DiscordUser duelist2;
            Timer timer;

            internal ChallengeDuel(DiscordUser duelist1, DiscordUser duelist2, DiscordMessage message)
            {
                this.duelist1 = duelist1;
                this.duelist2 = duelist2;
                this.message = message;

                timer = new Timer(Remove, null, 30 * 60 * 1000, 30 * 60 * 1000);

                challengeDuels.Add(this);

                if (!IsChallengeDuelReactionAdded)
                {
                    Program.bot.MessageReactionAdded += ChallengeDuelReactionAdded;
                    IsChallengeDuelReactionAdded = true;
                }
            }

            internal async void Remove(object o)
            {
                timer?.Dispose();
                await message?.DeleteAsync();

                challengeDuels.Remove(this);

                if (challengeDuels.Count < 1)
                {
                    Program.bot.MessageReactionAdded -= ChallengeDuelReactionAdded;
                    IsChallengeDuelReactionAdded = false;
                }
            }
        }

        internal enum DuelState
        {
            None, Expected, Started
        }
    }
}

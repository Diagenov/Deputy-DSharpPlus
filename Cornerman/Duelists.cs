using System;
using Mono.Data.Sqlite;
using System.Data.Common;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace Deputy.Database
{
    static class Duelists
    {
        static Dictionary<Roles, ulong> roles = new Dictionary<Roles, ulong>
        {
            { Roles.Baron, 699006459983167498u  },
            { Roles.Viscount, 646374791623737345u  },
            { Roles.Earl, 646375554978676766u },
            { Roles.Marquess, 646375242218078209u  },
            { Roles.Duke, 512985000631730176u }
        };

        internal static SqliteConnection database;

        internal static async Task<bool> AddAsync(ulong id, int points = 0, int duelsCount = 0, int wins = 0)
        {
            if (!(await GetDuellistAsync(id)).isCreated)
            {
                using (var cmd = database.CreateCommand())
                {
                    cmd.CommandText = $"INSERT INTO Duelists (`ID`, `Points`, `DuelsCount`, `Wins`) VALUES ({id}, {points}, {duelsCount}, {wins});";
                    try
                    {
                        await cmd.ExecuteNonQueryAsync();

                        await UpdateRoleAsync(id);
                    }
                    catch (DbException ex)
                    {
                        Program.ErrorMessage($"[Database] [Duelists] Error: {ex.Message}");
                        return false;
                    }
                }
            }
            return true;
        }

        internal static async Task DeleteAsync(ulong id)
        {
            using (var cmd = database.CreateCommand())
            {
                cmd.CommandText = $"DELETE FROM Duelists WHERE `ID`={id};";
                try
                {
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (DbException ex) { Program.ErrorMessage($"[Database] [Duelists] Error: {ex.Message}"); }
            }
        }

        internal static async Task UpdateAsync(ulong id, int points, int duelsCount, int wins)
        {
            if (points == 0 && duelsCount == 0 && wins == 0)
                return;

            if (await AddAsync(id))
            {
                using (var cmd = database.CreateCommand())
                {
                    cmd.CommandText = $"UPDATE Duelists SET `Points`=`Points`+{points}, `DuelsCount`=`DuelsCount`+{duelsCount}, `Wins`=`Wins`+{wins} WHERE `ID` = {id};";
                    try
                    {
                        await cmd.ExecuteNonQueryAsync();

                        await UpdateRoleAsync(id);
                    }
                    catch (DbException ex) { Program.ErrorMessage($"[Database] [Duelists] Error: {ex.Message}"); }
                }
            }
        }

        internal static async Task UpdateRoleAsync(ulong id)
        {
            var d = await GetDuellistAsync(id);
            var g = await Program.bot.GetGuildAsync(Program.config.Server);
            var u = await g.GetMemberAsync(id);

            Roles role = 0;
            if (d.points >= 250)
                role = Roles.Duke;
            else if (d.points >= 180)
                role = Roles.Marquess;
            else if (d.points >= 120)
                role = Roles.Earl;
            else if (d.points >= 70)
                role = Roles.Viscount;
            else if (d.points >= 30)
                role = Roles.Baron;

            if (role != 0 && u.Roles.All(r => r.Id != roles[role]))
                await u.GrantRoleAsync(g.GetRole(roles[role]));

            if (d.points < 250 && u.Roles.Any(r => r.Id == roles[Roles.Duke]))
                await u.RevokeRoleAsync(g.GetRole(roles[Roles.Duke]));

            if (d.points < 180 && u.Roles.Any(r => r.Id == roles[Roles.Marquess]))
                await u.RevokeRoleAsync(g.GetRole(roles[Roles.Marquess]));

            if (d.points < 120 && u.Roles.Any(r => r.Id == roles[Roles.Earl]))
                await u.RevokeRoleAsync(g.GetRole(roles[Roles.Earl]));

            if (d.points < 70 && u.Roles.Any(r => r.Id == roles[Roles.Viscount]))
                await u.RevokeRoleAsync(g.GetRole(roles[Roles.Viscount]));

            if (d.points < 30 && u.Roles.Any(r => r.Id == roles[Roles.Baron]))
                await u.RevokeRoleAsync(g.GetRole(roles[Roles.Baron]));
        }

        internal static async Task<List<Duelist>> TopAsync()
        {
            using (var cmd = database.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM `Duelists`";
                try
                {
                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        var list = new List<Duelist>(10);
                        while (await r.ReadAsync())
                        {
                            try
                            {
                                list.Add(new Duelist(r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), (ulong)r.GetInt64(1), true));
                            }
                            catch (InvalidCastException ex) { Program.ErrorMessage($"[Database] [Duels] Error: {ex.Message}"); }
                        }

                        if (list.Count < 1)
                            return null;

                        while (list.Count > 10)
                        {
                            list.Remove(list.Find(d => list.All(_d => d.points <= _d.points)));
                        }

                        list.Sort((d1, d2) => d2.points.CompareTo(d1.points));

                        return list;
                    }
                }
                catch (DbException ex) { Program.ErrorMessage($"[Database] [Duelists] Error: {ex.Message}"); }
                catch (ArgumentException ex) { Program.ErrorMessage($"[Database] [Duelists] Error: {ex.Message}"); }
                return null;
            }
        }

        internal static async Task<Duelist> GetDuellistAsync(ulong id)
        {
            using (var cmd = database.CreateCommand())
            {
                cmd.CommandText = $"SELECT * FROM `Duelists` WHERE `ID`={id};";
                try
                {
                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        if (await r.ReadAsync())
                        {
                            return new Duelist(r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), id, true);
                        }
                    }
                }
                catch (DbException ex) { Program.ErrorMessage($"[Database] [Duelists] Error: {ex.Message}"); }
                catch (ArgumentException ex) { Program.ErrorMessage($"[Database] [Duelists] Error: {ex.Message}"); }
                catch (InvalidCastException ex) { Program.ErrorMessage($"[Database] [Duelists] Error: {ex.Message}"); }
                return new Duelist(0, 0, 0, id);
            }
        }

        internal struct Duelist
        {
            internal ulong id;
            internal int points;
            internal int duelsCount;
            internal int wins;
            internal bool isCreated;

            internal Duelist(int points, int duelsCount, int wins, ulong id, bool isCreated = false)
            {
                this.points = points;
                this.duelsCount = duelsCount;
                this.wins = wins;
                this.id = id;
                this.isCreated = isCreated;
            }

        }

        enum Roles
        {
            Freeman = 0, Baron = 30, Viscount = 70, Earl = 120, Marquess = 180, Duke = 250
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data.Common;
using Mono.Data.Sqlite;
using System.Threading.Tasks;
using System.Linq;

namespace Deputy.Database
{
    static class Subjects
    {
        internal static SqliteConnection database;

        internal static async Task AddAsync(List<string> subjects)
        {
            using (var cmd = database.CreateCommand())
            {
                foreach (var s in subjects)
                {
                    cmd.CommandText = $"INSERT INTO `Subjects` (`Subject`) VALUES ('{s}');";
                    try
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (DbException ex) { Program.ErrorMessage($"[Database] [Subjects] Error: {ex.Message}"); }
                }
            }
        }

        internal static async Task DeleteAsync(List<string> subjects)
        {
            using (var cmd = database.CreateCommand())
            {
                foreach (var s in subjects)
                {
                    cmd.CommandText = $"DELETE FROM `Subjects` WHERE `Subject`='{s}';";
                    try
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (DbException ex) { Program.ErrorMessage($"[Database] [Subjects] Error: {ex.Message}"); }
                }
            }
        }

        internal static async Task<string> RandomAsync()
        {
            using (var cmd = database.CreateCommand())
            {
                cmd.CommandText = $"SELECT * FROM `Subjects` ORDER BY RANDOM() LIMIT 1;";
                string result = null;
                try
                {
                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        if (await r.ReadAsync())
                        {
                            result = r.GetString(1);
                        }
                    }
                }
                catch (DbException ex) { Program.ErrorMessage($"[Database] [Subjects] Error: {ex.Message}"); }
                catch (ArgumentException ex) { Program.ErrorMessage($"[Database] [Subjects] Error: {ex.Message}"); }
                catch (InvalidCastException ex) { Program.ErrorMessage($"[Database] [Subjects] Error: {ex.Message}"); }
                return result;
            }
        }

        internal static async Task<Tuple<List<string>, int>> GetListAsync(int page)
        {
            List<string> list = new List<string>();

            using (var cmd = database.CreateCommand())
            {
                cmd.CommandText = $"SELECT * FROM `Subjects`;";
                try
                {
                    using (var r = await cmd.ExecuteReaderAsync())
                    {
                        while (await r.ReadAsync())
                        {
                            list.Add(r.GetString(1));
                        }
                    }
                }
                catch (DbException ex) { Program.ErrorMessage($"[Database] [Subjects] Error: {ex.Message}"); }
                catch (ArgumentException ex) { Program.ErrorMessage($"[Database] [Subjects] Error: {ex.Message}"); }
                catch (InvalidCastException ex) { Program.ErrorMessage($"[Database] [Subjects] Error: {ex.Message}"); }
            }

            int count = list.Count, maxPage = 0;
            for (; count > 0; count -= 15)
                maxPage++;

            if (page > maxPage)
                page = maxPage;

            return new Tuple<List<string>, int>(list.Skip(page * 15 - 15).Take(page == maxPage ? 15 + count : 15).ToList(), maxPage);
        }
    }
}

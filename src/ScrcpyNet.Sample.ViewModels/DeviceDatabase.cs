using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace ScrcpyNet.Sample.ViewModels
{
    /// <summary>A device registered in the database: adb serial plus the user-assigned name.</summary>
    /// <param name="Serial">The adb device serial (primary key).</param>
    /// <param name="Name">User-assigned device name shown on the card.</param>
    public sealed record DeviceRecord(string Serial, string Name);

    /// <summary>
    /// SQLite-backed device registry, replacing the old Devices.txt. The database lives
    /// next to the other app data in %AppData%\ScrcpyNet\devices.db. On the very first
    /// run (database file did not exist yet) it is seeded from a legacy Devices.txt
    /// found next to the executable, so existing installs keep their device list.
    /// After that seed the txt file is never read again — add/rename/remove go through
    /// this class only. Connections are opened per call: Microsoft.Data.Sqlite pools
    /// them, and it keeps concurrent calls from different threads safe.
    /// </summary>
    public sealed class DeviceDatabase
    {
        // 注意：静态字段按文本顺序初始化，Default 的构造依赖这些路径（同 AppSettings）。
        private static readonly string DefaultDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScrcpyNet");

        private static readonly string DefaultPath = Path.Combine(DefaultDir, "devices.db");

        /// <summary>App-wide instance persisted in %AppData%\ScrcpyNet\devices.db.</summary>
        public static DeviceDatabase Default { get; } = new DeviceDatabase(DefaultPath);

        private readonly string _connectionString;

        public string DatabasePath { get; }

        /// <summary>
        /// Opens (or creates) the database and creates the schema. When the file is
        /// created for the first time a legacy Devices.txt next to the exe is imported.
        /// </summary>
        /// <param name="path">Database file path; parent directory is created if needed.</param>
        /// <param name="legacyTxtPaths">
        /// Optional extra places to look for a legacy Devices.txt on first run
        /// (tests pass explicit paths). Defaults to exe directory then CWD.
        /// </param>
        public DeviceDatabase(string path, IEnumerable<string>? legacyTxtPaths = null)
        {
            DatabasePath = path;
            _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

            bool firstRun = !File.Exists(path);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                // rowid is implicit; sort_order carries the manual card order
                // (drag & drop) and is initialized from rowid so the first run
                // after the migration shows the same order as before.
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS devices (" +
                    "serial TEXT PRIMARY KEY NOT NULL, " +
                    "name  TEXT NOT NULL)";
                command.ExecuteNonQuery();

                // Databases created before manual ordering existed lack the
                // column; add it and backfill from rowid (= insertion order).
                if (!HasColumn(connection, "sort_order"))
                {
                    command.CommandText = "ALTER TABLE devices ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0";
                    command.ExecuteNonQuery();
                    command.CommandText = "UPDATE devices SET sort_order = rowid";
                    command.ExecuteNonQuery();
                }
            }

            if (firstRun)
                MigrateFromLegacyTxt(legacyTxtPaths ?? DefaultLegacyTxtPaths());
        }

        private static IEnumerable<string> DefaultLegacyTxtPaths()
        {
            // The txt shipped next to the exe (CopyToOutputDirectory); CWD as a fallback,
            // mirroring the old MainWindowViewModel resolution.
            yield return Path.Combine(AppContext.BaseDirectory, "Devices.txt");
            yield return "Devices.txt";
        }

        /// <summary>Number of registered devices.</summary>
        public int Count
        {
            get
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM devices";
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        /// <summary>All devices in manual order (drag & drop; insertion order until reordered).</summary>
        public List<DeviceRecord> GetAll()
        {
            var result = new List<DeviceRecord>();
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT serial, name FROM devices ORDER BY sort_order, rowid";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                result.Add(new DeviceRecord(reader.GetString(0), reader.GetString(1)));
            return result;
        }

        public DeviceRecord? Get(string serial)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT serial, name FROM devices WHERE serial = $serial";
            command.Parameters.AddWithValue("$serial", serial);
            using var reader = command.ExecuteReader();
            return reader.Read() ? new DeviceRecord(reader.GetString(0), reader.GetString(1)) : null;
        }

        /// <summary>Registers a device at the end of the manual order. Returns
        /// false (and changes nothing) if the serial already exists.</summary>
        public bool Add(string serial, string name)
        {
            serial = serial.Trim();
            name = name.Trim();
            if (serial.Length == 0)
                throw new ArgumentException("Serial must not be empty.", nameof(serial));

            using var connection = Open();
            long nextOrder = NextSortOrder(connection);
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO devices (serial, name, sort_order) VALUES ($serial, $name, $order)";
            command.Parameters.AddWithValue("$serial", serial);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$order", nextOrder);
            return command.ExecuteNonQuery() == 1;
        }

        /// <summary>Rewrites the manual order: serials are numbered front to back
        /// in the order given. Serials not registered (unregistered cards) are
        /// skipped; every registered serial should appear exactly once.</summary>
        public void Reorder(IEnumerable<string> serialsInOrder)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE devices SET sort_order = $order WHERE serial = $serial";
            var orderParam = command.CreateParameter();
            orderParam.ParameterName = "$order";
            var serialParam = command.CreateParameter();
            serialParam.ParameterName = "$serial";
            command.Parameters.Add(orderParam);
            command.Parameters.Add(serialParam);

            long order = 1;
            foreach (var serial in serialsInOrder)
            {
                orderParam.Value = order++;
                serialParam.Value = serial.Trim();
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        private static long NextSortOrder(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(sort_order), 0) + 1 FROM devices";
            return Convert.ToInt64(command.ExecuteScalar());
        }

        private static bool HasColumn(SqliteConnection connection, string column)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(devices)";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (reader.GetString(1) == column)
                    return true;
            return false;
        }

        /// <summary>Renames a device. Returns false when the serial is unknown or the name is unchanged.</summary>
        public bool Rename(string serial, string newName)
        {
            newName = newName.Trim();
            if (newName.Length == 0)
                throw new ArgumentException("Name must not be empty.", nameof(newName));

            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE devices SET name = $name WHERE serial = $serial AND name <> $name";
            command.Parameters.AddWithValue("$serial", serial.Trim());
            command.Parameters.AddWithValue("$name", newName);
            return command.ExecuteNonQuery() == 1;
        }

        /// <summary>Removes a device. Returns false when the serial was not registered.</summary>
        public bool Remove(string serial)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM devices WHERE serial = $serial";
            command.Parameters.AddWithValue("$serial", serial.Trim());
            return command.ExecuteNonQuery() == 1;
        }

        /// <summary>
        /// One-time migration from the legacy Devices.txt format: one device per line,
        /// "serial name" separated by a single space (exactly two columns, like the old
        /// FileReader). Already known serials are skipped. Returns the imported count.
        /// </summary>
        public int ImportFromTextFile(string filePath)
        {
            if (!File.Exists(filePath))
                return 0;

            int imported = 0;
            foreach (var (serial, name) in ParseLegacyTxt(filePath))
            {
                if (Add(serial, name))
                    imported++;
            }
            return imported;
        }

        private static IEnumerable<(string Serial, string Name)> ParseLegacyTxt(string filePath)
        {
            foreach (var rawLine in File.ReadLines(filePath))
            {
                // Strip a UTF-8 BOM so the first serial matches adb exactly.
                var line = rawLine.TrimStart('\uFEFF').Trim();
                if (line.Length == 0)
                    continue;

                var data = line.Split(' ');
                if (data.Length == 2 && data[0].Length > 0 && data[1].Length > 0)
                    yield return (data[0], data[1]);
            }
        }

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private void MigrateFromLegacyTxt(IEnumerable<string> candidatePaths)
        {
            foreach (var path in candidatePaths)
            {
                if (!File.Exists(path))
                    continue;

                try
                {
                    int imported = ImportFromTextFile(path);
                    UiDiagnostics.Log($"DeviceDatabase: migrated {imported} device(s) from legacy '{path}' to '{DatabasePath}'");
                }
                catch (Exception ex)
                {
                    // A broken legacy file must not prevent the app from starting.
                    UiDiagnostics.Log($"DeviceDatabase: legacy import from '{path}' failed: {ex.Message}");
                }
                return;
            }

            UiDiagnostics.Log($"DeviceDatabase: no legacy Devices.txt found, starting with an empty database at '{DatabasePath}'");
        }
    }
}

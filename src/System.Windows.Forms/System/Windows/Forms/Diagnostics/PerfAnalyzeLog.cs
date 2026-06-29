// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PERFANALYZE

using Microsoft.Data.Sqlite;

namespace System.Windows.Forms.Diagnostics;

/// <summary>
///  Opt-in measurement tracer. Compiled only when the <c>PERFANALYZE</c> constant is defined
///  (build with <c>/p:PerfAnalyze=true</c>). Records every <c>Control.GetPreferredSize</c>
///  call into a daily SQLite database under <c>%AppData%\.WinFormsRuntime</c>. Rows are buffered and
///  flushed on a background thread so the layout hot path is not blocked by disk or SQLite work.
/// </summary>
internal static class PerfAnalyzeLog
{
    private readonly record struct Entry(
        long TimestampTicks,
        string TypeName,
        string Name,
        string ParentName,
        string Text,
        int ConstraintWidth,
        int ConstraintHeight,
        int ResultWidth,
        int ResultHeight,
        string AssemblyName);

    private const int FlushThreshold = 256;

    private static readonly Collections.Concurrent.ConcurrentQueue<Entry> s_queue = new();
    private static readonly Threading.ManualResetEventSlim s_signal = new(false);
    private static readonly Threading.Lock s_initLock = new();
    private static readonly string s_assemblyName =
        Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "<unknown>";

    private static volatile bool s_started;
    private static string? s_currentDay;
    private static SqliteConnection? s_connection;

    /// <summary>
    ///  Records a single measure call. Cheap and lock-free on the caller's thread.
    /// </summary>
    public static void Record(Control control, Drawing.Size constraint, Drawing.Size result)
    {
        EnsureStarted();

        s_queue.Enqueue(new Entry(
            TimestampTicks: DateTime.UtcNow.Ticks,
            TypeName: control.GetType().FullName ?? control.GetType().Name,
            Name: control.Name ?? string.Empty,
            ParentName: control.Parent?.Name ?? string.Empty,
            Text: control.Text ?? string.Empty,
            ConstraintWidth: constraint.Width,
            ConstraintHeight: constraint.Height,
            ResultWidth: result.Width,
            ResultHeight: result.Height,
            AssemblyName: s_assemblyName));

        if (s_queue.Count >= FlushThreshold)
        {
            s_signal.Set();
        }
    }

    private static void EnsureStarted()
    {
        if (s_started)
        {
            return;
        }

        lock (s_initLock)
        {
            if (s_started)
            {
                return;
            }

            Threading.Thread worker = new(WriterLoop)
            {
                IsBackground = true,
                Name = "WinForms PerfAnalyze writer"
            };

            worker.Start();
            s_started = true;
        }
    }

    private static void WriterLoop()
    {
        while (true)
        {
            s_signal.Wait(TimeSpan.FromSeconds(2));
            s_signal.Reset();

            try
            {
                Flush();
            }
            catch
            {
                // Diagnostics must never destabilize the app; drop and continue.
            }
        }
    }

    private static void Flush()
    {
        if (s_queue.IsEmpty)
        {
            return;
        }

        SqliteConnection connection = GetConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO Measurements " +
            "(Timestamp, TypeName, Name, ParentName, Text, ConstraintW, ConstraintH, ResultW, ResultH, AssemblyName) " +
            "VALUES ($ts, $type, $name, $parent, $text, $cw, $ch, $rw, $rh, $asm)";

        SqliteParameter ts = command.Parameters.Add("$ts", SqliteType.Text);
        SqliteParameter type = command.Parameters.Add("$type", SqliteType.Text);
        SqliteParameter name = command.Parameters.Add("$name", SqliteType.Text);
        SqliteParameter parent = command.Parameters.Add("$parent", SqliteType.Text);
        SqliteParameter text = command.Parameters.Add("$text", SqliteType.Text);
        SqliteParameter cw = command.Parameters.Add("$cw", SqliteType.Integer);
        SqliteParameter ch = command.Parameters.Add("$ch", SqliteType.Integer);
        SqliteParameter rw = command.Parameters.Add("$rw", SqliteType.Integer);
        SqliteParameter rh = command.Parameters.Add("$rh", SqliteType.Integer);
        SqliteParameter asm = command.Parameters.Add("$asm", SqliteType.Text);

        while (s_queue.TryDequeue(out Entry entry))
        {
            // Microsecond-resolution ISO-8601 timestamp.
            ts.Value = new DateTime(entry.TimestampTicks, DateTimeKind.Utc)
                .ToString("yyyy-MM-ddTHH:mm:ss.ffffff", Globalization.CultureInfo.InvariantCulture);
            type.Value = entry.TypeName;
            name.Value = entry.Name;
            parent.Value = entry.ParentName;
            text.Value = entry.Text;
            cw.Value = entry.ConstraintWidth;
            ch.Value = entry.ConstraintHeight;
            rw.Value = entry.ResultWidth;
            rh.Value = entry.ResultHeight;
            asm.Value = entry.AssemblyName;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static SqliteConnection GetConnection()
    {
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture);
        if (s_connection is not null && s_currentDay == today)
        {
            return s_connection;
        }

        // Day rolled over (or first use): open a fresh per-day database.
        s_connection?.Dispose();

        string folder = IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".WinFormsRuntime");
        IO.Directory.CreateDirectory(folder);

        string path = IO.Path.Combine(folder, $"perfanalyze-{today}.db");
        SqliteConnection connection = new($"Data Source={path}");
        connection.Open();

        using (SqliteCommand setup = connection.CreateCommand())
        {
            setup.CommandText =
                "PRAGMA journal_mode=WAL;" +
                "CREATE TABLE IF NOT EXISTS Measurements (" +
                "Id INTEGER PRIMARY KEY AUTOINCREMENT, Timestamp TEXT, TypeName TEXT, Name TEXT, " +
                "ParentName TEXT, Text TEXT, ConstraintW INTEGER, ConstraintH INTEGER, " +
                "ResultW INTEGER, ResultH INTEGER, AssemblyName TEXT);";
            setup.ExecuteNonQuery();
        }

        s_connection = connection;
        s_currentDay = today;
        return connection;
    }
}

#endif

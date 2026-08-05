using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Pamac
{
    /// <summary>
    /// Watches for package-manager lock file changes and runs checkupdates
    /// (equivalent of updates_checker.vala).
    /// </summary>
    public class UpdatesChecker
    {
        private Config _config = null!;
        private string _lockPath = "";
        private System.Threading.Timer? _lockTimer;
        private ushort _updatesNb;
        private string[] _updatesList = Array.Empty<string>();
        private readonly FileSystemWatcher _watcher = new();

        public ushort UpdatesNb => _updatesNb;
        public string[] UpdatesList => _updatesList;
        public ulong RefreshPeriod => _config.RefreshPeriod;
        public bool NoUpdateHideIcon => _config.NoUpdateHideIcon;

        public event Action<ushort>? UpdatesAvailable;

        public UpdatesChecker()
        {
            _config = new Config("/etc/pamac.conf");
            _lockPath = Path.Combine(_config.DbPath, "db.lck");
            try
            {
                _watcher.Path = Path.GetDirectoryName(_lockPath)!;
                _watcher.Filter = Path.GetFileName(_lockPath);
                _watcher.Created += OnLockChanged;
                _watcher.Deleted += OnLockChanged;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
            }
        }

        public void CheckUpdates()
        {
            _config.Reload();
            if (_config.RefreshPeriod != 0)
            {
                // get updates via the checkupdates tool
                try
                {
                    var psi = new ProcessStartInfo("pamac-checkupdates")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                    };
                    using var process = Process.Start(psi)!;
                    var lines = new List<string>();
                    string? line;
                    while ((line = process.StandardOutput.ReadLine()) != null)
                    {
                        lines.Add(line);
                    }
                    process.WaitForExit();
                    int status = process.ExitCode;
                    _updatesNb = 0;
                    _updatesList = Array.Empty<string>();
                    if (status == 100)
                    {
                        _updatesNb = (ushort)lines.Count;
                        _updatesList = lines.ToArray();
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.Message);
                }
                UpdatesAvailable?.Invoke(_updatesNb);
            }
        }

        private void OnLockChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Created)
            {
                _lockTimer?.Dispose();
            }
            else if (e.ChangeType == WatcherChangeTypes.Deleted)
            {
                _lockTimer?.Dispose();
                _lockTimer = new System.Threading.Timer(_ => { CheckUpdates(); }, null, 5000, System.Threading.Timeout.Infinite);
            }
        }
    }
}

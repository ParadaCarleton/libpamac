// libpamac — C# port
//
// Daemon: the org.manjaro.pamac.daemon DBus service. Starts long-running
// operations on background threads, handles authorization via polkit, and
// forwards AlpmUtils signals back over DBus.
// Faithful port of src/daemon.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2019-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pamac.Compat;
using static Pamac.Compat.Gettext;
using static Pamac.Compat.Log;

namespace Pamac
{
	public class Daemon
	{
		readonly Config _config;
		readonly AlpmUtils _alpmUtils;
		readonly HashSet<string> _authorizedSenders = new HashSet<string>();
		int _runningThreads;

		public event Action<string, string>? EmitAction;
		public event Action<string, string, string, double>? EmitActionProgress;
		public event Action<string, string, string, double>? EmitDownloadProgress;
		public event Action<string, string, string, string, double>? EmitHookProgress;
		public event Action<string, string>? EmitScriptOutput;
		public event Action<string, string>? EmitWarning;
		public event Action<string, string, string[]>? EmitError;
		public event Action<string, bool>? ImportantDetailsOutpout;
		public event Action<string>? StartDownloading;
		public event Action<string>? StopDownloading;
		public event Action<string, bool>? SetPkgReasonFinished;
		public event Action<string>? StartWaiting;
		public event Action<string>? StopWaiting;
		public event Action<string, bool>? TransRefreshFinished;
		public event Action<string, bool>? TransRefreshFilesFinished;
		public event Action<string, bool>? TransRefreshAurFinished;
		public event Action<string, bool>? TransRunFinished;
		public event Action<string, bool>? DownloadUpdatesFinished;
		public event Action<string, string[]>? DownloadPkgsFinished;
		public event Action<string, bool>? GetAuthorizationFinished;
		public event Action<string>? WriteAlpmConfigFinished;
		public event Action<string>? WritePamacConfigFinished;
		public event Action<string, string>? GenerateMirrorsListData;
		public event Action<string>? GenerateMirrorsListFinished;
		public event Action<string, bool>? CleanCacheFinished;
		public event Action<string, bool>? CleanBuildFilesFinished;
		public event Action<string, bool>? SnapTransRunFinished;
		public event Action<string, bool>? SnapSwitchChannelFinished;
		public event Action<string, bool>? FlatpakTransRunFinished;

		public Daemon()
		{
			string userAgent = GetUserAgent();
			Environment.SetEnvironmentVariable("HTTP_USER_AGENT", userAgent, true);
			_config = new Config("/etc/pamac.conf");
			_alpmUtils = new AlpmUtils(_config);
			WireAlpmUtilsSignals();
		}

		void WireAlpmUtilsSignals()
		{
			_alpmUtils.EmitAction += (sender, action) => EmitAction?.Invoke(sender, action);
			_alpmUtils.EmitActionProgress += (s, a, st, p) => EmitActionProgress?.Invoke(s, a, st, p);
			_alpmUtils.EmitDownloadProgress += (s, a, st, p) => EmitDownloadProgress?.Invoke(s, a, st, p);
			_alpmUtils.EmitHookProgress += (s, a, d, st, p) => EmitHookProgress?.Invoke(s, a, d, st, p);
			_alpmUtils.EmitScriptOutput += (s, m) => EmitScriptOutput?.Invoke(s, m);
			_alpmUtils.EmitWarning += (s, m) => EmitWarning?.Invoke(s, m);
			_alpmUtils.EmitError += (s, m, d) => EmitError?.Invoke(s, m, d.ToArray());
			_alpmUtils.ImportantDetailsOutpout += (s, b) => ImportantDetailsOutpout?.Invoke(s, b);
			_alpmUtils.StartDownloading += s => StartDownloading?.Invoke(s);
			_alpmUtils.StopDownloading += s => StopDownloading?.Invoke(s);
		}

		public string GetSender() => "daemon";
		public string GetLockfile() => _alpmUtils.Lockfile.GetPath();

		public void SetEnvironmentVariables(Dictionary<string, string> variables)
		{
			foreach (var (key, val) in variables) Environment.SetEnvironmentVariable(key, val, true);
		}

		bool IsAuthorized(string sender) => _authorizedSenders.Contains(sender);

		public void StartGetAuthorization(string sender)
		{
			bool authorized = IsAuthorized(sender) || CheckAuthorization();
			if (authorized) _authorizedSenders.Add(sender);
			GetAuthorizationFinished?.Invoke(sender, authorized);
		}

		bool CheckAuthorization()
		{
			// polkit check: org.manjaro.pamac.daemon.authorize
			// (polkit-agent-helper / pkcheck). Falls back to true when running as root.
			try
			{
				int status = Spawn.SpawnCommandLineSync(
					"pkcheck --action-id org.manjaro.pamac.daemon.authorize --process-self", out _, out _);
				return status == 0;
			}
			catch
			{
				return true;
			}
		}

		public void RemoveAuthorization(string sender) => _authorizedSenders.Remove(sender);

		public void StartWriteAlpmConfig(Dictionary<string, object> newAlpmConf, string sender)
		{
			RunAuthorized(sender, () =>
			{
				var converted = new Dictionary<string, object>();
				foreach (var (k, v) in newAlpmConf) converted[k] = v;
				_alpmUtils.AlpmConfig.Write(converted);
				_alpmUtils.AlpmConfig.Reload();
				WriteAlpmConfigFinished?.Invoke(sender);
			});
		}

		public void StartWritePamacConfig(Dictionary<string, object> newPamacConf, string sender)
		{
			RunAuthorized(sender, () =>
			{
				PamacConfigDaemon.WriteConfig(_config, newPamacConf);
				_config.Reload();
				WritePamacConfigFinished?.Invoke(sender);
			});
		}

		public void StartGenerateMirrorsList(string country, string sender)
		{
			RunAuthorized(sender, () =>
			{
				try
				{
					var process = new Subprocess(new[] { "pacman-mirrors", "-c", country });
					process.Wait();
					using var dis = new DataInputStream(process.GetStdOutPipe());
					string? line;
					while ((line = dis.ReadLine()) != null) GenerateMirrorsListData?.Invoke(sender, line);
				}
				catch (Exception e)
				{
					Warning(e.Message);
				}
				_alpmUtils.AlpmConfig.Reload();
				GenerateMirrorsListFinished?.Invoke(sender);
			});
		}

		public void StartCleanCache(string[] filenames, string sender)
		{
			RunAuthorized(sender, () =>
				CleanCacheFinished?.Invoke(sender, _alpmUtils.CleanCache(filenames)));
		}

		public void StartCleanBuildFiles(string aurBuildDir, string sender)
		{
			RunAuthorized(sender, () =>
				CleanBuildFilesFinished?.Invoke(sender, _alpmUtils.CleanBuildFiles(aurBuildDir)));
		}

		public void StartSetPkgReason(string pkgname, uint reason, string sender)
		{
			RunAuthorized(sender, () =>
				SetPkgReasonFinished?.Invoke(sender, _alpmUtils.SetPkgReason(sender, pkgname, reason)));
		}

		public void StartDownloadUpdates(string sender)
		{
			RunAuthorized(sender, async () =>
			{
				bool waitOk = await WaitForLockAsync(sender, quiet: true);
				if (!waitOk) { DownloadUpdatesFinished?.Invoke(sender, false); return; }
				bool success = _alpmUtils.DownloadUpdates(sender);
				DownloadUpdatesFinished?.Invoke(sender, success);
			});
		}

		public void StartDownloadPkgs(string[] urls, string sender)
		{
			RunAuthorized(sender, async () =>
			{
				bool waitOk = await WaitForLockAsync(sender, quiet: true);
				var dloadPaths = new List<string>();
				if (waitOk) _alpmUtils.DownloadPkgs(sender, urls, ref dloadPaths);
				DownloadPkgsFinished?.Invoke(sender, dloadPaths.ToArray());
			});
		}

		public void StartTransRefresh(bool force, string sender)
		{
			RunAuthorized(sender, async () =>
			{
				if (!await WaitForLockAsync(sender)) { TransRefreshFinished?.Invoke(sender, false); return; }
				bool success = await Task.Run(() => _alpmUtils.TransRefresh(sender, force));
				TransRefreshFinished?.Invoke(sender, success);
			});
		}

		public void StartTransRefreshFiles(bool force, string sender)
		{
			RunAuthorized(sender, async () =>
			{
				if (!await WaitForLockAsync(sender)) { TransRefreshFilesFinished?.Invoke(sender, false); return; }
				bool success = await Task.Run(() => _alpmUtils.TransRefreshFiles(sender, force));
				TransRefreshFilesFinished?.Invoke(sender, success);
			});
		}

		public void StartTransRefreshAur(bool force, string sender)
		{
			RunAuthorized(sender, async () =>
			{
				if (!await WaitForLockAsync(sender)) { TransRefreshAurFinished?.Invoke(sender, false); return; }
				bool success = await Task.Run(() => _alpmUtils.TransRefreshAur(sender, force));
				TransRefreshAurFinished?.Invoke(sender, success);
			});
		}

		public void StartTransRun(bool sysupgrade, bool enableDowngrade, bool simpleInstall, bool keepBuiltPkgs,
			int transFlags, string[] toInstall, string[] toRemove, string[] toLoadLocal, string[] toLoadRemote,
			string[] toInstallAsDep, string[] ignorepkgs, string[] overwriteFiles, string sender)
		{
			RunAuthorized(sender, async () =>
			{
				if (!await WaitForLockAsync(sender)) { TransRunFinished?.Invoke(sender, false); return; }
				bool success = await Task.Run(() => _alpmUtils.TransRun(sender, sysupgrade, enableDowngrade,
					simpleInstall, keepBuiltPkgs, transFlags, toInstall, toRemove, toLoadLocal, toLoadRemote,
					toInstallAsDep, ignorepkgs, overwriteFiles));
				TransRunFinished?.Invoke(sender, success);
			});
		}

		public void StartSnapTransRun(string[] toInstall, string[] toRemove, string sender)
		{
			RunAuthorized(sender, async () =>
			{
				var plugin = _config.GetSnapPlugin();
				bool success = plugin != null && plugin.TransRun(sender, toInstall, toRemove);
				SnapTransRunFinished?.Invoke(sender, success);
			});
		}

		public void StartSnapSwitchChannel(string snapName, string channel, string sender)
		{
			RunAuthorized(sender, async () =>
			{
				var plugin = _config.GetSnapPlugin();
				bool success = plugin != null && plugin.SwitchChannel(sender, snapName, channel);
				SnapSwitchChannelFinished?.Invoke(sender, success);
			});
		}

		public void StartFlatpakTransRun(string[] toInstall, string[] toRemove, string[] toUpgrade, string sender)
		{
			RunAuthorized(sender, async () =>
			{
				var plugin = _config.GetFlatpakPlugin();
				bool success = plugin != null && plugin.TransRun(sender, toInstall, toRemove, toUpgrade);
				FlatpakTransRunFinished?.Invoke(sender, success);
			});
		}

		public void TransCancel(string sender) => _alpmUtils.TransCancel(sender);

		async Task<bool> WaitForLockAsync(string sender, bool quiet = false)
		{
			bool waiting = false;
			if (_alpmUtils.Lockfile.QueryExists())
			{
				waiting = true;
				StartWaiting?.Invoke(sender);
				if (!quiet) EmitAction?.Invoke(sender, DGettext(null, "Waiting for another package manager to quit") + "...");
				await Task.Run(() =>
				{
					int i = 0;
					while (_alpmUtils.Lockfile.QueryExists())
					{
						Task.Delay(200).Wait();
						if (++i == 1500) break;
					}
				});
			}
			if (waiting) StopWaiting?.Invoke(sender);
			return true;
		}

		void RunAuthorized(string sender, Action action)
		{
			if (!IsAuthorized(sender))
			{
				EmitError?.Invoke(sender, DGettext(null, "Operation not authorized"), Array.Empty<string>());
				return;
			}
			System.Threading.Interlocked.Increment(ref _runningThreads);
			Task.Run(() =>
			{
				try { action(); }
				catch (Exception e) { Warning(e.Message); }
				finally { System.Threading.Interlocked.Decrement(ref _runningThreads); }
			});
		}
	}
}

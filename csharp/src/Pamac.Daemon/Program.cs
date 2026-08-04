// libpamac — C# port
//
// pamac-daemon entry point. Registers the org.manjaro.pamac.daemon service
// on the system bus and runs the main loop. In a real deployment the DBus
// registration is provided by the chosen DBus binding (e.g. NDesk.DBus /
// Tmds.DBus); the DaemonBridge.ProxyFactory below lets in-process clients
// (and the TransactionInterfaceDaemon) talk to the same instance.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2019-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Threading;
using System.Threading.Tasks;
using Pamac;

namespace Pamac
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			var daemon = new Daemon();

			// For the in-process C# port, wire the client-side proxy factory so
			// TransactionInterfaceDaemon / Config.Save can reach this instance.
			// A real deployment would instead export `daemon` on the system DBus.
			DaemonBridge.ProxyFactory = () => new LocalDaemonProxy(daemon);

			Console.WriteLine("pamac-daemon: running (org.manjaro.pamac.daemon)");
			// Block until SIGTERM.
			var exited = new ManualResetEventSlim(false);
			Console.CancelKeyPress += (_, e) => { e.Cancel = true; exited.Set(); };
			AppDomain.CurrentDomain.ProcessExit += (_, _) => exited.Set();
			exited.Wait();
			return 0;
		}
	}

	/// <summary>In-process IDaemon adapter over the server Daemon instance.</summary>
	internal sealed class LocalDaemonProxy : IDaemon
	{
		readonly Daemon _daemon;

		public LocalDaemonProxy(Daemon daemon)
		{
			_daemon = daemon;
			// forward server signals to the proxy's events
			_daemon.EmitAction += (s, a) => EmitAction?.Invoke(s, a);
			_daemon.EmitActionProgress += (s, a, st, p) => EmitActionProgress?.Invoke(s, a, st, p);
			_daemon.EmitDownloadProgress += (s, a, st, p) => EmitDownloadProgress?.Invoke(s, a, st, p);
			_daemon.EmitHookProgress += (s, a, d, st, p) => EmitHookProgress?.Invoke(s, a, d, st, p);
			_daemon.EmitScriptOutput += (s, m) => EmitScriptOutput?.Invoke(s, m);
			_daemon.EmitWarning += (s, m) => EmitWarning?.Invoke(s, m);
			_daemon.EmitError += (s, m, d) => EmitError?.Invoke(s, m, d);
			_daemon.ImportantDetailsOutpout += (s, b) => ImportantDetailsOutpout?.Invoke(s, b);
			_daemon.StartDownloading += s => StartDownloading?.Invoke(s);
			_daemon.StopDownloading += s => StopDownloading?.Invoke(s);
			_daemon.SetPkgReasonFinished += (s, ok) => SetPkgReasonFinished?.Invoke(s, ok);
			_daemon.StartWaiting += s => StartWaiting?.Invoke(s);
			_daemon.StopWaiting += s => StopWaiting?.Invoke(s);
			_daemon.DownloadPkgsFinished += (s, p) => DownloadPkgsFinished?.Invoke(s, p);
			_daemon.TransRefreshFinished += (s, ok) => TransRefreshFinished?.Invoke(s, ok);
			_daemon.TransRefreshFilesFinished += (s, ok) => TransRefreshFilesFinished?.Invoke(s, ok);
			_daemon.TransRefreshAurFinished += (s, ok) => TransRefreshAurFinished?.Invoke(s, ok);
			_daemon.TransRunFinished += (s, ok) => TransRunFinished?.Invoke(s, ok);
			_daemon.DownloadUpdatesFinished += (s, ok) => DownloadUpdatesFinished?.Invoke(s, ok);
			_daemon.GetAuthorizationFinished += (s, ok) => GetAuthorizationFinished?.Invoke(s, ok);
			_daemon.WriteAlpmConfigFinished += s => WriteAlpmConfigFinished?.Invoke(s);
			_daemon.WritePamacConfigFinished += s => WritePamacConfigFinished?.Invoke(s);
			_daemon.GenerateMirrorsListData += (s, l) => GenerateMirrorsListData?.Invoke(s, l);
			_daemon.GenerateMirrorsListFinished += s => GenerateMirrorsListFinished?.Invoke(s);
			_daemon.CleanCacheFinished += (s, ok) => CleanCacheFinished?.Invoke(s, ok);
			_daemon.CleanBuildFilesFinished += (s, ok) => CleanBuildFilesFinished?.Invoke(s, ok);
			_daemon.SnapTransRunFinished += (s, ok) => SnapTransRunFinished?.Invoke(s, ok);
			_daemon.SnapSwitchChannelFinished += (s, ok) => SnapSwitchChannelFinished?.Invoke(s, ok);
			_daemon.FlatpakTransRunFinished += (s, ok) => FlatpakTransRunFinished?.Invoke(s, ok);
		}

		public string GetSender() => _daemon.GetSender();
		public string GetLockfile() => _daemon.GetLockfile();
		public void SetEnvironmentVariables(Dictionary<string, string> variables) => _daemon.SetEnvironmentVariables(variables);
		public void StartGetAuthorization() => _daemon.StartGetAuthorization(GetSender());
		public void RemoveAuthorization() => _daemon.RemoveAuthorization(GetSender());
		public void StartWriteAlpmConfig(Dictionary<string, object> newAlpmConf) => _daemon.StartWriteAlpmConfig(newAlpmConf, GetSender());
		public void StartWritePamacConfig(Dictionary<string, object> newPamacConf) => _daemon.StartWritePamacConfig(newPamacConf, GetSender());
		public void StartGenerateMirrorsList(string country) => _daemon.StartGenerateMirrorsList(country, GetSender());
		public void StartCleanCache(string[] filenames) => _daemon.StartCleanCache(filenames, GetSender());
		public void StartCleanBuildFiles(string aurBuildDir) => _daemon.StartCleanBuildFiles(aurBuildDir, GetSender());
		public void StartSetPkgReason(string pkgname, uint reason) => _daemon.StartSetPkgReason(pkgname, reason, GetSender());
		public void StartDownloadUpdates() => _daemon.StartDownloadUpdates(GetSender());
		public void StartDownloadPkgs(string[] urls) => _daemon.StartDownloadPkgs(urls, GetSender());
		public void StartTransRefresh(bool force) => _daemon.StartTransRefresh(force, GetSender());
		public void StartTransRefreshFiles(bool force) => _daemon.StartTransRefreshFiles(force, GetSender());
		public void StartTransRefreshAur(bool force) => _daemon.StartTransRefreshAur(force, GetSender());
		public void StartTransRun(bool sysupgrade, bool enableDowngrade, bool simpleInstall, bool keepBuiltPkgs,
			int transFlags, string[] toInstall, string[] toRemove, string[] toLoadLocal, string[] toLoadRemote,
			string[] toInstallAsDep, string[] ignorepkgs, string[] overwriteFiles) =>
			_daemon.StartTransRun(sysupgrade, enableDowngrade, simpleInstall, keepBuiltPkgs, transFlags,
				toInstall, toRemove, toLoadLocal, toLoadRemote, toInstallAsDep, ignorepkgs, overwriteFiles, GetSender());
		public void TransCancel() => _daemon.TransCancel(GetSender());
		public void Quit() { }
		public void StartSnapTransRun(string[] toInstall, string[] toRemove) => _daemon.StartSnapTransRun(toInstall, toRemove, GetSender());
		public void StartSnapSwitchChannel(string snapName, string channel) => _daemon.StartSnapSwitchChannel(snapName, channel, GetSender());
		public void StartFlatpakTransRun(string[] toInstall, string[] toRemove, string[] toUpgrade) => _daemon.StartFlatpakTransRun(toInstall, toRemove, toUpgrade, GetSender());

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
		public event Action<string, string[]>? DownloadPkgsFinished;
		public event Action<string, bool>? TransRefreshFinished;
		public event Action<string, bool>? TransRefreshFilesFinished;
		public event Action<string, bool>? TransRefreshAurFinished;
		public event Action<string, bool>? TransRunFinished;
		public event Action<string, bool>? DownloadUpdatesFinished;
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
	}
}

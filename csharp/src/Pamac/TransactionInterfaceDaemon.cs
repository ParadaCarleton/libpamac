// libpamac — C# port
//
// TransactionInterfaceDaemon: the TransactionInterface implementation that
// talks to the org.manjaro.pamac.daemon system service over DBus.
// Faithful port of src/transaction_interface_daemon.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2019-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pamac.Compat;
using static Pamac.Compat.Log;

namespace Pamac
{
	internal sealed class TransactionInterfaceDaemon : ITransactionInterface
	{
		IDaemon? _systemDaemon;
		readonly Config _config;
		readonly string _sender;

		bool _getAuthorizationSuccess;
		bool _cleanCacheSuccess;
		bool _cleanBuildFilesSuccess;
		bool _setPkgReasonSuccess;
		bool _transRefreshSuccess;
		bool _transRefreshFilesSuccess;
		bool _transRefreshAurSuccess;
		bool _transRunSuccess;
		bool _downloadUpdatesSuccess;
		bool _snapTransRunSuccess;
		bool _snapSwitchChannelSuccess;
		bool _flatpakTransRunSuccess;
		string[] _downloadPaths = Array.Empty<string>();
		TaskCompletionSource<bool>? _getAuthorizationTcs;
		TaskCompletionSource<bool>? _cleanCacheTcs;
		TaskCompletionSource<bool>? _cleanBuildFilesTcs;
		TaskCompletionSource<bool>? _setPkgReasonTcs;
		TaskCompletionSource<bool>? _transRefreshTcs;
		TaskCompletionSource<bool>? _transRefreshFilesTcs;
		TaskCompletionSource<bool>? _transRefreshAurTcs;
		TaskCompletionSource<bool>? _transRunTcs;
		TaskCompletionSource<bool>? _downloadUpdatesTcs;
		TaskCompletionSource<string[]>? _downloadPkgsTcs;
		TaskCompletionSource<bool>? _snapTransRunTcs;
		TaskCompletionSource<bool>? _snapSwitchChannelTcs;
		TaskCompletionSource<bool>? _flatpakTransRunTcs;

		public event Action<string>? EmitAction;
		public event Action<string, string, double>? EmitActionProgress;
		public event Action? StartDownloading;
		public event Action? StopDownloading;
		public event Action? StartWaiting;
		public event Action? StopWaiting;
		public event Action<string, string, double>? EmitDownloadProgress;
		public event Action<string, string, string, double>? EmitHookProgress;
		public event Action<string>? EmitScriptOutput;
		public event Action<string>? EmitWarning;
		public event Action<string, List<string>>? EmitError;
		public event Action<bool>? ImportantDetailsOutpout;
		public event Action<string>? GenerateMirrorsListData;

		public TransactionInterfaceDaemon(Config config)
		{
			_config = config;
			ConnectSystemDaemon(config);
			// The in-process proxy uses the daemon's own sender name; a real DBus
			// deployment would use the client's unique bus name instead.
			_sender = _systemDaemon?.GetSender() ?? Guid.NewGuid().ToString("N");
			ConnectDbusSignals();
		}

		void ConnectSystemDaemon(Config config)
		{
			if (_systemDaemon == null)
			{
				try
				{
					_systemDaemon = DaemonBridge.GetProxy();
					_systemDaemon.SetEnvironmentVariables(
						new Dictionary<string, string>(config.EnvironmentVariables));
				}
				catch (Exception e)
				{
					Warning("connect system daemon error: {0}", e.Message);
				}
			}
		}

		void ConnectDbusSignals()
		{
			if (_systemDaemon == null) return;
			var d = _systemDaemon;
			d.EmitAction += OnEmitAction;
			d.EmitActionProgress += OnEmitActionProgress;
			d.EmitDownloadProgress += OnEmitDownloadProgress;
			d.EmitHookProgress += OnEmitHookProgress;
			d.EmitScriptOutput += OnEmitScriptOutput;
			d.EmitWarning += OnEmitWarning;
			d.EmitError += OnEmitError;
			d.ImportantDetailsOutpout += OnImportantDetailsOutpout;
			d.StartDownloading += OnStartDownloading;
			d.StopDownloading += OnStopDownloading;
			d.StartWaiting += OnStartWaiting;
			d.StopWaiting += OnStopWaiting;
			d.GetAuthorizationFinished += OnGetAuthorizationFinished;
			d.CleanCacheFinished += OnCleanCacheFinished;
			d.CleanBuildFilesFinished += OnCleanBuildFilesFinished;
			d.SetPkgReasonFinished += OnSetPkgReasonFinished;
			d.TransRefreshFinished += OnTransRefreshFinished;
			d.TransRefreshFilesFinished += OnTransRefreshFilesFinished;
			d.TransRefreshAurFinished += OnTransRefreshAurFinished;
			d.TransRunFinished += OnTransRunFinished;
			d.DownloadUpdatesFinished += OnDownloadUpdatesFinished;
			d.DownloadPkgsFinished += OnDownloadPkgsFinished;
			d.GenerateMirrorsListData += OnGenerateMirrorsListData;
			d.GenerateMirrorsListFinished += OnGenerateMirrorsListFinished;
			d.SnapTransRunFinished += OnSnapTransRunFinished;
			d.SnapSwitchChannelFinished += OnSnapSwitchChannelFinished;
			d.FlatpakTransRunFinished += OnFlatpakTransRunFinished;
		}

		bool IsOwn(string sender) => sender == _sender;

		void OnEmitAction(string sender, string action) { if (IsOwn(sender)) EmitAction?.Invoke(action); }
		void OnEmitActionProgress(string sender, string a, string s, double p) { if (IsOwn(sender)) EmitActionProgress?.Invoke(a, s, p); }
		void OnEmitDownloadProgress(string sender, string a, string s, double p) { if (IsOwn(sender)) EmitDownloadProgress?.Invoke(a, s, p); }
		void OnEmitHookProgress(string sender, string a, string d, string s, double p) { if (IsOwn(sender)) EmitHookProgress?.Invoke(a, d, s, p); }
		void OnEmitScriptOutput(string sender, string m) { if (IsOwn(sender)) EmitScriptOutput?.Invoke(m); }
		void OnEmitWarning(string sender, string m) { if (IsOwn(sender)) EmitWarning?.Invoke(m); }
		void OnEmitError(string sender, string m, string[] d)
		{
			if (!IsOwn(sender)) return;
			var arr = new List<string>(d);
			EmitError?.Invoke(m, arr);
		}
		void OnImportantDetailsOutpout(string sender, bool b) { if (IsOwn(sender)) ImportantDetailsOutpout?.Invoke(b); }
		void OnStartDownloading(string sender) { if (IsOwn(sender)) StartDownloading?.Invoke(); }
		void OnStopDownloading(string sender) { if (IsOwn(sender)) StopDownloading?.Invoke(); }
		void OnStartWaiting(string sender) { if (IsOwn(sender)) StartWaiting?.Invoke(); }
		void OnStopWaiting(string sender) { if (IsOwn(sender)) StopWaiting?.Invoke(); }

		void OnGetAuthorizationFinished(string sender, bool authorized)
		{
			if (IsOwn(sender)) { _getAuthorizationSuccess = authorized; _getAuthorizationTcs?.SetResult(true); }
		}
		void OnCleanCacheFinished(string sender, bool success)
		{
			if (IsOwn(sender)) { _cleanCacheSuccess = success; _cleanCacheTcs?.SetResult(true); }
		}
		void OnCleanBuildFilesFinished(string sender, bool success)
		{
			if (IsOwn(sender)) { _cleanBuildFilesSuccess = success; _cleanBuildFilesTcs?.SetResult(true); }
		}
		void OnSetPkgReasonFinished(string sender, bool success)
		{
			if (IsOwn(sender)) { _setPkgReasonSuccess = success; _setPkgReasonTcs?.SetResult(true); }
		}
		void OnTransRefreshFinished(string sender, bool success)
		{
			if (IsOwn(sender)) { _transRefreshSuccess = success; _transRefreshTcs?.SetResult(true); }
		}
		void OnTransRefreshFilesFinished(string sender, bool success)
		{
			if (IsOwn(sender)) { _transRefreshFilesSuccess = success; _transRefreshFilesTcs?.SetResult(true); }
		}
		void OnTransRefreshAurFinished(string sender, bool success)
		{
			if (IsOwn(sender)) { _transRefreshAurSuccess = success; _transRefreshAurTcs?.SetResult(true); }
		}
		void OnTransRunFinished(string sender, bool success)
		{
			if (IsOwn(sender)) { _transRunSuccess = success; _transRunTcs?.SetResult(true); }
		}
		void OnDownloadUpdatesFinished(string sender, bool success)
		{
			if (IsOwn(sender)) { _downloadUpdatesSuccess = success; _downloadUpdatesTcs?.SetResult(true); }
		}
		void OnDownloadPkgsFinished(string sender, string[] paths)
		{
			if (IsOwn(sender)) { _downloadPaths = paths; _downloadPkgsTcs?.SetResult(true); }
		}
		void OnGenerateMirrorsListData(string sender, string line) { if (IsOwn(sender)) GenerateMirrorsListData?.Invoke(line); }
		void OnGenerateMirrorsListFinished(string sender) { }
		void OnSnapTransRunFinished(string sender, bool success)
		{
			if (IsOwn(sender)) { _snapTransRunSuccess = success; _snapTransRunTcs?.SetResult(true); }
		}
		void OnSnapSwitchChannelFinished(string sender, bool success)
		{
			if (IsOwn(sender)) { _snapSwitchChannelSuccess = success; _snapSwitchChannelTcs?.SetResult(true); }
		}
		void OnFlatpakTransRunFinished(string sender, bool success)
		{
			if (IsOwn(sender)) { _flatpakTransRunSuccess = success; _flatpakTransRunTcs?.SetResult(true); }
		}

		public async Task<bool> GetAuthorization()
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_getAuthorizationTcs = tcs;
			d.StartGetAuthorization();
			await tcs.Task;
			return _getAuthorizationSuccess;
		}

		public void RemoveAuthorization()
		{
			RequireDaemon().RemoveAuthorization();
		}

		public async Task GenerateMirrorsList(string country)
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_generateMirrorsTcs = tcs;
			d.StartGenerateMirrorsList(country);
			await tcs.Task;
		}

		public async Task<bool> CleanCache(List<string> filenames)
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_cleanCacheTcs = tcs;
			d.StartCleanCache(filenames.ToArray());
			await tcs.Task;
			return _cleanCacheSuccess;
		}

		public async Task<bool> CleanBuildFiles(string aurBuildDir)
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_cleanBuildFilesTcs = tcs;
			d.StartCleanBuildFiles(aurBuildDir);
			await tcs.Task;
			return _cleanBuildFilesSuccess;
		}

		public async Task<bool> SetPkgReason(string pkgname, uint reason)
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_setPkgReasonTcs = tcs;
			d.StartSetPkgReason(pkgname, reason);
			await tcs.Task;
			return _setPkgReasonSuccess;
		}

		public async Task<bool> DownloadUpdates()
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_downloadUpdatesTcs = tcs;
			d.StartDownloadUpdates();
			await tcs.Task;
			return _downloadUpdatesSuccess;
		}

		public async Task<string[]> DownloadPkgs(List<string> urls)
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_downloadPkgsTcs = tcs;
			d.StartDownloadPkgs(urls.ToArray());
			await tcs.Task;
			return _downloadPaths;
		}

		public async Task<bool> TransRefresh(bool force)
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_transRefreshTcs = tcs;
			d.StartTransRefresh(force);
			await tcs.Task;
			return _transRefreshSuccess;
		}

		public async Task<bool> TransRefreshFiles(bool force)
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_transRefreshFilesTcs = tcs;
			d.StartTransRefreshFiles(force);
			await tcs.Task;
			return _transRefreshFilesSuccess;
		}

		public async Task<bool> TransRefreshAur(bool force)
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_transRefreshAurTcs = tcs;
			d.StartTransRefreshAur(force);
			await tcs.Task;
			return _transRefreshAurSuccess;
		}

		public async Task<bool> TransRun(bool sysupgrade, bool enableDowngrade, bool simpleInstall,
			bool keepBuiltPkgs, int transFlags, List<string> toInstall, List<string> toRemove,
			List<string> toLoadLocal, List<string> toLoadRemote, List<string> toInstallAsDep,
			List<string> ignorepkgs, List<string> overwriteFiles)
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_transRunTcs = tcs;
			d.StartTransRun(sysupgrade, enableDowngrade, simpleInstall, keepBuiltPkgs, transFlags,
				toInstall.ToArray(), toRemove.ToArray(), toLoadLocal.ToArray(), toLoadRemote.ToArray(),
				toInstallAsDep.ToArray(), ignorepkgs.ToArray(), overwriteFiles.ToArray());
			await tcs.Task;
			return _transRunSuccess;
		}

		public void TransCancel()
		{
			RequireDaemon().TransCancel();
		}

		public async Task<bool> SnapTransRun(List<string> toInstall, List<string> toRemove)
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_snapTransRunTcs = tcs;
			d.StartSnapTransRun(toInstall.ToArray(), toRemove.ToArray());
			await tcs.Task;
			return _snapTransRunSuccess;
		}

		public async Task<bool> SnapSwitchChannel(string snapName, string channel)
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_snapSwitchChannelTcs = tcs;
			d.StartSnapSwitchChannel(snapName, channel);
			await tcs.Task;
			return _snapSwitchChannelSuccess;
		}

		public async Task<bool> FlatpakTransRun(List<string> toInstall, List<string> toRemove, List<string> toUpgrade)
		{
			var d = RequireDaemon();
			var tcs = new TaskCompletionSource<bool>();
			_flatpakTransRunTcs = tcs;
			d.StartFlatpakTransRun(toInstall.ToArray(), toRemove.ToArray(), toUpgrade.ToArray());
			await tcs.Task;
			return _flatpakTransRunSuccess;
		}

		public void QuitDaemon()
		{
			try { _systemDaemon?.Quit(); }
			catch (Exception e) { Warning(e.Message); }
		}

		TaskCompletionSource<bool>? _generateMirrorsTcs;

		IDaemon RequireDaemon() =>
			_systemDaemon ?? throw new InvalidOperationException("not connected to dbus daemon");
	}

	/// <summary>Bridge that obtains the IDaemon DBus proxy (daemon project registers it).</summary>
	internal static class DaemonBridge
	{
		public static Func<IDaemon>? ProxyFactory { get; set; }

		public static IDaemon GetProxy() =>
			ProxyFactory?.Invoke() ?? throw new InvalidOperationException("no dbus daemon proxy available");
	}
}

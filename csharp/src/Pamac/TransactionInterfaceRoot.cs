// libpamac — C# port
//
// TransactionInterfaceRoot: the TransactionInterface implementation used when
// running as root — it drives AlpmUtils directly (no DBus).
// Faithful port of src/transaction_interface_root.vala.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2018-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pamac.Compat;
using static Pamac.Compat.Gettext;
using static Pamac.Compat.Log;

namespace Pamac
{
	internal sealed class TransactionInterfaceRoot : ITransactionInterface
	{
		readonly AlpmUtils _alpmUtils;
		readonly MainContext _context;
		readonly Cancellable _transCancellable = new Cancellable();
		bool _transRefreshSuccess;
		bool _transRefreshFilesSuccess;
		bool _transRefreshAurSuccess;
		bool _transRunSuccess;

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

		public TransactionInterfaceRoot(AlpmUtils alpmUtils, MainContext context)
		{
			_alpmUtils = alpmUtils;
			_context = context;
			WireAlpmUtilsSignals();
		}

		void WireAlpmUtilsSignals()
		{
			_alpmUtils.EmitAction += (_, action) => EmitAction?.Invoke(action);
			_alpmUtils.EmitActionProgress += (_, a, s, p) => EmitActionProgress?.Invoke(a, s, p);
			_alpmUtils.EmitDownloadProgress += (_, a, s, p) => EmitDownloadProgress?.Invoke(a, s, p);
			_alpmUtils.EmitHookProgress += (_, a, d, s, p) => EmitHookProgress?.Invoke(a, d, s, p);
			_alpmUtils.EmitScriptOutput += (_, m) => EmitScriptOutput?.Invoke(m);
			_alpmUtils.EmitWarning += (_, m) => EmitWarning?.Invoke(m);
			_alpmUtils.EmitError += (_, m, d) => EmitError?.Invoke(m, d);
			_alpmUtils.ImportantDetailsOutpout += (_, b) => ImportantDetailsOutpout?.Invoke(b);
			_alpmUtils.StartDownloading += _ => StartDownloading?.Invoke();
			_alpmUtils.StopDownloading += _ => StopDownloading?.Invoke();
		}

		public Task<bool> GetAuthorization() => Task.FromResult(true);

		public void RemoveAuthorization()
		{
			// we are root
		}

		public async Task GenerateMirrorsList(string country)
		{
			try
			{
				var process = new Subprocess(new[] { "pacman-mirrors", "-c", country });
				process.Wait();
				using var dis = new DataInputStream(process.GetStdOutPipe());
				string? line;
				while ((line = dis.ReadLine()) != null) GenerateMirrorsListData?.Invoke(line);
			}
			catch (Exception e)
			{
				Warning(e.Message);
			}
			_alpmUtils.AlpmConfig.Reload();
		}

		public Task<bool> CleanCache(List<string> filenames) =>
			Task.FromResult(_alpmUtils.CleanCache(filenames.ToArray()));

		public Task<bool> CleanBuildFiles(string aurBuildDir) =>
			Task.FromResult(_alpmUtils.CleanBuildFiles(aurBuildDir));

		public Task<bool> SetPkgReason(string pkgname, uint reason) =>
			Task.FromResult(_alpmUtils.SetPkgReason("root", pkgname, reason));

		public Task<bool> DownloadUpdates() =>
			Task.Run(() => _alpmUtils.DownloadUpdates("root"));

		public Task<string[]> DownloadPkgs(List<string> urls)
		{
			return Task.Run(() =>
			{
				var urlsCopy = new List<string>(urls);
				var dloadPaths = new List<string>();
				_alpmUtils.DownloadPkgs("root", urlsCopy.ToArray(), ref dloadPaths);
				return dloadPaths.ToArray();
			});
		}

		async Task<bool> WaitForLock()
		{
			bool waiting = false;
			bool success = false;
			_transCancellable.Reset();
			if (_alpmUtils.Lockfile.QueryExists())
			{
				waiting = true;
				StartWaiting?.Invoke();
				EmitAction?.Invoke(DGettext(null, "Waiting for another package manager to quit") + "...");
				await Task.Run(() =>
				{
					int i = 0;
					while (_alpmUtils.Lockfile.QueryExists() && !_transCancellable.IsCancelled)
					{
						Task.Delay(200).Wait();
						i++;
						if (i == 1500)
						{
							EmitAction?.Invoke(string.Format("{0}: {1}.",
								DGettext(null, "Transaction cancelled"), DGettext(null, "Timeout expired")));
							_transCancellable.Cancel();
							break;
						}
					}
				});
				success = true;
			}
			else success = true;
			if (waiting) StopWaiting?.Invoke();
			return success;
		}

		async Task<bool> TransRefreshReal(bool force)
		{
			if (!await WaitForLock())
			{
				_transRefreshSuccess = false;
				return false;
			}
			_transRefreshSuccess = await Task.Run(() => _alpmUtils.TransRefresh("root", force));
			return _transRefreshSuccess;
		}

		public async Task<bool> TransRefresh(bool force)
		{
			if (_alpmUtils.DownloadingUpdates)
			{
				_alpmUtils.Cancellable.Cancel();
				await Task.Delay(1000);
			}
			await TransRefreshReal(force);
			return _transRefreshSuccess;
		}

		async Task<bool> TransRefreshFilesReal(bool force)
		{
			if (!await WaitForLock())
			{
				_transRefreshFilesSuccess = false;
				return false;
			}
			_transRefreshFilesSuccess = await Task.Run(() => _alpmUtils.TransRefreshFiles("root", force));
			return _transRefreshFilesSuccess;
		}

		public async Task<bool> TransRefreshFiles(bool force)
		{
			if (_alpmUtils.DownloadingUpdates)
			{
				_alpmUtils.Cancellable.Cancel();
				await Task.Delay(1000);
			}
			await TransRefreshFilesReal(force);
			return _transRefreshFilesSuccess;
		}

		async Task<bool> TransRefreshAurReal(bool force)
		{
			if (!await WaitForLock())
			{
				_transRefreshAurSuccess = false;
				return false;
			}
			_transRefreshAurSuccess = await Task.Run(() => _alpmUtils.TransRefreshAur("root", force));
			return _transRefreshAurSuccess;
		}

		public async Task<bool> TransRefreshAur(bool force)
		{
			if (_alpmUtils.DownloadingUpdates)
			{
				_alpmUtils.Cancellable.Cancel();
				await Task.Delay(1000);
			}
			await TransRefreshAurReal(force);
			return _transRefreshAurSuccess;
		}

		async Task<bool> TransRunReal(bool sysupgrade, bool enableDowngrade, bool simpleInstall,
			bool keepBuiltPkgs, int transFlags, List<string> toInstall, List<string> toRemove,
			List<string> toLoadLocal, List<string> toLoadRemote, List<string> toInstallAsDep,
			List<string> ignorepkgs, List<string> overwriteFiles)
		{
			if (!await WaitForLock())
			{
				_transRunSuccess = false;
				return false;
			}
			_transRunSuccess = await Task.Run(() => _alpmUtils.TransRun("root", sysupgrade, enableDowngrade,
				simpleInstall, keepBuiltPkgs, transFlags, toInstall.ToArray(), toRemove.ToArray(),
				toLoadLocal.ToArray(), toLoadRemote.ToArray(), toInstallAsDep.ToArray(),
				ignorepkgs.ToArray(), overwriteFiles.ToArray()));
			return _transRunSuccess;
		}

		public async Task<bool> TransRun(bool sysupgrade, bool enableDowngrade, bool simpleInstall,
			bool keepBuiltPkgs, int transFlags, List<string> toInstall, List<string> toRemove,
			List<string> toLoadLocal, List<string> toLoadRemote, List<string> toInstallAsDep,
			List<string> ignorepkgs, List<string> overwriteFiles)
		{
			if (_alpmUtils.DownloadingUpdates)
			{
				_alpmUtils.Cancellable.Cancel();
				await Task.Delay(1000);
			}
			return await TransRunReal(sysupgrade, enableDowngrade, simpleInstall, keepBuiltPkgs, transFlags,
				toInstall, toRemove, toLoadLocal, toLoadRemote, toInstallAsDep, ignorepkgs, overwriteFiles);
		}

		public void TransCancel()
		{
			_transCancellable.Cancel();
			_alpmUtils.TransCancel("root");
		}

		public void QuitDaemon()
		{
			// we are the daemon
		}

		public Task<bool> SnapTransRun(List<string> toInstall, List<string> toRemove) => Task.FromResult(true);
		public Task<bool> SnapSwitchChannel(string snapName, string channel) => Task.FromResult(false);
		public Task<bool> FlatpakTransRun(List<string> toInstall, List<string> toRemove, List<string> toUpgrade) => Task.FromResult(true);
	}
}

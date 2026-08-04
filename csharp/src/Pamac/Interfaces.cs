// libpamac — C# port
//
// Plugin interfaces (AUR / Appstream / Snap / Flatpak), the abstract model
// classes they expose (AURInfos, App, SnapPackage, FlatpakPackage), and the
// TransactionInterface / Daemon interfaces.
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2019-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;

namespace Pamac
{
	// ------------------------------------------------------------------
	// AUR
	// ------------------------------------------------------------------
	internal interface IAurPlugin
	{
		void SetRealBuildDir(string configAurBuildDir);
		string GetRealBuildDir();
		AURInfos? GetInfos(string pkgname);
		List<AURInfos> GetMultiInfos(List<string> pkgnames);
		List<AURInfos> GetProviders(string depend);
		List<AURInfos> Search(string searchString);

		event Action<string, double>? EmitDownloadProgress;
		event Action<string>? EmitDownloadError;
	}

	internal abstract class AURInfos
	{
		public abstract string Name { get; }
		public abstract string Version { get; }
		public abstract string? Desc { get; }
		public abstract string? License { get; }
		public abstract string? Url { get; }
		public abstract List<string> Groups { get; }
		public abstract List<string> Depends { get; }
		public abstract List<string> OptDepends { get; }
		public abstract List<string> MakeDepends { get; }
		public abstract List<string> CheckDepends { get; }
		public abstract List<string> Provides { get; }
		public abstract List<string> Replaces { get; }
		public abstract List<string> Conflicts { get; }
		public abstract string? PackageBase { get; }
		public abstract string? Maintainer { get; }
		public abstract double Popularity { get; }
		public abstract DateTime? LastModified { get; }
		public abstract DateTime? OutOfDate { get; }
		public abstract DateTime? FirstSubmitted { get; }
		public abstract ulong NumVotes { get; }
	}

	// ------------------------------------------------------------------
	// Appstream
	// ------------------------------------------------------------------
	internal interface IAppstreamPlugin
	{
		void Load(List<string> reposNames);
		List<Dictionary<string, App>> GetApps();
		List<App> Search(string[] searchTokens);
		List<App> GetPkgnameApps(string pkgname);
		Dictionary<string, App> GetCategoryApps(string category);
	}

	internal abstract class App
	{
		public abstract string? Name { get; }
		public abstract string? Id { get; }
		public abstract string? Pkgname { get; }
		public abstract string? Desc { get; }
		public abstract string? LongDesc { get; }
		public abstract string? Repo { get; }
		public abstract string? Launchable { get; }
		public abstract string? Icon { get; }
		public abstract List<string> Screenshots { get; }
	}

	// ------------------------------------------------------------------
	// Snap
	// ------------------------------------------------------------------
	internal interface ISnapPlugin
	{
		event Action<string, string, string, double>? EmitActionProgress;
		event Action<string, string, string, double>? EmitDownloadProgress;
		event Action<string, string>? EmitScriptOutput;
		event Action<string, string, string[]>? EmitError;
		event Action<string>? StartDownloading;
		event Action<string>? StopDownloading;

		void SearchSnaps(string searchString, ref List<SnapPackage> pkgs);
		void SearchUninstalledSnapsSync(string searchString, ref List<SnapPackage> pkgs);
		bool IsInstalledSnap(string name);
		SnapPackage? GetSnap(string name);
		SnapPackage? GetSnapByAppId(string appId);
		void GetInstalledSnaps(ref List<SnapPackage> pkgs);
		string GetInstalledSnapIcon(string name);
		void GetCategorySnaps(string category, ref List<SnapPackage> pkgs);
		bool TransRun(string sender, string[] toInstall, string[] toRemove);
		bool SwitchChannel(string sender, string name, string channel);
		void TransCancel(string sender);
		void Refresh();
	}

	public abstract class SnapPackage : Package
	{
		public abstract string? Channel { get; }
		public abstract string? Publisher { get; }
		public abstract string? Confined { get; }
		public abstract List<string> Channels { get; }
	}

	// ------------------------------------------------------------------
	// Flatpak
	// ------------------------------------------------------------------
	internal interface IFlatpakPlugin
	{
		ulong RefreshPeriod { get; set; }

		event Action<string, string, string, double>? EmitActionProgress;
		event Action<string, string>? EmitScriptOutput;
		event Action<string, string, string[]>? EmitError;

		bool RefreshAppstreamData();
		void LoadAppstreamData();
		void GetRemotesNames(ref List<string> remotesNames);
		void SearchFlatpaks(string searchString, ref List<FlatpakPackage> pkgs);
		void SearchUninstalledFlatpaksSync(string[] searchTerms, ref List<FlatpakPackage> pkgs);
		bool IsInstalledFlatpak(string name);
		FlatpakPackage? GetFlatpakByAppId(string appId);
		FlatpakPackage? GetFlatpak(string id);
		void GetInstalledFlatpaks(ref List<FlatpakPackage> pkgs);
		void GetCategoryFlatpaks(string category, ref List<FlatpakPackage> pkgs);
		void GetFlatpakUpdates(ref List<FlatpakPackage> pkgs);
		bool TransRun(string sender, string[] toInstall, string[] toRemove, string[] toUpgrade);
		void TransCancel(string sender);
		void Refresh();
	}

	public abstract class FlatpakPackage : Package
	{
	}

	// ------------------------------------------------------------------
	// Transaction interface
	// ------------------------------------------------------------------
	internal interface ITransactionInterface
	{
		event Action<string>? EmitAction;
		event Action<string, string, double>? EmitActionProgress;
		event Action? StartDownloading;
		event Action? StopDownloading;
		event Action? StartWaiting;
		event Action? StopWaiting;
		event Action<string, string, double>? EmitDownloadProgress;
		event Action<string, string, string, double>? EmitHookProgress;
		event Action<string>? EmitScriptOutput;
		event Action<string>? EmitWarning;
		event Action<string, List<string>>? EmitError;
		event Action<bool>? ImportantDetailsOutpout;
		event Action<string>? GenerateMirrorsListData;

		System.Threading.Tasks.Task<bool> GetAuthorization();
		void RemoveAuthorization();
		System.Threading.Tasks.Task GenerateMirrorsList(string country);
		System.Threading.Tasks.Task<bool> CleanCache(List<string> filenames);
		System.Threading.Tasks.Task<bool> CleanBuildFiles(string aurBuildDir);
		System.Threading.Tasks.Task<bool> SetPkgReason(string pkgname, uint reason);
		System.Threading.Tasks.Task<bool> DownloadUpdates();
		System.Threading.Tasks.Task<string[]> DownloadPkgs(List<string> urls);
		System.Threading.Tasks.Task<bool> TransRefresh(bool force);
		System.Threading.Tasks.Task<bool> TransRefreshFiles(bool force);
		System.Threading.Tasks.Task<bool> TransRefreshAur(bool force);
		System.Threading.Tasks.Task<bool> TransRun(bool sysupgrade, bool enableDowngrade, bool simpleInstall,
			bool keepBuiltPkgs, int transFlags, List<string> toInstall, List<string> toRemove,
			List<string> toLoadLocal, List<string> toLoadRemote, List<string> toInstallAsDep,
			List<string> ignorepkgs, List<string> overwriteFiles);
		void TransCancel();
		void QuitDaemon();
		System.Threading.Tasks.Task<bool> SnapTransRun(List<string> toInstall, List<string> toRemove);
		System.Threading.Tasks.Task<bool> SnapSwitchChannel(string snapName, string channel);
		System.Threading.Tasks.Task<bool> FlatpakTransRun(List<string> toInstall, List<string> toRemove, List<string> toUpgrade);
	}

	// ------------------------------------------------------------------
	// Daemon (DBus org.manjaro.pamac.daemon)
	// ------------------------------------------------------------------
	[DBusInterface("org.manjaro.pamac.daemon")]
	internal interface IDaemon
	{
		string GetSender();
		string GetLockfile();
		void SetEnvironmentVariables(Dictionary<string, string> variables);
		void StartGetAuthorization();
		void RemoveAuthorization();
		void StartWriteAlpmConfig(Dictionary<string, object> newAlpmConf);
		void StartWritePamacConfig(Dictionary<string, object> newPamacConf);
		void StartGenerateMirrorsList(string country);
		void StartCleanCache(string[] filenames);
		void StartCleanBuildFiles(string aurBuildDir);
		void StartSetPkgReason(string pkgname, uint reason);
		void StartDownloadUpdates();
		void StartDownloadPkgs(string[] urls);
		void StartTransRefresh(bool force);
		void StartTransRefreshFiles(bool force);
		void StartTransRefreshAur(bool force);
		void StartTransRun(bool sysupgrade, bool enableDowngrade, bool simpleInstall, bool keepBuiltPkgs,
			int transFlags, string[] toInstall, string[] toRemove, string[] toLoadLocal, string[] toLoadRemote,
			string[] toInstallAsDep, string[] ignorepkgs, string[] overwriteFiles);
		void TransCancel();
		void Quit();
		void StartSnapTransRun(string[] toInstall, string[] toRemove);
		void StartSnapSwitchChannel(string snapName, string channel);
		void StartFlatpakTransRun(string[] toInstall, string[] toRemove, string[] toUpgrade);

		// signals
		event Action<string, string>? EmitAction;
		event Action<string, string, string, double>? EmitActionProgress;
		event Action<string, string, string, double>? EmitDownloadProgress;
		event Action<string, string, string, string, double>? EmitHookProgress;
		event Action<string, string>? EmitScriptOutput;
		event Action<string, string>? EmitWarning;
		event Action<string, string, string[]>? EmitError;
		event Action<string, bool>? ImportantDetailsOutpout;
		event Action<string>? StartDownloading;
		event Action<string>? StopDownloading;
		event Action<string, bool>? SetPkgReasonFinished;
		event Action<string>? StartWaiting;
		event Action<string>? StopWaiting;
		event Action<string, string[]>? DownloadPkgsFinished;
		event Action<string, bool>? TransRefreshFinished;
		event Action<string, bool>? TransRefreshFilesFinished;
		event Action<string, bool>? TransRefreshAurFinished;
		event Action<string, bool>? TransRunFinished;
		event Action<string, bool>? DownloadUpdatesFinished;
		event Action<string, bool>? GetAuthorizationFinished;
		event Action<string>? WriteAlpmConfigFinished;
		event Action<string>? WritePamacConfigFinished;
		event Action<string, string>? GenerateMirrorsListData;
		event Action<string>? GenerateMirrorsListFinished;
		event Action<string, bool>? CleanCacheFinished;
		event Action<string, bool>? CleanBuildFilesFinished;
		event Action<string, bool>? SnapTransRunFinished;
		event Action<string, bool>? SnapSwitchChannelFinished;
		event Action<string, bool>? FlatpakTransRunFinished;
	}

	[AttributeUsage(AttributeTargets.Interface)]
	internal sealed class DBusInterfaceAttribute : Attribute
	{
		public string Name { get; }
		public DBusInterfaceAttribute(string name) => Name = name;
	}
}

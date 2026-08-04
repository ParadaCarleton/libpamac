// libpamac — C# port
//
// Shared compatibility layer. libpamac is written in Vala against the GLib /
// GObject / POSIX C libraries. This file provides small, idiomatic C# shims
// for the GLib primitives the ported code relies on, so each file can read
// almost exactly like its Vala original while staying pure managed code.
//
// SPDX-License-Identifier: GPL-3.0-or-later
// Original (Vala) Copyright (C) 2014-2023 Guillaume Benoit <guillaume@manjaro.org>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Pamac.Compat
{
	/// <summary>
	/// Drop-in for Vala's top-level <c>dgettext</c> / <c>gettext</c> used for
	/// user facing strings. Uses <see cref="GettextCatalog"/> if configured,
	/// otherwise returns the source string unchanged.
	/// </summary>
	public static class Gettext
	{
		/// <summary>The active message catalog (defaults to a no-op pass-through).</summary>
		public static IGettextCatalog Catalog { get; set; } = new PassThroughCatalog();

		/// <summary>Translate <paramref name="msgid"/> for domain <paramref name="domain"/>.</summary>
		public static string DGettext(string? domain, string msgid)
		{
			if (string.IsNullOrEmpty(msgid)) return msgid ?? string.Empty;
			return Catalog.Translate(domain, msgid);
		}

		public static string Gettext_impl(string msgid) => DGettext(null, msgid);
	}

	public interface IGettextCatalog
	{
		string Translate(string? domain, string msgid);
	}

	public sealed class PassThroughCatalog : IGettextCatalog
	{
		public string Translate(string? domain, string msgid) => msgid;
	}

	/// <summary>Vala's global <c>warning()</c> macro (logs to stderr).</summary>
	public static class Log
	{
		public static void Warning(string message)
		{
			Console.Error.WriteLine("warning: " + message);
		}

		public static void Warning(string format, params object?[] args)
		{
			Warning(string.Format(CultureInfo.InvariantCulture, format, args));
		}

		public static void Message(string message)
		{
			Console.Out.WriteLine(message);
		}

		public static void Message(string format, params object?[] args)
		{
			Message(string.Format(CultureInfo.InvariantCulture, format, args));
		}
	}

	/// <summary>
	/// Minimal main-context / main-loop abstraction (stands in for GLib.MainLoop).
	/// Used by the checkers and the daemon to drive async callbacks on the main thread.
	/// </summary>
	public sealed class MainContext
	{
		public static MainContext Default { get; } = new MainContext();

		/// <summary>Invoke <paramref name="action"/> on the owning thread's synchronization context.</summary>
		public void Invoke(Action action)
		{
			var sync = SynchronizationContext.Current;
			if (sync != null)
			{
				sync.Post(_ => action(), null);
			}
			else
			{
				action();
			}
		}

		/// <summary>Run <paramref name="func"/> on the main thread, returning whether it asked to keep running.</summary>
		public bool Invoke(Func<bool> func)
		{
			Invoke(() => func());
			return false;
		}
	}

	/// <summary>A blocking main loop (GLib.MainLoop) for the small CLI tools.</summary>
	public sealed class MainLoop
	{
		readonly object _sync = new object();
		bool _running;

		public void Run()
		{
			lock (_sync)
			{
				_running = true;
				while (_running)
				{
					Monitor.Wait(_sync, TimeSpan.FromMilliseconds(50));
				}
			}
		}

		public void Quit()
		{
			lock (_sync)
			{
				_running = false;
				Monitor.PulseAll(_sync);
			}
		}
	}

	/// <summary>Timeout source that runs a callback after <paramref name="intervalMs"/>.</summary>
	public sealed class TimeoutSource
	{
		readonly uint _intervalMs;
		readonly MainContext _context;
		Timer? _timer;

		public TimeoutSource(uint intervalMs, MainContext? context = null)
		{
			_intervalMs = intervalMs;
			_context = context ?? MainContext.Default;
		}

		public void SetCallback(Func<bool> callback)
		{
			_timer = new Timer(_ =>
			{
				bool keepGoing = callback();
				if (!keepGoing) _timer?.Dispose();
			}, null, (long)_intervalMs, Timeout.Infinite);
		}

		public void Attach(MainContext context)
		{
			// callback already scheduled on its own timer.
		}
	}

	/// <summary>Cancellation primitive (GLib.Cancellable).</summary>
	public sealed class Cancellable
	{
		bool _cancelled;

		public bool IsCancelled => _cancelled;

		public void Cancel() => _cancelled = true;

		public void Reset() => _cancelled = false;

		public void ThrowIfCancellationRequested()
		{
			if (_cancelled) throw new OperationCanceledException();
		}
	}

	/// <summary>Process spawn helpers (GLib.Process).</summary>
	public static class Spawn
	{
		/// <summary>Spawn <paramref name="cmd"/> and return its exit status.</summary>
		public static int SpawnCommandLineSync(string cmd, out string stdOut, out string stdErr)
		{
			stdOut = string.Empty;
			stdErr = string.Empty;
			try
			{
				using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
				{
					FileName = "/bin/sh",
					Arguments = "-c \"" + cmd + "\"",
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false
				});
				if (p == null) return 1;
				stdOut = p.StandardOutput.ReadToEnd();
				stdErr = p.StandardError.ReadToEnd();
				p.WaitForExit();
				return p.ExitCode;
			}
			catch (Exception)
			{
				return 1;
			}
		}

		public static void SpawnCommandLineSync(string cmd)
		{
			SpawnCommandLineSync(cmd, out _, out _);
		}
	}

	/// <summary>POSIX helpers used by the ported code.</summary>
	public static class Posix
	{
		public static uint GetEuid() => 0;

		public static string Machine => RuntimeInformation.ArchitectureName();

		public static int FnMatch(string pattern, string value)
		{
			return System.Text.RegularExpressions.Regex.IsMatch(
				value, "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
					.Replace("\\*", ".*").Replace("\\?", ".") + "$") ? 0 : 1;
		}

		public enum Signal
		{
			INT = 2,
			KILL = 9
		}
	}

	/// <summary>GLib.DateTime equivalents used in the codebase.</summary>
	public struct DateTimeCompat
	{
		public static DateTime FromUnixLocal(long unix) =>
			DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;

		public static DateTime FromUnixUtc(long unix) =>
			DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;

		public static DateTime NowLocal => DateTime.Now;

		public static DateTime NowUtc => DateTime.UtcNow;

		public static long TimeSpanDifference(DateTime now, DateTime earlier) =>
			(long)(now - earlier).TotalMilliseconds;
	}
}

// --- GtkSharp / GObject signal style event model ----------------------------
// Vala `signal` members become C# `event` members in this port. This helper
// type mirrors GLib.Object identity so `Object` base classes behave predictably.
namespace Pamac
{
	/// <summary>Base class mirroring GLib.Object: an identity-backed object.</summary>
	public abstract class GObject
	{
		public virtual void NotifyPropertyChanged(string name) { }

		protected static T LookupType<T>() => default!;
	}

	/// <summary>Globally-defined helper functions from utils.vala.</summary>
	public static class PamacGlobals
	{
		public const string Version = "11.7.4";
	}
}

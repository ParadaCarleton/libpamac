// GLib.File / stream shims for the C# port.
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pamac.Compat
{
	/// <summary>Flags for <see cref="GFile.Create"/> / <see cref="GFile.Replace"/>.</summary>
	[Flags]
	public enum FileCreateFlags
	{
		None = 0,
		ReplaceDestination = 1
	}

	/// <summary>GLib.File enum value type.</summary>
	public enum FileType
	{
		Unknown,
		Regular,
		Directory,
		SymbolicLink,
		Special,
		Shortcut,
		Mountable
	}

	/// <summary>Thin managed wrapper around a filesystem path, mirroring GLib.File.</summary>
	public sealed class GFile : IEquatable<GFile>
	{
		public string Path { get; }

		public GFile(string path) => Path = path ?? throw new ArgumentNullException(nameof(path));

		public static GFile NewForPath(string path) => new GFile(path);

		public string GetPath() => Path;

		public string GetBasename() => System.IO.Path.GetFileName(Path.TrimEnd('/'));

		public string GetChildName(string child) => System.IO.Path.Combine(Path, child);

		public GFile GetChild(string child) => new GFile(GetChildName(child));

		public bool QueryExists() => File.Exists(Path) || Directory.Exists(Path);

		/// <summary>Open a read stream over the file (GLib.File.read).</summary>
		public Stream Read() => new FileStream(Path, FileMode.Open, FileAccess.Read);

		public FileStream Create(FileCreateFlags flags = FileCreateFlags.None) =>
			new FileStream(Path, FileMode.Create, FileAccess.Write);

		public FileStream Replace(string? etag, bool makeBackup, FileCreateFlags flags) =>
			new FileStream(Path, FileMode.Create, FileAccess.Write);

		public void Delete() => File.Delete(Path);

		public void MakeDirectoryWithParents() => Directory.CreateDirectory(Path);

		public long MeasureDiskUsage()
		{
			if (File.Exists(Path)) return new FileInfo(Path).Length;
			if (!Directory.Exists(Path)) return 0;
			long total = 0;
			foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
				total += new FileInfo(f).Length;
			return total;
		}

		public DateTime? GetModificationDateTime()
		{
			if (!QueryExists()) return null;
			return File.GetLastWriteTime(Path);
		}

		public IEnumerable<(string Name, long Size, FileType Type, DateTime Modified)> EnumerateChildren()
		{
			if (!Directory.Exists(Path)) yield break;
			foreach (var name in Directory.EnumerateFileSystemEntries(Path))
			{
				bool isDir = Directory.Exists(name);
				long size = isDir ? 0 : new FileInfo(name).Length;
				yield return (System.IO.Path.GetFileName(name), size,
					isDir ? FileType.Directory : FileType.Regular,
					isDir ? Directory.GetLastWriteTime(name) : File.GetLastWriteTime(name));
			}
		}

		public bool Equals(GFile? other) => other != null && string.Equals(Path, other.Path);

		public override bool Equals(object? obj) => Equals(obj as GFile);

		public override int GetHashCode() => Path.GetHashCode();
	}

	/// <summary>Reads lines from a stream (GLib.DataInputStream).</summary>
	public sealed class DataInputStream : IDisposable
	{
		readonly StreamReader _reader;

		public DataInputStream(Stream stream) => _reader = new StreamReader(stream, Encoding.UTF8);

		public string? ReadLine()
		{
			var sb = new StringBuilder();
			int c;
			while ((c = _reader.Read()) != -1)
			{
				if (c == '\n') break;
				sb.Append((char)c);
			}
			if (sb.Length == 0 && c == -1) return null;
			return sb.ToString();
		}

		public void Dispose() => _reader.Dispose();
	}

	/// <summary>Writes bytes to a stream (GLib.DataOutputStream).</summary>
	public sealed class DataOutputStream : IDisposable
	{
		readonly Stream _stream;

		public DataOutputStream(Stream stream) => _stream = stream;

		public void PutString(string text)
		{
			var bytes = Encoding.UTF8.GetBytes(text);
			_stream.Write(bytes, 0, bytes.Length);
			_stream.Flush();
		}

		public void CopyFrom(Stream source)
		{
			source.CopyTo(_stream);
			_stream.Flush();
		}

		public void Dispose() { }
	}

	/// <summary>Result of enumerating a directory's children (GLib.FileEnumerator).</summary>
	public sealed class FileEnumerator
	{
		readonly List<(string Name, long Size, FileType Type, DateTime Modified)> _entries;
		int _index;

		public FileEnumerator(GFile dir) => _entries = dir.EnumerateChildren().ToList();

		public (string Name, long Size, FileType Type, DateTime Modified)? NextFile()
		{
			if (_index >= _entries.Count) return null;
			return _entries[_index++];
		}
	}

	/// <summary>Subprocess helper (GLib.Subprocess).</summary>
	public sealed class Subprocess
	{
		readonly string[] _cmd;
		public string? StdOutCapture { get; private set; }
		public string? StdErrCapture { get; private set; }
		public int ExitStatus { get; private set; } = -1;
		public bool HasExited { get; private set; }

		public Subprocess(string[] cmd) => _cmd = cmd;

		public static Subprocess NewV(string[] cmd) => new Subprocess(cmd);

		public void Wait()
		{
			try
			{
				using var p = new System.Diagnostics.Process();
				p.StartInfo.FileName = _cmd[0];
				p.StartInfo.ArgumentList.Clear();
				for (int i = 1; i < _cmd.Length; i++) p.StartInfo.ArgumentList.Add(_cmd[i]);
				p.StartInfo.RedirectStandardOutput = true;
				p.StartInfo.RedirectStandardError = true;
				p.StartInfo.UseShellExecute = false;
				p.Start();
				StdOutCapture = p.StandardOutput.ReadToEnd();
				StdErrCapture = p.StandardError.ReadToEnd();
				p.WaitForExit();
				ExitStatus = p.ExitCode;
			}
			catch
			{
				ExitStatus = -1;
			}
			HasExited = true;
		}

		public Stream GetStdOutPipe() =>
			StdOutCapture == null
				? new MemoryStream()
				: new MemoryStream(Encoding.UTF8.GetBytes(StdOutCapture));
	}

	/// <summary>POSIX path helpers (GLib.Path).</summary>
	public static class PathCompat
	{
		public static string BuildFilename(params string[] parts)
		{
			var joined = string.Join(System.IO.Path.DirectorySeparatorChar,
				parts.Select(p => p.TrimEnd('/')).Where(p => p.Length > 0));
			return parts.Length > 0 && parts[0].StartsWith("/") ? "/" + joined : joined;
		}
	}
}

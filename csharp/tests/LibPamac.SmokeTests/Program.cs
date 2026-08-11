using Pamac;
using Pamac.LibAlpm;

// Self-contained smoke tests for the libpamac C# port.
// These deliberately avoid requiring libalpm, so they run on any host.
// A failing check increments the failure count and the process exits non-zero,
// which lets CI gate on the result without an external test framework.

int failures = 0;

void Check(string name, bool condition, string? detail = null)
{
    if (condition)
    {
        Console.WriteLine($"[PASS] {name}");
    }
    else
    {
        Console.WriteLine($"[FAIL] {name}  ({detail ?? "condition was false"})");
        failures++;
    }
}

Console.WriteLine($"LibPamac version: {Pamac.Version.GetVersion()}");

// ---- Version ----
Check("Version returns 11.7.4", Pamac.Version.GetVersion() == "11.7.4", Pamac.Version.GetVersion());

// ---- Utils ----
string ua = Utils.GetUserAgent();
Check("UserAgent starts with Pamac/", ua.StartsWith("Pamac/", StringComparison.Ordinal), ua);
Check("UserAgent is non-empty", ua.Length > 0, ua);

// ---- AlpmConfig parsing (no libalpm needed) ----
string confPath = Path.Combine(Path.GetTempPath(), "pamac-test-pacman.conf");
File.WriteAllText(confPath, """
# test pacman.conf
[options]
Architecture = x86_64
IgnorePkg = foo bar baz
CheckSpace
HoldPkg = pacman glibc
SyncFirst = archlinux-keyring

[core]
Server = https://mirror.example/$repo/os/$arch

[extra]
Server = https://mirror.example/$repo/os/$arch
""");
try
{
    var cfg = new AlpmConfig(confPath);
    Check("IgnorePkg parsed (3 entries)",
        cfg.Ignorepkgs.Contains("foo") && cfg.Ignorepkgs.Contains("bar") && cfg.Ignorepkgs.Contains("baz"),
        string.Join(",", cfg.Ignorepkgs));
    Check("CheckSpace parsed", cfg.Checkspace, "");
    Check("HoldPkg parsed (contains pacman)", cfg.Holdpkgs.Contains("pacman"), string.Join(",", cfg.Holdpkgs));
    Check("SyncFirst parsed (contains archlinux-keyring)", cfg.Syncfirsts.Contains("archlinux-keyring"), string.Join(",", cfg.Syncfirsts));
    Check("Default DBPath when unset", cfg.Dbpath == "/var/lib/pacman/", cfg.Dbpath);
}
finally
{
    File.Delete(confPath);
}

// ---- AlpmApi ----
// libalpm may or may not be present. We assert Version() never throws and returns
// either a real version or the graceful "unknown".
string alpmVersion = AlpmApi.Version();
Check("AlpmApi.Version() returns a string", alpmVersion != null, alpmVersion);

// ---- Compare: vercmp logic doesn't need libalpm only if lib present; guard skip ----
// (AlpmPackage.VerCmp forwards to libalpm; only exercise when available.)

Console.WriteLine(failures == 0 ? "\nAll smoke tests passed." : $"\n{failures} smoke test(s) FAILED.");
return failures == 0 ? 0 : 1;

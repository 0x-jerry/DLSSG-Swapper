using DlssgSwapper.Core.Diagnostics;

namespace DlssgSwapper.Core.Tests;

public class LogInspectorTests
{
    private const string Exe = "FakeGame-Win64-Shipping.exe";

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // 0.4.1 writes one line per record: "<local time>  pid <n>  <exe>  <level letter>  {json}".
    private static string Line(string json, string exe = Exe, int pid = 4242, char level = 'I') =>
        $"2026-10-27 14:03:11.482  pid {pid}  {exe}  {level}  {json}";

    private static void WriteLog(string gameDir, params string[] lines)
    {
        string dir = Path.Combine(gameDir, "dlssg_sm86", "logs");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, LogInspector.LogFileName), string.Join("\n", lines) + "\n");
    }

    [Fact]
    public void Check_FindsActivationMarkers()
    {
        string dir = TempDir();
        try
        {
            WriteLog(dir,
                Line("""{"event":"log_session","exe":"FakeGame-Win64-Shipping.exe","pid":4242,"version":"0.4.1"}"""),
                Line("""{"event":"configuration","ini":"C:\\game\\dlssg_sm86.ini","runtime_mode":"Bundled","requested_max":5,"proxies":{"active":"version.dll","standby":[]}}"""),
                Line("""{"event":"runtime_redirect","mode":"bundled","selected_source":"bundled"}"""),
                Line("""{"event":"backend_install","status":0}"""),
                Line("""{"event":"install","active":true,"image":"ptx_sm86","max_generated_frames":5}"""),
                Line("""{"event":"mfg_capability","image":"ptx_sm86","advertised_max":5}"""),
                Line("""{"event":"routed","active":true}"""));

            var result = LogInspector.Check(dir, Exe);
            Assert.True(result.LogsFound);
            Assert.True(result.InstallActive);
            Assert.Equal(0, result.BackendInstallStatus);
            Assert.Equal("ptx_sm86", result.Image);
            Assert.Equal("version.dll", result.Proxy);
            Assert.True(result.Verified);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Check_IgnoresAnotherGamesLines()
    {
        string dir = TempDir();
        try
        {
            WriteLog(dir,
                Line("""{"event":"runtime_redirect"}""", exe: "OtherGame.exe"),
                Line("""{"event":"backend_install","status":0}""", exe: "OtherGame.exe"),
                Line("""{"event":"install","active":true}""", exe: "OtherGame.exe"),
                Line("""{"event":"routed"}""", exe: "OtherGame.exe"));

            var result = LogInspector.Check(dir, Exe);
            Assert.True(result.LogsFound);
            Assert.Null(result.InstallActive);
            Assert.False(result.Verified);
            Assert.Contains(result.Messages, m => m.Contains("may not have been loaded"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Check_ResetsAtANewSession()
    {
        string dir = TempDir();
        try
        {
            // The first run reached the route; the game was restarted and the new run has not yet.
            WriteLog(dir,
                Line("""{"event":"log_session","pid":1}"""),
                Line("""{"event":"runtime_redirect"}"""),
                Line("""{"event":"backend_install","status":0}"""),
                Line("""{"event":"install","active":true}"""),
                Line("""{"event":"routed"}"""),
                Line("""{"event":"log_session","pid":2}"""),
                Line("""{"event":"configuration","proxies":{"active":"version.dll"}}"""));

            var result = LogInspector.Check(dir, Exe);
            Assert.Null(result.InstallActive);
            Assert.False(result.Verified);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Check_ReportsInactiveRoute()
    {
        string dir = TempDir();
        try
        {
            WriteLog(dir,
                Line("""{"event":"runtime_redirect"}"""),
                Line("""{"event":"backend_install","status":0}"""),
                Line("""{"event":"install","active":false,"image":"original"}"""));

            var result = LogInspector.Check(dir, Exe);
            Assert.False(result.Verified);
            Assert.False(result.InstallActive);
            Assert.Contains(result.Messages, m => m.Contains("install.active=false"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Check_ReportsFailedBackendInstall()
    {
        string dir = TempDir();
        try
        {
            WriteLog(dir,
                Line("""{"event":"runtime_redirect"}"""),
                Line("""{"event":"backend_install","status":5}"""),
                Line("""{"event":"install","active":true}"""),
                Line("""{"event":"install_failed","message":"bundled backend install failed"}""", level: 'E'));

            var result = LogInspector.Check(dir, Exe);
            Assert.False(result.Verified);
            Assert.Contains(result.Messages, m => m.Contains("status=5"));
            Assert.Contains(result.Messages, m => m.Contains("bundled backend install failed"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Check_ReportsAnInvalidIniValue()
    {
        string dir = TempDir();
        try
        {
            // A wrong value no longer falls back to a default: the feature it belongs to is off and
            // this record is the only place the reason shows up.
            WriteLog(dir,
                Line("""{"event":"configuration_invalid","section":"DLSSG","key":"Preset","value":"Auto","allowed":"A/B/C","effect":"frame_generation_not_enabled"}""", level: 'E'));

            var result = LogInspector.Check(dir, Exe);
            Assert.Contains(result.Messages, m =>
                m.Contains("DLSSG") && m.Contains("Preset") && m.Contains("A/B/C"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Check_LoaderOnlyLog_ReportsMissingBackendMarkers()
    {
        string dir = TempDir();
        try
        {
            WriteLog(dir,
                Line("""{"event":"configuration_error","message":"Preset must be A, B or C"}""", level: 'E'));

            var result = LogInspector.Check(dir, Exe);
            Assert.True(result.LogsFound);
            Assert.Null(result.InstallActive);
            Assert.False(result.Verified);
            Assert.Contains(result.Messages, m => m.Contains("No runtime_redirect"));
            Assert.Contains(result.Messages, m => m.Contains("Preset must be A, B or C"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Check_SkipsAPartiallyWrittenLastLine()
    {
        string dir = TempDir();
        try
        {
            WriteLog(dir,
                Line("""{"event":"runtime_redirect"}"""),
                Line("""{"event":"backend_install","status":0}"""),
                Line("""{"event":"install","active":true}"""),
                Line("""{"event":"routed"}"""),
                Line("{\"event\":\"install\",\"active\":f")); // cut off mid-record

            var result = LogInspector.Check(dir, Exe);
            Assert.True(result.Verified);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Check_NoLogDir_ReportsNotFound()
    {
        string dir = TempDir();
        try
        {
            var result = LogInspector.Check(dir, Exe);
            Assert.False(result.LogsFound);
            Assert.False(result.Verified);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

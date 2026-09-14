using DlssgSwapper.Core.Diagnostics;

namespace DlssgSwapper.Core.Tests;

public class LogInspectorTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteLog(string gameDir, string name, string content)
    {
        Directory.CreateDirectory(Path.Combine(gameDir, "dlssg_sm86", "logs"));
        File.WriteAllText(Path.Combine(gameDir, "dlssg_sm86", "logs", name), content);
    }

    [Fact]
    public void Check_FindsActivationMarkers()
    {
        string dir = TempDir();
        try
        {
            WriteLog(dir, "loader_1.jsonl", """
                {"event":"configuration","ini":"C:\\game\\dlssg_sm86.ini","runtime_mode":"Bundled","requested_max":5}
                {"event":"runtime_redirect","mode":"bundled","selected":"game_runtime_used"}
                {"event":"backend_install","status":0}
                """);
            WriteLog(dir, "backend_2.jsonl", """
                {"event":"install","active":true,"image":"ptx_sm86","max_generated_frames":5}
                {"event":"mfg_capability","image":"ptx_sm86","advertised_max":5}
                {"event":"routed","active":true}
                """);

            var result = LogInspector.Check(dir);
            Assert.True(result.LogsFound);
            Assert.True(result.InstallActive);
            Assert.Equal(0, result.BackendInstallStatus);
            Assert.Equal("ptx_sm86", result.Image);
            Assert.True(result.Verified);
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
            WriteLog(dir, "loader_9.jsonl", """
                {"event":"runtime_redirect"}
                {"event":"backend_install","status":0}
                """);
            WriteLog(dir, "backend_9.jsonl", """
                {"event":"install","active":false,"image":"original"}
                """);

            var result = LogInspector.Check(dir);
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
            WriteLog(dir, "loader_3.jsonl", """
                {"event":"runtime_redirect"}
                {"event":"backend_install","status":5}
                """);
            WriteLog(dir, "backend_3.jsonl", """
                {"event":"install","active":true}
                {"event":"install_failed","message":"bundled backend install failed"}
                """);

            var result = LogInspector.Check(dir);
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
    public void Check_LoaderOnlyLog_ReportsMissingBackendMarkers()
    {
        string dir = TempDir();
        try
        {
            WriteLog(dir, "loader_4.jsonl", """{"event":"configuration_error","message":"Preset must be Auto, A or B"}""");

            var result = LogInspector.Check(dir);
            Assert.True(result.LogsFound);
            Assert.Null(result.InstallActive);
            Assert.False(result.Verified);
            Assert.Contains(result.Messages, m => m.Contains("No runtime_redirect event"));
            Assert.Contains(result.Messages, m => m.Contains("Preset must be Auto"));
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
            var result = LogInspector.Check(dir);
            Assert.False(result.LogsFound);
            Assert.False(result.Verified);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

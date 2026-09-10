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

    [Fact]
    public void Check_FindsActivationMarkers()
    {
        string dir = TempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "dlssg_sm86", "logs"));
            File.WriteAllText(Path.Combine(dir, "dlssg_sm86", "logs", "loader_1.jsonl"), """
                {"event":"configuration","ini":"C:\\game\\dlssg_sm86.ini","mode":"Bundled"}
                {"event":"install","active":true,"actual_sm":86,"image":"ptx_sm86"}
                {"event":"backend_install","status":0,"install":{"active":true}}
                {"event":"mfg_capability","advertised_max":3}
                """);
            File.WriteAllText(Path.Combine(dir, "dlssg_sm86", "logs", "backend_2.jsonl"), """
                {"event":"kernel_create","format":"ptx_sm86","status":0}
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
            Directory.CreateDirectory(Path.Combine(dir, "dlssg_sm86", "logs"));
            File.WriteAllText(Path.Combine(dir, "dlssg_sm86", "logs", "loader_9.jsonl"),
                "{\"event\":\"install\",\"active\":false,\"image\":\"original\"}\n");

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
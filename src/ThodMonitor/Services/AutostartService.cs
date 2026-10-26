using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace ThodMonitor.Services;

/// <summary>
/// Автозапуск с правами администратора: задача планировщика «при входе пользователя» с наивысшими правами.
/// (Запись в HKCU\Run для программ с запросом прав администратора Windows не запускает.)
/// </summary>
public static class AutostartService
{
    public static bool IsSupported => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("THODHUB_DATA_DIR"));
    private const string TaskName = "ThodMonitor";
    private const string LegacyKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static int Run(string args, out string output)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("schtasks не запустился");
        var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } output = "таймаут"; return -1; }
        output = (o.Result + e.Result).Trim();
        return p.ExitCode;
    }

    public static bool IsEnabled()
    {
        if (!IsSupported) return false;
        try { return Run($"/query /tn \"{TaskName}\"", out _) == 0; }
        catch (Exception ex) { Log.Error("Не удалось проверить автозапуск", ex); return false; }
    }

    public static bool Set(bool enabled)
    {
        if (!IsSupported) return false;
        try
        {
            RemoveLegacyRunEntry();
            if (!enabled)
            {
                if (!IsEnabled()) return true;
                int d = Run($"/delete /tn \"{TaskName}\" /f", out var dout);
                if (d != 0) Log.Error("Не удалось удалить задачу автозапуска: " + dout);
                return d == 0;
            }

            var tmp = Path.Combine(Path.GetTempPath(), "thodmonitor-task.xml");
            File.WriteAllText(tmp, BuildTaskXml(), Encoding.Unicode);   // schtasks ждёт UTF-16
            try
            {
                int c = Run($"/create /tn \"{TaskName}\" /xml \"{tmp}\" /f", out var cout);
                if (c != 0) Log.Error("Не удалось создать задачу автозапуска: " + cout);
                return c == 0;
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
        catch (Exception ex) { Log.Error("Не удалось изменить автозапуск", ex); return false; }
    }

    private static string BuildTaskXml()
    {
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name) ?? "";
        string exe = SecurityElement.Escape(Environment.ProcessPath) ?? "";
        string dir = SecurityElement.Escape(Path.GetDirectoryName(Environment.ProcessPath)) ?? "";
        return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Thod Monitor: автозапуск</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
  </Settings>
  <Actions Context=""Author""><Exec><Command>{exe}</Command><Arguments>--minimized</Arguments><WorkingDirectory>{dir}</WorkingDirectory></Exec></Actions>
</Task>";
    }

    private static string? QueryXml()
    {
        try { return Run($"/query /tn \"{TaskName}\" /xml", out var o) == 0 ? o : null; }
        catch { return null; }
    }

    /// <summary>
    /// Вызывается при каждом запуске (в фоне): переводит старый автозапуск (HKCU\Run) на задачу с правами администратора
    /// и пересоздаёт задачу, если она без «наивысших прав» или указывает на другой путь.
    /// </summary>
    public static bool Reconcile()
    {
        if (!IsSupported) return false;
        try
        {
            bool legacy;
            using (var k = Registry.CurrentUser.OpenSubKey(LegacyKeyPath)) legacy = k?.GetValue("ThodMonitor") is string;
            var xml = QueryXml();
            if (!legacy && xml == null) return false;                       // автозапуск не включён
            string exe = SecurityElement.Escape(Environment.ProcessPath) ?? "";
            bool ascii = exe.All(c => c < 128);                             // кодировка вывода schtasks: путь сравниваем, только если он ASCII
            bool ok = xml != null && xml.Contains("HighestAvailable") && (!ascii || xml.Contains(exe, StringComparison.OrdinalIgnoreCase));
            if (!ok) return Set(true);
            RemoveLegacyRunEntry();
            return true;
        }
        catch (Exception ex) { Log.Error("Не удалось сверить автозапуск", ex); return false; }
    }

    /// <summary>Убирает запись автозапуска из прежних версий (HKCU\Run), чтобы не было двойного запуска.</summary>
    private static void RemoveLegacyRunEntry()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(LegacyKeyPath, true); key?.DeleteValue("ThodMonitor", false); }
        catch { }
    }
}

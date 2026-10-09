using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvevaClashLink
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            if (args.Length == 1 && string.Equals(args[0], "--register", StringComparison.OrdinalIgnoreCase))
            {
                RegisterProtocol();
                return;
            }
            try
            {
                if (args.Length == 0)
                    return;

                Uri link = new Uri(args[0]);

                if (!string.Equals(link.Scheme, "avevaclash", StringComparison.OrdinalIgnoreCase))
                    return;

                string zone = GetQueryValue(link, "zone");
                string idValue = GetQueryValue(link, "id");

                if (string.IsNullOrWhiteSpace(zone))
                    return;

                if (!int.TryParse(idValue, out int clashId))
                    return;

                string commandDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AvevaClash",
                    "Commands");

                Directory.CreateDirectory(commandDirectory);

                string commandName =
                    $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";

                string temporaryPath =
                    Path.Combine(commandDirectory, commandName + ".tmp");

                string commandPath =
                    Path.Combine(commandDirectory, commandName + ".clashlink");

                File.WriteAllLines(
                    temporaryPath,
                    new[]
                    {
                        $"ZONE={zone}",
                        $"ID={clashId}"
                    });

                File.Move(temporaryPath, commandPath);
            }
            catch (Exception exception)
            {
                WriteError(exception);
            }
        }

        private static string GetQueryValue(Uri link, string parameterName)
        {
            string query = link.Query.TrimStart('?');
            string[] parameters = query.Split('&');

            foreach (string parameter in parameters)
            {
                string[] parts = parameter.Split(new[] { '=' }, 2);

                if (parts.Length != 2)
                    continue;

                string name = Uri.UnescapeDataString(parts[0]);

                if (!string.Equals(name, parameterName, StringComparison.OrdinalIgnoreCase))
                    continue;

                return Uri.UnescapeDataString(parts[1].Replace("+", " "));
            }

            return "";
        }

        private static void WriteError(Exception exception)
        {
            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AvevaClash");

            Directory.CreateDirectory(logDirectory);

            string logPath = Path.Combine(
                logDirectory,
                "AvevaClashLinkErrors.log");

            File.AppendAllText(
                logPath,
                $"{DateTime.Now:dd.MM.yyyy HH:mm:ss}\n" +
                $"{exception}\n\n");
        }
        private static void RegisterProtocol()
        {
            string applicationPath = Process.GetCurrentProcess().MainModule.FileName;

            using (RegistryKey protocolKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\avevaclash"))
            {
                protocolKey.SetValue("", "URL:Aveva Clash Protocol");
                protocolKey.SetValue("URL Protocol", "");
            }

            using (RegistryKey commandKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\avevaclash\shell\open\command"))
            {
                commandKey.SetValue("", $"\"{applicationPath}\" \"%1\"");
            }
        }
    }
}

//&"C:\AVEVA\AvevaWorkDLL\CLS\ClashForm\AvevaClashLink\bin\\Debug\AvevaClashLink.exe" "avevaclash://open?zone=%2F1300-30UHJ-E006-TD&id=113656"
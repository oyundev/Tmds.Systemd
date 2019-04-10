using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.DragonFruit;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;

namespace Tmds.Systemd.Tool
{
    class Program
    {
        const string Unit = nameof(Unit);
        const string Service = nameof(Service);
        const string Install = nameof(Install);

        class Option
        {
            public Option(string section, string name, string @default)
            {
                Section = section;
                Name = name;
                Default = @default;
            }
            public string Section { get; set; }
            public string Name { get; set; }
            public string Default { get; set; }
        }

        static Option[] s_options = new Option[]
        {
            new Option(Unit, "Description", ".NET Core %name% service"),

            new Option(Service, "Type", "simple"),
            new Option(Service, "WorkingDirectory", "%assemblydirectory%"),
            new Option(Service, "ExecStart", "%dotnetpath% %assemblypath%"),
            new Option(Service, "Restart", "always"),
            new Option(Service, "SyslogIdentifier", "%name%"),
            new Option(Service, "User", null),
            new Option(Service, "Group", null),

            new Option(Install, "WantedBy", "multi-user.target"),
        };

        static int Main(string[] args)
        {
            var userOptions = new Dictionary<string, string>();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    userOptions.Add(args[i].Substring(2).ToUpperInvariant(), args[i + 1]);
                }
            }
            if (!userOptions.ContainsKey("NAME"))
            {
                Console.Error.WriteLine("A --name argument must be specified");
                return 1;
            }
            var substitutions = new Dictionary<string, string>();

            System.Console.WriteLine(args.Length);
            string assemblyPathValue = args.Length >= 1 ? args[0] : ".";
            System.Console.WriteLine(assemblyPathValue);
            assemblyPathValue = Path.GetFullPath(assemblyPathValue);
            System.Console.WriteLine(assemblyPathValue);
            if (Directory.Exists(assemblyPathValue))
            {
                assemblyPathValue = Directory.GetFiles(assemblyPathValue, "*.runtimeconfig.json")
                                    .FirstOrDefault()?.Replace(".runtimeconfig.json", ".dll");
            }
            System.Console.WriteLine(assemblyPathValue);
            if (assemblyPathValue == null || !File.Exists(assemblyPathValue))
            {
                Console.Error.WriteLine("Cannot determine the entrypoint assembly. Please specify it as an argument.");
                return 1;
            }
            string nameValue = userOptions["NAME"];
            substitutions.Add("%name%", nameValue);
            string dotnetPathValue = FindProgramInPath("dotnet");
            if (dotnetPathValue == null)
            {
                System.Console.WriteLine("Cannot find dotnet on PATH");
                return 1;
            }
            substitutions.Add("%dotnetpath%", dotnetPathValue);
            substitutions.Add("%assemblypath%", assemblyPathValue);
            substitutions.Add("%assemblydirectory%", Path.GetDirectoryName(assemblyPathValue));

            var sb = new StringBuilder();
            string currentSection = null;
            foreach (var option in s_options)
            {
                string optionValue = Evaluate(option.Name, userOptions, option.Default, substitutions);
                if (optionValue != null)
                {
                    if (currentSection != option.Section)
                    {
                        if (currentSection != null)
                        {
                            sb.AppendLine();
                        }
                        sb.AppendLine($"[{option.Section}]");
                        currentSection = option.Section;
                    }
                    sb.AppendLine($"{option.Name}={optionValue}");
                }
            }
            string systemdServiceFilePath = $"/etc/systemd/system/{nameValue}.service";
            System.Console.WriteLine($"Writing service file to: {systemdServiceFilePath}");
            try
            {
                using (FileStream fs = new FileStream(systemdServiceFilePath, FileMode.CreateNew))
                {
                    using (StreamWriter sw = new StreamWriter(fs))
                    {
                        sw.Write(sb.ToString());
                    }
                }
            }
            catch (IOException) when (File.Exists(systemdServiceFilePath))
            {
                System.Console.WriteLine("A service with that name already exists.");
                return 1;
            }
            catch (UnauthorizedAccessException)
            {
                System.Console.WriteLine("Cannot write file. Try running this command with 'sudo'.");
                return 1;
            }

            System.Console.WriteLine(sb);
            return 0;
        }

        private static string FindProgramInPath(string program)
        {
            string pathEnvVar = Environment.GetEnvironmentVariable("PATH");
            if (pathEnvVar != null)
            {
                var paths = pathEnvVar.Split(':');
                foreach (var path in paths)
                {
                    string filename = Path.Combine(path, program);
                    if (File.Exists(filename))
                    {
                        return filename;
                    }
                }
            }
            return null;
        }

        private static string Evaluate(string name, Dictionary<string, string> userOptions, string @default, Dictionary<string, string> substitutions)
        {
            string userValue;
            if (!userOptions.TryGetValue(name.ToUpperInvariant(), out userValue))
            {
                userValue = @default;
            }
            if (userValue != null)
            {
                foreach (var substitution in substitutions)
                {
                    userValue = userValue.Replace(substitution.Key, substitution.Value);
                }
            }
            return userValue;
        }

        static string ServiceTemplate = 
@"[Unit]
Description=My .NET Core Daemon

[Service]
Type=notify
WorkingDirectory=/var/mydaemon
ExecStart=/opt/dotnet/dotnet MyDaemon.dll
Restart=always
RestartSec=10
SyslogIdentifier=mydaemon
User=mydaemon

[Install]
WantedBy=multi-user.target";
    }
}

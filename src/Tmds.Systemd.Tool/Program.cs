using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.DragonFruit;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Tmds.Systemd.Tool
{
    class Program
    {
        static int Main(string[] args)
        {
            var rootCommand = new RootCommand();
            rootCommand.AddCommand(CreateServiceCommand());
            return rootCommand.InvokeAsync(args).Result;            
        }

        private static Command CreateServiceCommand()
        {
            var createServiceCommand = new Command("create-service", "Creates a systemd service", handler: CommandHandler.Create(new Func<string, string, ParseResult, int>(CreateServiceHandler)));
            createServiceCommand.AddOption(new Option("--name", "Name of the service (required)", new Argument<string>()));
            foreach (var configOption in UnitConfiguration.SystemServiceOptions)
            {
                if (configOption.CanSet)
                {
                    createServiceCommand.AddOption(new Option($"--{configOption.Name.ToLowerInvariant()}", $"Sets {configOption.Name}", new Argument<string>()));
                }
            }
            createServiceCommand.Argument = new Argument<string>() { Name = "application", Description = "Assembly to execute" };
            return createServiceCommand;
        }

        private static Dictionary<string, string> GetCommandOptions(ParseResult result)
        {
            var userOptions = new Dictionary<string, string>();
            foreach (var childResult in result.CommandResult.Children)
            {
                userOptions.Add(childResult.Name.ToUpperInvariant(), childResult.Arguments.First());
            }
            return userOptions;
        }

        private static bool GetRequired(Dictionary<string, string> commandOptions, string name, out string value)
        {
            if (!commandOptions.TryGetValue(name.ToUpperInvariant(), out value))
            {
                Console.WriteLine($"Missing required option: --{name}");
                return false;
            }
            return true;
        }

        private static bool ResolveAssembly(string application, out string assemblyValue)
        {
            assemblyValue = application;
            assemblyValue = Path.GetFullPath(assemblyValue);
            if (Directory.Exists(assemblyValue))
            {
                assemblyValue = Directory.GetFiles(assemblyValue, "*.runtimeconfig.json")
                                    .FirstOrDefault()?.Replace(".runtimeconfig.json", ".dll");
            }
            System.Console.WriteLine(assemblyValue);
            if (assemblyValue == null || !File.Exists(assemblyValue))
            {
                Console.Error.WriteLine("Cannot determine the entrypoint assembly. Please specify it as an argument.");
                return false;
            }

            return true;
        }

        private static bool FindProgramInPath(string program, out string programPath)
        {
            programPath = FindProgramInPath(program);
            if (programPath == null)
            {
                System.Console.WriteLine($"Cannot find {program} on PATH");
                return false;
            }

            return true;
        }

        private static string BuildUnitFile(ConfigurationOption[] options, Dictionary<string, string> userOptions, Dictionary<string, string> substitutions)
        {
            var sb = new StringBuilder();
            string currentSection = null;
            foreach (var option in UnitConfiguration.SystemServiceOptions)
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
            return sb.ToString();
        }

        private static int CreateServiceHandler(string name, string application, ParseResult result)
        {
            var commandOptions = GetCommandOptions(result);
            
            if (!GetRequired(commandOptions, "name", out string unitName) ||
                !ResolveAssembly(application, out string applicationPath) ||
                !FindProgramInPath("dotnet", out string dotnetPath))
            {
                return 1;
            }

            var substitutions = new Dictionary<string, string>();
            substitutions.Add("%name%", unitName);
            string execstart = $"'{dotnetPath}' '{applicationPath}'";
            string scls = Environment.GetEnvironmentVariable("X_SCLS");
            if (scls != null)
            {
                string sclPath = FindProgramInPath("scl");
                if (sclPath != null)
                {
                    execstart = $"{sclPath} enable {scls} -- {execstart}";
                }
            }
            substitutions.Add("%execstart%", execstart);
            substitutions.Add("%applicationdirectory%", Path.GetDirectoryName(applicationPath));

            string unitFileContent = BuildUnitFile(UnitConfiguration.SystemServiceOptions, commandOptions, substitutions);

            string systemdServiceFilePath = $"/etc/systemd/system/{unitName}.service";
            try
            {
                using (FileStream fs = new FileStream(systemdServiceFilePath, FileMode.CreateNew))
                {
                    using (StreamWriter sw = new StreamWriter(fs))
                    {
                        sw.Write(unitFileContent);
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
                string sudoCommand = "sudo";
                if (scls != null)
                {
                    sudoCommand = $"{sudoCommand} scl enable {scls} --";
                }
                System.Console.WriteLine($"Cannot write file. Try running this command with '{sudoCommand}'.");
                return 1;
            }

            System.Console.WriteLine($"Writing service file to: {systemdServiceFilePath}");

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
    }
}

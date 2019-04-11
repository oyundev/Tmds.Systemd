namespace Tmds.Systemd.Tool
{

    class ConfigurationOption
    {
        public ConfigurationOption(string section, string name, string @default, bool canSet = true)
        {
            Section = section;
            Name = name;
            Default = @default;
            CanSet = canSet;
        }
        public string Section { get; }
        public string Name { get; }
        public string Default { get; }
        public bool CanSet { get; }
    }

    class UnitConfiguration
    {
        const string Unit = nameof(Unit);
        const string Service = nameof(Service);
        const string Install = nameof(Install);

        public static readonly ConfigurationOption[] SystemServiceOptions = new ConfigurationOption[]
        {
            new ConfigurationOption(Unit, "Description", ".NET Core %name%"),

            new ConfigurationOption(Service, "Type", "simple"),
            new ConfigurationOption(Service, "WorkingDirectory", "%applicationdirectory%"),
            new ConfigurationOption(Service, "ExecStart", "%execstart%", canSet: false),
            new ConfigurationOption(Service, "Restart", "always"),
            new ConfigurationOption(Service, "SyslogIdentifier", "%name%"),
            new ConfigurationOption(Service, "User", null),
            new ConfigurationOption(Service, "Group", null),

            new ConfigurationOption(Install, "WantedBy", "multi-user.target"),
        };
    }
}
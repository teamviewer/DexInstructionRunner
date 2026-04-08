using System.Collections.Generic;

namespace DexInstructionRunner.Helpers
{
    public class FilterAttributeDefinition
    {
        public string Name { get; set; } = "";
        public string? ApiName { get; set; } = null;
        public List<string>? AllowedValues { get; set; } = null;
        public List<string>? Operators { get; set; } = null;
    }

    public static class DynamicTargetingHelper
    {
        public static List<FilterAttributeDefinition> GetAttributes()
        {
            return new List<FilterAttributeDefinition>
            {
                new() { Name = "FQDN", ApiName = "fqdn" },
                new() { Name = "ManagementGroup", ApiName = "managementgroup" },
                new() { Name = "OsType", ApiName = "ostype", AllowedValues = new() { "Windows | Windows", "Linux | Linux", "MacOS | MacOS" } },
                new()
                        {
                            Name = "DeviceType",
                            ApiName = "devtype",
                            AllowedValues = new()
                            {
                                "Desktop | Desktop",
                                "Laptop | Laptop",
                                "Mobile | Mobile",
                                "Server | Server"
                            }
                        },
                new()
                        {
                            Name = "Criticality",
                            ApiName = "criticality",
                            AllowedValues = new()
                            {
                                "Undefined | 0",
                                "Non-critical | 1",
                                "Low | 2",
                                "Medium | 3",
                                "High | 4",
                                "Critical | 5"
                            }
                        },
                new()
                            {
                                Name = "VirtualPlatform",
                                ApiName = "vrplatform",
                                AllowedValues = new()
                                {
                                    "Virtual Server | Virtual Server",
                                    "Hyper-V | Hyper-V",
                                    "VMWare | VMWare",
                                    "VirtualBox | VirtualBox",
                                    "Red Hat KVM | Red Hat KVM",
                                    "Nutanix Acropolis | Nutanix Acropolis"
                                }
                            },
                new() { Name = "PrimaryUser", ApiName = "user" },
                new() { Name = "OsVersion", ApiName = "osver" },
                new() { Name = "Model", ApiName = "model" },
                new() { Name = "Location", ApiName = "location" },
                new() { Name = "Domain", ApiName = "domain" },
                new() {
                    Name = "TimeZone",
                    ApiName = "timezone",
                    AllowedValues = new()
                    {
                        "(UTC-12:00) International Date Line West|-720",
                        "(UTC-11:00) Coordinated Universal Time-11|-660",
                        "(UTC-10:00) Hawaii|-600",
                        "(UTC-09:00) Alaska|-540",
                        "(UTC-08:00) Pacific Time (US & Canada)|-480",
                        "(UTC-07:00) Arizona|-420",
                        "(UTC-06:00) Central Time (US & Canada)|-360",
                        "(UTC-05:00) Eastern Time (US & Canada)|-300",
                        "(UTC-04:00) Atlantic Time (Canada)|-240",
                        "(UTC-03:00) Brasilia|-180",
                        "(UTC-02:00) Mid-Atlantic|-120",
                        "(UTC-01:00) Azores|-60",
                        "(UTC+00:00) Dublin, Lisbon, London|0",
                        "(UTC+01:00) Amsterdam, Berlin, Rome, Paris|60",
                        "(UTC+02:00) Athens, Bucharest, Istanbul|120",
                        "(UTC+03:00) Moscow, St. Petersburg, Volgograd|180",
                        "(UTC+03:30) Tehran|210",
                        "(UTC+04:00) Abu Dhabi, Muscat|240",
                        "(UTC+04:30) Kabul|270",
                        "(UTC+05:00) Islamabad, Karachi|300",
                        "(UTC+05:30) Chennai, Kolkata, Mumbai|330",
                        "(UTC+05:45) Kathmandu|345",
                        "(UTC+06:00) Astana, Dhaka|360",
                        "(UTC+06:30) Yangon (Rangoon)|390",
                        "(UTC+07:00) Bangkok, Hanoi, Jakarta|420",
                        "(UTC+08:00) Beijing, Perth, Singapore|480",
                        "(UTC+09:00) Osaka, Sapporo, Tokyo|540",
                        "(UTC+09:30) Adelaide|570",
                        "(UTC+10:00) Brisbane, Sydney|600",
                        "(UTC+11:00) Magadan, Solomon Islands|660",
                        "(UTC+12:00) Auckland, Wellington|720",
                        "(UTC+13:00) Nuku'alofa|780",
                        "(UTC+14:00) Kiritimati Island|840"
                    }
                },
                new() { Name = "LastBootTime", ApiName = "lastboot", AllowedValues = new() { "Before", "After" } },
                new() { Name = "RamMB", ApiName = "rammb" },
                new() { Name = "AgentVersion", ApiName = "agentver" }
            };

        }

    }
}

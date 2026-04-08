using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DexInstructionRunner
{
    public partial class DeviceDetailWindow : Window
    {
        public DeviceDetailWindow(DeviceTowerModel device)
        {
            InitializeComponent();
            var panel = this.FindControl<StackPanel>("DeviceDetailPanel");

            void AddRow(string label, string? value)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                row.Children.Add(new TextBlock
                {
                    Text = label + ":",
                    FontWeight = FontWeight.Bold,
                    Width = 160
                });
                row.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(value) ? "N/A" : value,
                    TextWrapping = TextWrapping.Wrap
                });
                panel.Children.Add(row);
            }

            AddRow("FQDN", device.Fqdn);
            AddRow("Name", device.Name);
            AddRow("Device Type", device.DeviceType);
            AddRow("Domain", device.Domain);
            AddRow("OS Type", device.OsType);
            AddRow("OS Version", device.OsVerTxt);
            AddRow("OS Architecture", device.OsArchitecture);
            AddRow("CPU", device.CpuType);
            AddRow("CPU Architecture", device.CpuArchitecture);
            AddRow("RAM (MB)", device.RamMB.ToString());
            AddRow("Manufacturer", device.Manufacturer);
            AddRow("Model", device.Model);
            AddRow("Serial Number", device.SerialNumber);
            AddRow("BIOS Version", device.BiosVersion);
            AddRow("User", device.User);
            AddRow("MAC", device.MAC);
            AddRow("Local IP", device.LocalIpAddress);
            AddRow("Connecting IP", device.ConnectingIpAddress);
            AddRow("OU Path", device.OuPath);
            AddRow("Criticality", device.Criticality.ToString());
            AddRow("Location", device.Location);
            AddRow("Features", device.Features);
            AddRow("OS Locale", device.OsLocale);
            AddRow("Last Boot Time", device.LastBootUTC.ToString("g"));
            AddRow("Last Connected", device.LastConnUtc.ToString("g"));
            AddRow("OS Install Date", device.OsInstallUtc.ToString("g"));
            AddRow("Created Date", device.CreatedUtc.ToString("g"));
            AddRow("Cert Type", device.CertType);
            AddRow("Cert Expiry", device.CertExpiryUtc?.ToString("g"));
            AddRow("Time Zone", device.TimeZone.ToString());
            AddRow("Time Zone ID", device.TimeZoneId);
            AddRow("Default Gateway", device.DefaultGateway);
            AddRow("Primary DNS", device.PrimaryDnsServer);
            AddRow("Secondary DNS", device.SecondaryDnsServers);
            AddRow("Primary Conn Type", device.PrimaryConnectionType);
            AddRow("Free OS Disk Space (MB)", device.FreeOsDiskSpaceMb.ToString());

            if (device.CoverageTags != null && device.CoverageTags.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Coverage Tags:",
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(0, 10, 0, 0)
                });

                foreach (var tag in device.CoverageTags)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"  • {tag.Key} = {tag.Value}"
                    });
                }
            }
        }
    }
}

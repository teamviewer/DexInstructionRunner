using System;
using System.Collections.Generic;

public class DeviceTowerModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Fqdn { get; set; }
    public int Status { get; set; }
    public string OsType { get; set; }
    public long OsVerNum { get; set; }
    public string OsVerTxt { get; set; }
    public long AgentVersion { get; set; }
    public string Manufacturer { get; set; }
    public int ChassisType { get; set; }
    public string DeviceType { get; set; }
    public string CpuType { get; set; }
    public string CpuArchitecture { get; set; }
    public string OsArchitecture { get; set; }
    public int RamMB { get; set; }
    public Guid SMBiosGuid { get; set; }
    public Guid TachyonGuid { get; set; }
    public DateTime LastBootUTC { get; set; }
    public DateTime LastConnUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string VrPlatform { get; set; }
    public int TimeZone { get; set; }
    public string CertType { get; set; }
    public DateTime? CertExpiryUtc { get; set; }
    public string Model { get; set; }
    public string Domain { get; set; }
    public string Tags { get; set; }
    public List<string> ConnectionState { get; set; }
    public string LocalIpAddress { get; set; }
    public string TimeZoneId { get; set; }
    public string SerialNumber { get; set; }
    public int Criticality { get; set; }
    public string Location { get; set; }
    public string Features { get; set; }
    public string User { get; set; }
    public string MAC { get; set; }
    public string ConnectingIpAddress { get; set; }
    public string OsLocale { get; set; }
    public DateTime OsInstallUtc { get; set; }
    public string DhcpServer { get; set; }
    public DateTime? DhcpLeaseExpiryUtc { get; set; }
    public string DefaultGateway { get; set; }
    public string PrimaryDnsServer { get; set; }
    public string SecondaryDnsServers { get; set; }
    public string PrimaryConnectionType { get; set; }
    public string OuPath { get; set; }
    public string BatteryHealth { get; set; }
    public string BiosVersion { get; set; }
    public string NativeDiskEncryption { get; set; }
    public int FreeOsDiskSpaceMb { get; set; }
    public Dictionary<string, string> CoverageTags { get; set; }
}

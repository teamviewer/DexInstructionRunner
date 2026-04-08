using System.Collections.Generic;

namespace DexInstructionRunner.Models
{
    public class TemplateConfigurationContainer
    {
        public List<ChartConfiguration> TemplateConfigurations { get; set; } = new();
    }

    public class ChartConfiguration
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Type { get; set; } = "";  // e.g., Bar, Pie, etc.
        public string X { get; set; } = "";
        public string Y { get; set; } = "";
    }
}

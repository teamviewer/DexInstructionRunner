using System.Collections.Generic;

namespace DexInstructionRunner.Models
{
    public class ExperienceMeasure
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }

        public string Unit { get; set; }
        public bool Hidden { get; set; }
        public string DataType { get; set; }
        public List<string> Measures { get; set; }
        public string BadgeType { get; set; }
        public Weight Weight { get; set; }
        public string? Description { get; set; }
        public List<ExperienceMeasure> Children { get; set; }
        public List<string> InvestigationCategories { get; set; }
        public string? Notes { get; set; } // Add Notes field
        public Metadata Metadata { get; set; }
    }

    public class Weight
    {
        public float? Default { get; set; }
    }

    public class Metadata
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public object Normalization { get; set; }  // Normalize the value as per the structure
        public bool Hidden { get; set; }
        public string Unit { get; set; }
        public string DataType { get; set; }
        public int SourceType { get; set; }
        public bool IsLicensed { get; set; }
        public List<string> Notes { get; set; } // Add Notes field
        public int Type { get; set; }
        public List<string> Measures { get; set; }
        public List<Metadata> Children { get; set; }
        public string BadgeType { get; set; }
        public Weight Weight { get; set; }
    }

    public class ExperienceMeasureResponse
    {
        public List<ExperienceMeasure> Measures { get; set; }
    }

}

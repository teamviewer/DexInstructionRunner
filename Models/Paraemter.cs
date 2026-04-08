using System.Collections.Generic;
namespace DexInstructionRunner.Models
{
    public class Parameter
    {
        public string Name { get; set; }
        public string Pattern { get; set; }
        public string DataType { get; set; }
        public string ControlType { get; set; }
        public string ControlMetadata { get; set; }
        public string Placeholder { get; set; }
        public string DefaultValue { get; set; }
        public string Value { get; set; }
        public string HintText { get; set; }
        public string Source { get; set; }
        public string Description { get; set; }
        public ValidationData Validation { get; set; }
    }

    public class ValidationData
    {
        public string Regex { get; set; }
        public string MaxLength { get; set; }
        public List<string> AllowedValues { get; set; }
        public object NumValueRestrictions { get; set; }
    }
}

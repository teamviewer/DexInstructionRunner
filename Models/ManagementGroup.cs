namespace DexInstructionRunner.Models
{
    public class ManagementGroup
    {
        public string Name { get; set; }
        public int UsableId { get; set; }

        public override string ToString() => Name;
    }
}

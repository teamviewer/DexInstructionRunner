namespace DexInstructionRunner.Models
{
    internal sealed class PlatformListItem
    {
        public string Alias { get; }
        public string Url { get; }

        public PlatformListItem(string alias, string url)
        {
            Alias = alias ?? string.Empty;
            Url = url ?? string.Empty;
        }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Alias) ? Url : Alias;
        }
    }
}

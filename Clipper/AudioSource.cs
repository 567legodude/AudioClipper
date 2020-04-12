namespace Clipper
{
    public class AudioSource
    {
        
        public string DisplayName { get; set; }
        
        public string Identifier { get; set; }
        
        public bool IsOutput { get; set; }

        public string SourceType => IsOutput ? "Output" : "Input";

    }
}
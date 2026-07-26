namespace AnalyzerBaseline
{
    // Skeleton-only source so the baseline runs a real CoreCompile under -t:Rebuild and
    // registers the exact SDK-default Analyzer/source-generator set the closures are
    // diffed against. No body, no initializer, synthesizes nothing.
    public sealed class Marker
    {
        public int Ordinal { get; }
    }
}

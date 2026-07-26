using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace GenSourceGenerator
{
    // INV-011 fixture: a source generator that emits EXECUTABLE content into the consumer
    // compilation. The consumer's own *.cs is skeleton-only, so a naive csproj-XML + *.cs
    // glob PASSES the consumer; only a REAL build runs this generator and surfaces the
    // emitted method body (a BlockSyntax), which the scanner then rejects.
    [Generator]
    public sealed class ExecutableEmitter : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(ctx =>
                ctx.AddSource(
                    "Injected.g.cs",
                    SourceText.From(
                        "namespace Consumed { public static class Injected { public static int Detonate() { return 1; } } }",
                        Encoding.UTF8)));
        }
    }
}

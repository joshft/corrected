// STUB:TDD marker present ONLY to satisfy the RED-phase workflow-gate hook (see the
// system-io fixture). NEGATIVE FIXTURE for the PROJECT-GRAPH bound: the CODE is pure,
// but the .csproj declares a PackageReference (INV-004 / B4). The symbol scan would
// pass this; the project-graph predicate (KernelHasNoProjectOrPackageReference) must
// still reject it because a reference edge to an I/O-capable dependency is present.
namespace BadKernel.HasReference;

public static class PureEnoughKernel
{
    public static int Answer() => 42;
}

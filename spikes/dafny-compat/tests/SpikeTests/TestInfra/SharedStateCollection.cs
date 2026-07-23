// TA-A13 (AP-019): xUnit parallelism hazard — tests that mutate SHARED state
// (the run-context artifact directories, repo-local bin dirs, or process-global
// CultureInfo) must not run concurrently with other test classes. This
// collection is marked DisableParallelization, so xUnit runs its members
// serially and never overlapped with the parallel batch.
using Xunit;

namespace Corrected.Spike.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public class SharedStateMutatingCollection
{
    public const string Name = "shared-state-mutating";
}

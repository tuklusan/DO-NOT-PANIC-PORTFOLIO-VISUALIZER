using Xunit;

// Several tests intentionally mutate process-wide environment variables and
// TraceLog static state; keep the assembly serial so VM/CI scheduling cannot
// interleave those global-state assumptions.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

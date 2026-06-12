using Xunit;

// The test suite mutates process-wide environment variables and selected
// static diagnostic state; keep execution serial so VM/CI scheduling cannot
// interleave those global-state assumptions.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

using Xunit;

// The WPF input manager is ambient-singleton (WpfInputManager.Current):
// sessions replace it on construction, so interop tests run serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

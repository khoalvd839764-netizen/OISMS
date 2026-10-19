using Xunit;

// Disable parallel test execution within this assembly because integration tests share a single PostgreSQL database (oisms_test)
// and call ResetTestDataAsync().
[assembly: CollectionBehavior(DisableTestParallelization = true)]

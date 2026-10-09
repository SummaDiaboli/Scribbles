using Xunit;

// Native PDFium (via Docnet) is not safe under parallel test execution.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

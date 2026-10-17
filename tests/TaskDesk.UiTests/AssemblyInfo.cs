using Xunit.Sdk;
using Xunit.v3;

// One app at a time: every test drives the real keyboard, mouse and foreground window.
[assembly: Parallelization(Mode = ParallelMode.None)]

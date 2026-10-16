/*
 * FILE DOCUMENTATION
 * Where: Spaces/Tests/TestParallelization.cs, in the Spaces test assembly.
 * What: This file owns the assembly-level xUnit parallelization contract.
 * How: Every test class that starts an Avalonia HeadlessUnitTestSession shares one dispatcher.
 * Why: HeadlessUnitTestSession binds a dispatcher to the thread that created it. When two test
 *   classes run concurrently, one class's dispatcher is reset or driven by the other class's
 *   thread, and the failure surfaces as "The calling thread cannot access this object because a
 *   different thread owns it" attributed to whichever test happened to be running. That is a
 *   harness artefact, not a product defect, and it appears only once a second headless test class
 *   exists. The headless suites are fast (well under a second), so serializing them costs nothing
 *   and keeps every assertion intact.
 * Maintenance: Keep this attribute if the headless test classes are consolidated; it remains
 *   correct for a suite that starts one session per class.
 */

using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
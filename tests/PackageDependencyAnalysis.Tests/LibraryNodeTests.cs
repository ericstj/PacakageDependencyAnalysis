using Microsoft.NET.Build.Tasks;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace PackageDependencyAnalysis.Tests;

public class LibraryNodeTests
{
    [Fact]
    public void CanBuildGraph()
    {
        var lockFile = LockFileUtilities.GetLockFile("project.assets.json", NullLogger.Instance);
        var testTargetFramework = typeof(LibraryNodeTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        var graph = LibraryNode.BuildGraph(lockFile, testTargetFramework, null);

        Assert.NotNull(graph);
        Assert.NotEmpty(graph.Dependencies);
    }


    [Fact]
    public void ThrowsForMissingTargetFramework()
    {
        var lockFile = LockFileUtilities.GetLockFile("project.assets.json", NullLogger.Instance);

        Assert.Throws<BuildErrorException>(() => LibraryNode.BuildGraph(lockFile, "wpa8.0", null));
    }

    [Fact]
    public void CanTraverseNodes()
    {
        var lockFile = LockFileUtilities.GetLockFile("project.assets.json", NullLogger.Instance);
        var testTargetFramework = typeof(LibraryNodeTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        var graph = LibraryNode.BuildGraph(lockFile, testTargetFramework, null);

        graph.Traverse((path, node) => true);
    }

    [Fact]
    public void GraphIsComplete()
    {
        var lockFile = LockFileUtilities.GetLockFile("project.assets.json", NullLogger.Instance);
        var testTargetFramework = typeof(LibraryNodeTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        var graph = LibraryNode.BuildGraph(lockFile, testTargetFramework, null);
        HashSet<LibraryNode> nodes = new();

        graph.Traverse((path, node) => nodes.Add(node));

        var lockFileTarget = lockFile.GetTarget(NuGetFramework.Parse(testTargetFramework), null);

        // remove the root since it will not appear in the lock file
        nodes.Remove(graph);

        Assert.Equal(lockFileTarget.Libraries.Count, nodes.Count);

    }

    [Fact]
    public void GraphIsCompleteRecursive()
    {
        var lockFile = LockFileUtilities.GetLockFile("project.assets.json", NullLogger.Instance);
        var testTargetFramework = typeof(LibraryNodeTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        var graph = LibraryNode.BuildGraph(lockFile, testTargetFramework, null);
        HashSet<LibraryNode> nodes = new();

        graph.TraverseRecursive((path, node) => nodes.Add(node));

        var lockFileTarget = lockFile.GetTarget(NuGetFramework.Parse(testTargetFramework), null);

        // remove the root since it will not appear in the lock file
        nodes.Remove(graph);

        Assert.Equal(lockFileTarget.Libraries.Count, nodes.Count);

    }
}

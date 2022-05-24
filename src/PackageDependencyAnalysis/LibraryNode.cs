// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.NET.Build.Tasks;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.ProjectModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PackageDependencyAnalysis;

/// <summary>
/// Metedata and information for a package listed in the lock file.
/// </summary>
public sealed class LibraryNode
{
    private List<LibraryNode> _dependencies = new();
    private List<LibraryNode> _dependers = new();
    private LockFileTargetLibrary _library;

    public LibraryNode(LockFileTargetLibrary library)
    {
        _library = library;
        Id = _library.Name;
        Version = _library.Version.ToString();
        Type = _library.Type;
    }

    private LibraryNode(string id, string version, string type) 
    { 
        Id = id;
        Version = version;
        Type = type;
    }

    public string Id { get; }
    public string Version { get; }
    public string Type { get; }
    public bool IsMetaPackage
    {
        get => _library != null &&
               _library.CompileTimeAssemblies.Count == 0 &&
               _library.RuntimeAssemblies.Count == 0 &&
               _library.RuntimeTargets.Count == 0;
    }

    public IReadOnlyList<LibraryNode> Dependencies { get => _dependencies; }
    public IReadOnlyList<LibraryNode> Dependers { get => _dependers; }

    /// <summary>
    /// Walks the graph of nodes from this node
    /// </summary>
    /// <param name="visit"></param>
    public void Traverse(Func<IEnumerable<LibraryNode>, LibraryNode, bool> visit)
    {
        Stack<LibraryNode> stack = new Stack<LibraryNode>();
        Stack<IEnumerator<LibraryNode>> dependencies = new();

        PushNode(this);

        while (stack.Count > 0)
        {
            var current = stack.Peek();

            if (!visit(stack, current))
            {
                PopNode();
            }

            // find the next node
            var dependencyEnumerator = dependencies.Peek();

            // no more dependencies this node
            while (!dependencyEnumerator.MoveNext())
            {
                PopNode();

                // move to the parent to try it's next dependency, break if we reach end.
                if (!dependencies.TryPeek(out dependencyEnumerator))
                    break;
            }

            if (dependencyEnumerator != null)
            {
                PushNode(dependencyEnumerator.Current);
            }
        }

        void PushNode(LibraryNode node)
        {
            stack.Push(node);
            dependencies.Push(node.Dependencies.GetEnumerator());
        }

        void PopNode()
        {
            dependencies.Pop();
            stack.Pop();
        }
    }

    public void TraverseRecursive(Func<IEnumerable<LibraryNode>, LibraryNode, bool> visit, Stack<LibraryNode> stack = null)
    {
        stack ??= new Stack<LibraryNode>();
        stack.Push(this);

        if (visit(stack, this))
        {
            foreach (var dependency in Dependencies)
            {
                dependency.TraverseRecursive(visit, stack);
            }
        }

        stack.Pop();
    }

    private void PopulateDependencies(IDictionary<string, LibraryNode> packages) =>
        PopulateDependencies(packages, _library.Dependencies.Select(d => d.Id));

    private void PopulateDependencies(IDictionary<string, LibraryNode> packages, IEnumerable<string> dependencies)
    { 
        foreach (var dependency in dependencies)
        {
            if (packages.TryGetValue(dependency, out var dependencyNode))
            {
                _dependencies.Add(dependencyNode);
                dependencyNode._dependers.Add(this);
            }
            else
            {
                throw new BuildErrorException($"Invalid assets file. Could not locate dependency {dependency} of {Id}.");
            }
        }
    }

    public static LibraryNode BuildGraph(LockFile lockFile, string targetFramework, string runtimeIdentifier)
    {
        var framework = NuGetFramework.Parse(targetFramework);
        var lockFileTarget = lockFile.GetTarget(framework, runtimeIdentifier);

        if (lockFileTarget == null)
        {
            var targetString = string.IsNullOrEmpty(runtimeIdentifier) ? targetFramework : $"{targetFramework}/{runtimeIdentifier}";
            throw new BuildErrorException($"Missing target section {targetString} from assets file.  Ensure you have restored this project previously.");
        }

        Dictionary<string, LibraryNode> libraryMap = new(StringComparer.OrdinalIgnoreCase);
        foreach (var library in lockFileTarget.Libraries)
        {
            libraryMap.Add(library.Name, new LibraryNode(library));
        }

        foreach (var node in libraryMap.Values)
        {
            node.PopulateDependencies(libraryMap);
        }

        var projectName = lockFile.PackageSpec.Name; 
        var version = lockFile.PackageSpec.Version.ToString();

        var root = new LibraryNode(projectName, version, "project");

        var projectFileDependencyGroup = lockFile.ProjectFileDependencyGroups.FirstOrDefault(dg => NuGetFramework.Parse(dg.FrameworkName) == framework);

        if (projectFileDependencyGroup == null)
        {
            throw new BuildErrorException($"Missing projectFileDependencyGroup section for {framework.GetShortFolderName()} from assets file.  Ensure you have restored this project previously.");
        }
        
        root.PopulateDependencies(libraryMap, projectFileDependencyGroup.Dependencies.Select(d => GetPackageNameFromDependency(d)));

        return root;

        string GetPackageNameFromDependency(string dependency)
        {
            int index = dependency.IndexOf(' ');
            return index == -1 ? dependency : dependency.Substring(0, index);
        }
    }

    public IEnumerable<string> GetReferencePaths()
    {
        // perform a depth first search up the graph to find all pathways to this package node
        Queue<(string path, LibraryNode node)> paths = new();
        paths.Enqueue((Id, this));

        while(paths.Count > 0)
        {
            var pathNode = paths.Dequeue();

            if (pathNode.node.Dependers.Count == 0)
            {
                // if this node has no dependers, it's a complete path
                yield return pathNode.path;
            }
            else
            {
                // queue the next level of dependenders
                foreach (var depender in pathNode.node.Dependers)
                {
                    paths.Enqueue(($"{depender.Id} > {pathNode.path}", depender));
                }
            }
        }
    }


    public override string ToString()
    {
        return Id;
    }
}

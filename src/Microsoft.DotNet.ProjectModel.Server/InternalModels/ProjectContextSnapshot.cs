// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.ProjectModel.Compilation;
using Microsoft.DotNet.ProjectModel.Graph;
using Microsoft.DotNet.ProjectModel.Server.Helpers;
using Microsoft.DotNet.ProjectModel.Server.Models;
using NuGet.Frameworks;

namespace Microsoft.DotNet.ProjectModel.Server
{
    internal class ProjectContextSnapshot
    {
        public string RootDependency { get; set; }
        public NuGetFramework TargetFramework { get; set; }
        public IReadOnlyList<string> SourceFiles { get; set; }
        public CommonCompilerOptions CompilerOptions { get; set; }
        public IReadOnlyList<ProjectReferenceDescription> ProjectReferences { get; set; }
        public IReadOnlyList<string> FileReferences { get; set; }
        public IReadOnlyList<DiagnosticMessage> DependencyDiagnostics { get; set; }
        public IDictionary<string, DependencyDescription> Dependencies { get; set; }

        public static ProjectContextSnapshot Create(ProjectContext context, string configuration, IEnumerable<string> currentSearchPaths)
        {
            var snapshot = new ProjectContextSnapshot();

            var allDependencyDiagnostics = new List<DiagnosticMessage>();
            allDependencyDiagnostics.AddRange(context.LibraryManager.GetAllDiagnostics());
            allDependencyDiagnostics.AddRange(DependencyTypeChangeFinder.Diagnose(context, currentSearchPaths));

            var diagnosticsLookup = allDependencyDiagnostics.ToLookup(d => d.Source);

            var allLibraries = context.LibraryManager.GetLibraries();
            var allSourceFiles = new List<string>(context.ProjectFile.Files.SourceFiles);
            var allFileReferences = new List<string>();
            var allProjectReferences = new List<ProjectReferenceDescription>();
            var allDependencies = new Dictionary<string, DependencyDescription>();

            // filter out the packages override by system assemblies
            var allLibraryExports = new Dictionary<string, LibraryExport>();
            foreach (var export in context.CreateExporter(configuration).GetAllExports())
            {
                LibraryExport current;
                if (!allLibraryExports.TryGetValue(export.Library.Identity.Name, out current) ||
                     export.Library.Identity.Type == LibraryType.ReferenceAssembly)
                {
                    current = export;
                }

                allLibraryExports[export.Library.Identity.Name] = export;
            }

            var allDependencyItems = new Dictionary<string, DependencyItem>();
            foreach (var library in allLibraries)
            {
                allDependencyItems[library.Identity.Name] = new DependencyItem
                {
                    Name = (library.Identity.Type != LibraryType.ReferenceAssembly) ? library.Identity.Name :
                                                                                      $"fx/{library.Identity.Name}",
                    Version = library.Identity.Version?.ToString()
                };
            }

            foreach (var library in allLibraries)
            {
                LibraryExport export;
                if (allLibraryExports.TryGetValue(library.Identity.Name, out export))
                {
                    allSourceFiles.AddRange(export.SourceReferences);
                    allFileReferences.AddRange(export.CompilationAssemblies.Select(asset => asset.ResolvedPath));
                }

                var diagnostics = diagnosticsLookup[library].ToList();
                var description = DependencyDescription.Create(library, diagnostics, allDependencyItems);
                allDependencies[description.Name] = description;

                var projectDescription = library as ProjectDescription;
                if (projectDescription != null && projectDescription.Identity.Name != context.ProjectFile.Name)
                {
                    allProjectReferences.Add(ProjectReferenceDescription.Create(projectDescription));
                }
            }

            snapshot.RootDependency = context.ProjectFile.Name;
            snapshot.TargetFramework = context.TargetFramework;
            snapshot.SourceFiles = allSourceFiles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToList();
            snapshot.CompilerOptions = context.ProjectFile.GetCompilerOptions(context.TargetFramework, configuration);
            snapshot.ProjectReferences = allProjectReferences.OrderBy(reference => reference.Name).ToList();
            snapshot.FileReferences = allFileReferences.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToList();
            snapshot.DependencyDiagnostics = allDependencyDiagnostics;
            snapshot.Dependencies = allDependencies;

            return snapshot;
        }
    }
}

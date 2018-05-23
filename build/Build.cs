using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.NuGet;
using Nuke.Common.Tools.Nunit;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tooling.NuGetPackageResolver;
using static Nuke.Common.Tools.MSBuild.MSBuildTasks;
using static Nuke.Common.Tools.NuGet.NuGetTasks;
using static Nuke.Common.Tools.Nunit.NunitTasks;

class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter] readonly string Source = "https://resharper-plugins.jetbrains.com/api/v2/package";
    [Parameter] readonly string ApiKey;

    [GitVersion] readonly GitVersion GitVersion;
    [GitRepository] readonly GitRepository GitRepository;
    [Solution] readonly Solution Solution;

    Target Clean => _ => _
        .Executes(() =>
        {
            DeleteDirectories(GlobDirectories(SourceDirectory, "**/bin", "**/obj"));
            EnsureCleanDirectory(OutputDirectory);
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            MSBuild(s => DefaultMSBuildRestore);
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            MSBuild(s => DefaultMSBuildCompile);
        });
    
    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            Nunit3(s => s
                .AddInputFiles(GlobFiles(SourceDirectory,
                    $"**/bin/{Configuration}/SerializationInspections.*.Tests.*.dll").NotEmpty())
                .EnableNoResults());
        });
    
    Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var releaseNotes = TextTasks.ReadAllLines(RootDirectory / "History.md")
                .SkipWhile(x => !x.StartsWith("##"))
                .Skip(count: 1)
                .TakeWhile(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => $"\u2022{x.TrimStart('-')}")
                .JoinNewLine();

            var projectFile = Solution.GetProject("SerializationInspections.Plugin.RS").NotNull();
            var waveVersion = GetWaveVersion(projectFile) + ".0";
            
            GlobFiles(SourceDirectory, "*/*.nuspec")
                .ForEach(x => NuGetPack(s => DefaultNuGetPack
                    .SetTargetPath(x)
                    .SetBasePath(SourceDirectory)
                    .SetProperty("wave", waveVersion)
                    .SetProperty("currentyear", DateTime.Now.Year.ToString())
                    .SetProperty("releasenotes", releaseNotes)
                    .EnableNoPackageAnalysis()));
        });
    
    Target Push => _ => _
        .DependsOn(Test, Pack)
        .Requires(() => ApiKey)
        .Requires(() => Configuration.EqualsOrdinalIgnoreCase("Release"))
        .Executes(() =>
        {
            GlobFiles(OutputDirectory, "*.nupkg")
                .ForEach(x => NuGetPush(s => s
                    .SetTargetPath(x)
                    .SetSource(Source)
                    .SetApiKey(ApiKey)));
        });
    
    static string GetWaveVersion(string projectFile)
    {
        var fullWaveVersion = GetLocalInstalledPackages(projectFile, includeDependencies: true)
            .SingleOrDefault(x => x.Id == "Wave").NotNull("fullWaveVersion != null").Version.ToString();
        return fullWaveVersion.Substring(startIndex: 0, length: fullWaveVersion.IndexOf(value: '.'));
    }
}

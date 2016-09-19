// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.PlatformAbstractions;
using Microsoft.DotNet.ProjectModel;
using Microsoft.DotNet.Tools.CrossGen.Exceptions;
using Microsoft.DotNet.Tools.CrossGen.Outputs;
using Microsoft.Extensions.DependencyModel;
using NuGet.Frameworks;

using static Microsoft.DotNet.Tools.CrossGen.Operations.FileNameConstants;

namespace Microsoft.DotNet.Tools.CrossGen
{
    public class CrossGenContext
    {
        private readonly string _appName;
        private readonly string _appDir;
        private readonly bool _generatePDB;

        private CrossGenTarget _crossGenTarget;
        private DependencyContext _dependencyContext;
        
        public CrossGenContext(string appName, string appDir, bool generatePDB)
        {
            _appName = appName;
            _appDir = appDir;
            _generatePDB = generatePDB;
        }

        public void Initialize()
        {
            LoadDependencyContext();
            DetermineCrossGenTarget();
            Reporter.Verbose.WriteLine($"CrossGen will be performed to target Framework: {_crossGenTarget.Framework}, RID: {_crossGenTarget.RID}");
        }

        public void ExecuteCrossGen(string crossGenExe, string diaSymReaderDll, string outputDir, CrossGenOutputStructure structure, bool overwriteHash)
        {
            if (_generatePDB && diaSymReaderDll == null)
            {
                diaSymReaderDll = FindDiaSymReader();
            }

            CrossGenHandler outputHandler;
            switch (structure)
            {
                case CrossGenOutputStructure.FLAT:
                    outputHandler = new FlatCrossGenHandler(crossGenExe, diaSymReaderDll, _crossGenTarget, _appDir, outputDir, _generatePDB);
                    break;
                
                case CrossGenOutputStructure.CACHE:
                    outputHandler = new OptimizationCacheCrossGenHandler(crossGenExe, diaSymReaderDll, _crossGenTarget, _appDir, outputDir, _generatePDB, overwriteHash);
                    break;

                default:
                    throw new CrossGenException($"Invalid output structure: {structure}");
            }

            foreach (var lib in _dependencyContext.RuntimeLibraries)
            {
                outputHandler.CrossGenAssets(lib);
            }
            outputHandler.OnCompleted();
        }

        private void LoadDependencyContext()
        {
            var depsFilePath = Path.Combine(_appDir, $"{_appName}.deps.json");
            if (!File.Exists(depsFilePath))
            {
                throw new CrossGenException($"Deps {depsFilePath} file not found");
            }

            using (var fstream = new FileStream(depsFilePath, FileMode.Open))
            {
                _dependencyContext = new DependencyContextJsonReader().Read(fstream);
            }

            if (_dependencyContext == null)
            {
                throw new CrossGenException($"Unexpected error while reading {depsFilePath}");
            }
        }

        private void DetermineCrossGenTarget()
        {
            var runtimeConfigPath = Path.Combine(_appDir, $"{_appName}.runtimeconfig.json");
            RuntimeConfig runtimeConfig = null;
            if (File.Exists(runtimeConfigPath))
            {
                runtimeConfig = new RuntimeConfig(runtimeConfigPath);
            }

            if (runtimeConfig != null && runtimeConfig.IsPortable)
            {
                Reporter.Verbose.WriteLine($"This is a portable app, runtime config file: {runtimeConfigPath}");
                GetCrossGenTargetForPortable(runtimeConfig);
            }
            else
            {
                Reporter.Verbose.WriteLine($"This is a standalone app, runtime config file: {(runtimeConfig == null ? runtimeConfigPath : "None")}");
                GetCrossGenTargetForSelfContained();
            }
            
            if (_crossGenTarget.Framework.Framework != ".NETCoreApp")
            {
                throw new CrossGenException($"App targets {_crossGenTarget.Framework.Framework} cannot be CrossGen'd, supported frameworks: [.NETCoreApp].");
            }
        }

        private string FindDiaSymReader()
        {
            var targetRid = _crossGenTarget.RID;
            var hostDepContext = DependencyContext.Default;
            var ridFallback = hostDepContext.RuntimeGraph.FirstOrDefault(fallback => fallback.Runtime == _crossGenTarget.RID);
            IEnumerable<string> ridList = new string[] { targetRid };
            if (ridFallback == null)
            {
                Reporter.Output.WriteLine($"Runtime {targetRid} fallback is not defined.");
                ridList = new string[] { targetRid };
            }
            else
            {
                ridList = ridList.Concat(ridFallback.Fallbacks);
            }

            var arch = RuntimeEnvironment.RuntimeArchitecture;

            var probeLocations = new string[] { Path.Combine(ApplicationEnvironment.ApplicationBasePath,
                $"{DynamicLibPrefix}Microsoft.DiaSymReader.Native.{arch}{DynamicLibSuffix}") }.Concat(
                    ridList.Select(rid => Path.Combine(ApplicationEnvironment.ApplicationBasePath,
                        "runtimes", rid, "native", $"{DynamicLibPrefix}Microsoft.DiaSymReader.Native.{arch}{DynamicLibSuffix}")));

            // x64, aka amd64
            if (arch == "x64")
            {
                var archSuffix = $".{arch}{DynamicLibSuffix}";
                probeLocations = probeLocations.Concat(
                    probeLocations.Where(l => l.EndsWith(archSuffix))
                                    .Select(l => l.Substring(0, l.Length - archSuffix.Length) + $".amd64{DynamicLibSuffix}"));
            }

            var foundLocation = probeLocations.FirstOrDefault(l => File.Exists(l));

            if (foundLocation == null)
            {
                throw new CrossGenException($"Failed to locate DiaSymReader for runtime {targetRid}");
            }

            Reporter.Output.WriteLine($"Found DiaSymReader {foundLocation}");
            return foundLocation;
        }

        ////////////////////////////////////////////////////////////////
        /// Get framework from deps file. We will determine RID from current dotnet host.
        ////////////////////////////////////////////////////////////////
        private void GetCrossGenTargetForPortable(RuntimeConfig runtimeConfig)
        {
            var framework = NuGetFramework.Parse(_dependencyContext.Target.Framework);
            var rid = RuntimeEnvironment.GetRuntimeIdentifier();
            Reporter.Verbose.WriteLine($"Assuming the app will be run using the current cli, we will CrossGen with the RID {rid}");
            var dotnetHome = Path.GetDirectoryName(new Muxer().MuxerPath);
            var sharedFrameworkPath = Path.Combine(dotnetHome, "shared", runtimeConfig.Framework.Name, runtimeConfig.Framework.Version);
            Reporter.Verbose.WriteLine($"Shared framework path: {sharedFrameworkPath}");
            _crossGenTarget = CrossGenTarget.CreatePortable(framework, rid, sharedFrameworkPath);
        }

        ////////////////////////////////////////////////////////////
        /// Get the CrossGenTarget from deps file
        ////////////////////////////////////////////////////////////
        private void GetCrossGenTargetForSelfContained()
        {
            var framework = NuGetFramework.Parse(_dependencyContext.Target.Framework);
            var rid = _dependencyContext.Target.Runtime;
            _crossGenTarget = CrossGenTarget.CreateSelfContained(framework, rid);
        }
    }
}

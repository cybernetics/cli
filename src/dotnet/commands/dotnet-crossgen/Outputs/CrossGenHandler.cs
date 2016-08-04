// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.CrossGen.Exceptions;
using Microsoft.DotNet.Tools.CrossGen.Operations;
using Microsoft.Extensions.DependencyModel;

using static Microsoft.DotNet.Tools.CrossGen.Operations.FileNameConstants;

namespace Microsoft.DotNet.Tools.CrossGen.Outputs
{
    public abstract class CrossGenHandler
    {
        // use ready2run because this is a common cache location
        // we can make this configurable if required
        private const NativeImageType DefaultCrossGenType = NativeImageType.Ready2Run;

        private readonly bool _generatePDB;
        private readonly List<string> _platformAssembliesPaths;
        private readonly CrossGenCmdUtil _crossGenCmds;

        protected string OutputDir { get; private set; }
        protected string AppDir { get; private set; }

        public CrossGenHandler(string crossGenExe, string diaSymReaderDll, CrossGenTarget crossGenTarget, string appDir, string outputDir, bool generatePDB)
        {
            AppDir = appDir;
            OutputDir = outputDir;
            _generatePDB = generatePDB;
            
            _platformAssembliesPaths = new List<string>();
            _platformAssembliesPaths.Add(AppDir);
            if (!string.IsNullOrEmpty(crossGenTarget.SharedFrameworkDir))
            {
                _platformAssembliesPaths.Add(crossGenTarget.SharedFrameworkDir);
            }

            _crossGenCmds = GetCrossGenCmds(crossGenExe, diaSymReaderDll, crossGenTarget);
        }

        public void CrossGenAssets(RuntimeLibrary lib)
        {
            if (VerifyLibForCrossGen(lib))
            {
                Reporter.Verbose.WriteLine($"Looking for assets to CrossGen for library {lib.Name}.{lib.Version}");
                foreach (var assetGroup in lib.RuntimeAssemblyGroups)
                {
                    // Is this needed?
                    // var runtime = assetGroup.Runtime;

                    // if (!string.IsNullOrEmpty(runtime) && runtime != _crossgenTarget.RID)
                    // {
                    //     Console.WriteLine($"Skipping asset group because it targets runtime {runtime}");
                    // }
                
                    foreach (var assetPath in assetGroup.AssetPaths.Where(p => p.EndsWith(".dll")))
                    {
                        var fileName = Path.GetFileName(assetPath);
                        // TODO: This is not right! Assets can be runtime-specific and in that case it wouldn't be in the AppDir
                        var sourcePath = Path.Combine(AppDir, fileName);

                        if (!File.Exists(sourcePath))
                        {
                            throw new CrossGenException($"Assembly {fileName} not found in {AppDir}");
                        }

                        if (!_crossGenCmds.ShouldExclude(sourcePath))
                        {
                            var outputFiles = _crossGenCmds.CrossGenAssembly(AppDir, sourcePath, _platformAssembliesPaths, OutputDir, _generatePDB);
                            Reporter.Verbose.WriteLine($"CrossGen successful for {assetPath}");
                            OnCrossGenCompletedForAsset(assetPath, outputFiles);
                        }
                    }

                    OnCrossGenCompletedForLib();
                }
            }
        }
        
        private CrossGenCmdUtil GetCrossGenCmds(string crossGenExe, string diaSymReaderDll, CrossGenTarget crossGenTarget)
        {
            var crossGenPath = string.IsNullOrEmpty(crossGenExe) ? ResolveCrossGenExeLocation() : crossGenExe;

            // TODO: Actually, we could support this if we really want to, we just need to copy files to to same directory
            if (_generatePDB)
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(crossGenPath);
                if (versionInfo.FileMajorPart <= 1 && versionInfo.FileMinorPart == 0)
                {
                    throw new CrossGenException($"Generate PDB is not supported for {crossGenPath}, version: {versionInfo.FileMajorPart}.{versionInfo.FileMinorPart} < 1.1.0");
                }
            }

            var jitPath = Path.Combine(AppDir, JITLibName);
            if (!File.Exists(jitPath))
            {
                jitPath = Path.Combine(crossGenTarget.SharedFrameworkDir, JITLibName);
                if (!File.Exists(jitPath))
                {
                    throw new CrossGenException($"Unable to resolve jit path. It should either be in the app directory or shared framework directory.");
                }
            }

            string diaSymReaderPath = _generatePDB ? (string.IsNullOrEmpty(diaSymReaderDll) ? ResolveDiaSymReaderLocation() : diaSymReaderDll) : null;
            return new CrossGenCmdUtil(crossGenPath, jitPath, diaSymReaderPath, DefaultCrossGenType);
        }

        private string ResolveCrossGenExeLocation()
        {
            throw new NotImplementedException("Please provide the location of crossgen.exe");
        }

        private string ResolveDiaSymReaderLocation()
        {
            throw new NotImplementedException("Please provide the location of diasymreader if pdb generation is desired");
        }

        public virtual void OnCompleted() {}
        protected virtual bool VerifyLibForCrossGen(RuntimeLibrary lib) { return true; }
        protected virtual void OnCrossGenCompletedForAsset(string assetPath, ICollection<string> outputFiles) {}
        protected virtual void OnCrossGenCompletedForLib() {}
    }
}

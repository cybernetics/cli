// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.CrossGen.Exceptions;
using Microsoft.Extensions.DependencyModel;

namespace Microsoft.DotNet.Tools.CrossGen.Outputs
{
    public class OptimizationCacheCrossGenHandler : CrossGenHandler
    {
        // looks like the hash value "{Algorithm}-{value}" should be handled as an opaque string
        private const string Sha512PropertyName = "sha512";
        private readonly string _archName;
        private readonly bool _overwriteOnConflict;
        private RuntimeLibrary _lib;
        private string _libRoot;
        private string _sha;

        public OptimizationCacheCrossGenHandler(string crossGenExe, string diaSymReaderDll, CrossGenTarget crossGenTarget, string appDir, string outputDir, bool generatePDB, bool overwriteOnConflict)
           : base (crossGenExe, diaSymReaderDll, crossGenTarget, appDir, outputDir, generatePDB)
        {
            _archName = crossGenTarget.RID.Split(new char[]{'-'}).Last();
            _overwriteOnConflict = overwriteOnConflict;
        }
        
        protected override bool VerifyLibForCrossGen(RuntimeLibrary lib)
        {
            if (!lib.Serviceable)
            {
                return false;
            }
            else
            {
                _lib = lib;
                _libRoot = Path.Combine(OutputDir, _archName, lib.Name, lib.Version);

                DetermineHashValue();
                return true;
            }
        }

        protected override void OnCrossGenCompletedForAsset(string assetPath, ICollection<string> outputFiles)
        {
            var targetAsset = Path.Combine(_libRoot, assetPath);
            var targetAssetFileInfo = new FileInfo(targetAsset);
            var outputDirectory = targetAssetFileInfo.Directory.FullName;
            Directory.CreateDirectory(outputDirectory);

            foreach (var file in outputFiles)
            {
                var fileName = Path.GetFileName(file);
                var destFileName = Path.Combine(outputDirectory, fileName);
                if (File.Exists(destFileName))
                {
                    Reporter.Output.WriteLine($"[INFO] Overwriting {destFileName} with the new CrossGen output");
                    File.Delete(destFileName);
                }
                File.Move(file, destFileName);
            }
        }

        protected override void OnCrossGenCompletedForLib()
        {
            if (Directory.Exists(_libRoot) && _sha != null)
            {
                File.WriteAllText(GetShaLocation(), _sha);
            }
            // TODO: copy the rest of the files
        }

        private string GetShaLocation()
        {
            return Path.Combine(_libRoot, $"{_lib.Name}.{_lib.Version}.nupkg.sha512");
        }

        private void DetermineHashValue()
        {
            var libHashString = _lib.Hash;
            if (!libHashString.StartsWith($"{Sha512PropertyName}-"))
            {
                throw new CrossGenException($"Unsupported Hash value for package {_lib.Name}.{_lib.Version}, value: {libHashString}");
            }
            var newShaValue = libHashString.Substring(Sha512PropertyName.Length + 1);

            var targetLibShaFile = GetShaLocation();
            
            if (!File.Exists(targetLibShaFile) || ShouldOverwrite(targetLibShaFile, newShaValue))
            {
                // We don't have to write until we need to
                _sha = newShaValue;
            }
            else
            {
                _sha = null;
            }
        }

        private bool ShouldOverwrite(string targetLibShaFile, string newShaValue)
        {
            var oldShaValue = File.ReadAllText(targetLibShaFile);
            if (oldShaValue == newShaValue)
            {
                return false;
            }
            else if (_overwriteOnConflict)
            {
                Reporter.Output.WriteLine($"[INFO] Hash mismatch found for {_lib.Name}.{_lib.Version}. Overwriting existing hash file. This might causes cache misses for other applications.");
                return true;
            }
            else
            {
                throw new CrossGenException($"Hash mismatch found for {_lib.Name}.{_lib.Version}.");
            }
        }
    }
}
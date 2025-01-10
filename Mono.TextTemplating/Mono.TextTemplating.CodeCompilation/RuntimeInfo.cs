//
// FrameworkHelpers.cs
//
// Author:
//       Mikayla Hutchinson <m.j.hutchinson@gmail.com>
//
// Copyright (c) 2018 Microsoft Corp
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Mono.TextTemplating.CodeCompilation
{
	enum RuntimeKind
	{
		NetCore,
		NetFramework,
		Mono
	}

	class RuntimeInfo
	{
		RuntimeInfo (RuntimeKind kind, string error)
		{
			Kind = kind;
			Error = error;
		}

		RuntimeInfo (RuntimeKind kind, string runtimeDir, Version runtimeVersion, string refAssembliesDir, string runtimeFacadesDir, string cscPath, CSharpLangVersion cscMaxLangVersion, CSharpLangVersion runtimeLangVersion)
		{
			Kind = kind;
			RuntimeVersion = runtimeVersion;
			RuntimeDir = runtimeDir;
			RefAssembliesDir = refAssembliesDir;
			RuntimeFacadesDir = runtimeFacadesDir;
			CscPath = cscPath;
			CscMaxLangVersion = cscMaxLangVersion;
			RuntimeLangVersion = runtimeLangVersion;
		}

		static RuntimeInfo FromError (RuntimeKind kind, string error) => new (kind, error);

		public RuntimeKind Kind { get; }
		public string Error { get; }
		public string RuntimeDir { get; }

		// may be null, as this is not a problem when the optional in-process compiler is used
		public string CscPath { get; }

		/// <summary>
		/// Maximum C# language version supported by C# compiler in <see cref="CscPath"/>.
		/// </summary>
		public CSharpLangVersion CscMaxLangVersion { get; }

		/// <summary>
		/// The C# version fully supported by the runtime, which is the default when targeting this runtime.
		/// </summary>
		/// <remarks>
		/// Using newer C# language versions is possible but some features may not work if they depend on runtime changes.
		/// </remarks>
		public CSharpLangVersion RuntimeLangVersion { get; }

		public bool IsValid => Error == null;
		public Version RuntimeVersion { get; }

		public string RefAssembliesDir { get; }
		public string RuntimeFacadesDir { get; }

		public static RuntimeInfo GetRuntime ()
		{
			// VS2022 can host a dotnet runtime, so we need to check for that first
			var dotnetCoreSdk = GetDotNetCoreSdk ();
			if (dotnetCoreSdk.IsValid) {
				return dotnetCoreSdk;
			}

			// we failed to find a dotnet runtime, so we need to check for mono or .net framework
			else{
				if (Type.GetType ("Mono.Runtime") != null)
				{
					return GetMonoRuntime ();
				}
				else if (RuntimeInformation.FrameworkDescription.StartsWith (".NET Framework", StringComparison.OrdinalIgnoreCase))
				{
					// we can use the MSBuild Roslyn compiler if it's available
					var roslynRuntime = MsbuildRoslynRootInfo ();
					// if we can't find the MSBuild Roslyn compiler, fall back to the .NET Framework compiler
					return roslynRuntime.IsValid ? roslynRuntime : GetNetFrameworkRuntime ();
				}
			}

			return FromError (RuntimeKind.NetFramework, "Could not determine runtime");
		}

		static RuntimeInfo GetMonoRuntime ()
		{
			var runtimeDir = Path.GetDirectoryName (typeof (object).Assembly.Location);
			var csc = Path.Combine (runtimeDir, "csc.exe");
			if (!File.Exists (csc)) {
				return FromError (RuntimeKind.Mono, "Could not find csc in host Mono installation" );
			}

			return new RuntimeInfo (
				RuntimeKind.Mono,
				runtimeDir: runtimeDir,
				// we don't really care about the version if it's not .net core
				runtimeVersion: new Version ("4.7.2"),
				refAssembliesDir: null,
				runtimeFacadesDir: Path.Combine (runtimeDir, "Facades"),
				cscPath: csc,
				//if mono has csc at all, we know it at least supports 6.0
				cscMaxLangVersion: CSharpLangVersion.v6_0,
				runtimeLangVersion: CSharpLangVersion.v5_0
			);
		}

		static RuntimeInfo GetNetFrameworkRuntime ()
		{
			var runtimeDir = Path.GetDirectoryName (typeof (object).Assembly.Location);
			var csc = Path.Combine (runtimeDir, "csc.exe");
			if (!File.Exists (csc)) {
				return FromError (RuntimeKind.NetFramework, "Could not find csc in host .NET Framework installation");
			}
			return new RuntimeInfo (
				RuntimeKind.NetFramework,
				runtimeDir: runtimeDir,
				// we don't really care about the version if it's not .net core
				runtimeVersion: new Version ("4.7.2"),
				refAssembliesDir: null,
				runtimeFacadesDir: runtimeDir,
				cscPath: csc,
				cscMaxLangVersion: CSharpLangVersion.v5_0,
				runtimeLangVersion: CSharpLangVersion.v5_0
			);
		}

		static RuntimeInfo MsbuildRoslynRootInfo ()
		{
			// expected to be something like C:\Program Files\Microsoft Visual Studio\2022\Enterprise\
			var vSInstalledDir = Environment.GetEnvironmentVariable ("VSINSTALLDIR");
			//MSBuild\Current\Bin\amd64
			if (string.IsNullOrEmpty (vSInstalledDir) || !Directory.Exists (vSInstalledDir)) {
				return FromError (RuntimeKind.NetFramework, "Could not locate MSBuild directory");
			}
			var runtimeDir = Path.Combine (vSInstalledDir, "MSBuild\\Current\\Bin\\amd64");
			var roslynDir = Path.Combine (vSInstalledDir, "MSBuild\\Current\\Bin\\Roslyn");
			var csc = Path.Combine (roslynDir, "csc.exe");
			// should now be C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe
			if (!File.Exists (csc)) {
				return FromError (RuntimeKind.NetFramework, "Could not find csc in MSBuild Roslyn installation");
			}

			var maxLangVersion = CSharpLangVersionHelper.ParseMaxLangVersion (csc);

			return new RuntimeInfo (
				RuntimeKind.NetFramework,
				runtimeDir: runtimeDir,
				// we don't really care about the version if it's not .net core
				runtimeVersion: new Version ("4.7.2"),
				refAssembliesDir: null,
				runtimeFacadesDir: roslynDir,
				cscPath: csc,
				cscMaxLangVersion: maxLangVersion,
				runtimeLangVersion: CSharpLangVersion.Latest // todo: Resolve this from the version of the compiler
			);
		}

		static RuntimeInfo GetDotNetCoreSdk ()
		{
			static bool DotnetRootIsValid (string root) => !string.IsNullOrEmpty (root) && (File.Exists (Path.Combine (root, "dotnet")) || File.Exists (Path.Combine (root, "dotnet.exe")));

			// the runtime dir is used when probing for DOTNET_ROOT
			// and as a fallback in case we cannot locate reference assemblies
			var runtimeDir = Path.GetDirectoryName (typeof (object).Assembly.Location);

			var dotnetRoot = Environment.GetEnvironmentVariable ("DOTNET_ROOT");

			if (!DotnetRootIsValid (dotnetRoot)) {
				// this will work if runtimeDir is $DOTNET_ROOT/shared/Microsoft.NETCore.App/5.0.0
				dotnetRoot = Path.GetDirectoryName (Path.GetDirectoryName (Path.GetDirectoryName (runtimeDir)));

				if (!DotnetRootIsValid (dotnetRoot)) {

					// see if there's a VS install, C:\Program Files\Microsoft Visual Studio\2022\Enterprise\dotnet\net8.0\runtime
					var vSInstalledDir = Environment.GetEnvironmentVariable ("VSINSTALLDIR");

					// find the dotnet directory and then the highest versioned runtime
					if (!string.IsNullOrEmpty (vSInstalledDir) && Directory.Exists (vSInstalledDir)) {
						var vsDotnetDir = Path.Combine (vSInstalledDir, "dotnet");
						if (Directory.Exists (vsDotnetDir)) {

							// in the VS install, find the highest versioned runtime
							dotnetRoot = FindHighestVersionedDirectory (vsDotnetDir, d => Directory.Exists (Path.Combine (d, "shared", "Microsoft.NETCore.App")), out _);
						}
					}

					if (!DotnetRootIsValid (dotnetRoot)) {
						return FromError (RuntimeKind.NetCore, "Could not locate .NET root directory from running app. It can be set explicitly via the `DOTNET_ROOT` environment variable.");
					}
				}
			}

			var hostVersion = Environment.Version;

			// fallback for .NET Core < 3.1, which always returned 4.0.x
			if (hostVersion.Major == 4)
			{
				// this will work if runtimeDir is $DOTNET_ROOT/shared/Microsoft.NETCore.App/5.0.0
				var versionPathComponent = Path.GetFileName (runtimeDir);
				if (SemVersion.TryParse (versionPathComponent, out var hostSemVersion)) {
					hostVersion = new Version (hostSemVersion.Major, hostSemVersion.Minor, hostSemVersion.Patch);
				}
				else {
					return FromError (RuntimeKind.NetCore, "Could not determine host runtime version");
				}
			}

			// find the highest available C# compiler. we don't load it in process, so its runtime doesn't matter.
			static string MakeCscPath (string d) => Path.Combine (d, "Roslyn", "bincore", "csc.dll");
			var sdkDir = FindHighestVersionedDirectory (Path.Combine (dotnetRoot, "sdk"), d => File.Exists (MakeCscPath (d)), out var sdkVersion);

			// it's okay if cscPath is null as we may be using the in-process compiler
			string cscPath = sdkDir == null ? null : MakeCscPath (sdkDir);
			var maxCSharpVersion = CSharpLangVersionHelper.FromNetCoreSdkVersion (sdkVersion);

			// it's ok if this is null, we may be running on an older SDK that didn't support packs
			//in which case we fall back to resolving from the runtime dir
			var refAssembliesDir = FindHighestVersionedDirectory (
				Path.Combine (dotnetRoot, "packs", "Microsoft.NETCore.App.Ref"),
				d => File.Exists (Path.Combine (d, $"net{hostVersion.Major}.{hostVersion.Minor}", "System.Runtime.dll")),
				out _
			);



			return new RuntimeInfo (
				RuntimeKind.NetCore,
				runtimeDir: runtimeDir,
				runtimeVersion: hostVersion,
				refAssembliesDir: refAssembliesDir,
				runtimeFacadesDir: null,
				cscPath: MakeCscPath (sdkDir),
				cscMaxLangVersion: maxCSharpVersion,
				runtimeLangVersion: CSharpLangVersionHelper.FromNetCoreSdkVersion (sdkVersion)
			);
		}

		static string FindHighestVersionedDirectory (string parentFolder, Func<string, bool> validate, out SemVersion bestVersion)
		{
			string bestMatch = null;
			bestVersion = SemVersion.Zero;
			foreach (var dir in Directory.EnumerateDirectories (parentFolder)) {
				var name = Path.GetFileName (dir);
				if (SemVersion.TryParse (name, out var version) && version.Major >= 0) {
					if (version > bestVersion && (validate == null || validate (dir))) {
						bestVersion = version;
						bestMatch = dir;
					}
				}
			}
			return bestMatch;
		}
	}
}

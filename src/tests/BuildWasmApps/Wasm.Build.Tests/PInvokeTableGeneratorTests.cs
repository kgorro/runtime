// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace Wasm.Build.Tests
{
    public class PInvokeTableGeneratorTests : BuildTestBase
    {
        public PInvokeTableGeneratorTests(ITestOutputHelper output, SharedBuildPerTestClassFixture buildContext)
            : base(output, buildContext)
        {
        }

        [Theory]
        [BuildAndRun(host: RunHost.V8)]
        public void NativeLibraryWithVariadicFunctions(BuildArgs buildArgs, RunHost host, string id)
        {
            string code = @"
                using System;
                using System.Runtime.InteropServices;
                public class Test
                {
                    public static int Main(string[] args)
                    {
                        Console.WriteLine($""Main running"");
                        if (args.Length > 0)
                        {
                            // We don't want to run this, because we can't call variadic functions
                            Console.WriteLine($""sum_three: {sum_three(7, 14, 21)}"");
                            Console.WriteLine($""sum_two: {sum_two(3, 6)}"");
                            Console.WriteLine($""sum_one: {sum_one(5)}"");
                        }
                        return 42;
                    }

                    [DllImport(""variadic"", EntryPoint=""sum"")] public static extern int sum_one(int a);
                    [DllImport(""variadic"", EntryPoint=""sum"")] public static extern int sum_two(int a, int b);
                    [DllImport(""variadic"", EntryPoint=""sum"")] public static extern int sum_three(int a, int b, int c);
                }";

            (buildArgs, string output) = BuildForVariadicFunctionTests(code,
                                                          buildArgs with { ProjectName = $"variadic_{buildArgs.Config}_{id}" },
                                                          id);
            Assert.Matches("warning.*native function.*sum.*varargs", output);
            Assert.Matches("warning.*sum_(one|two|three)", output);

            output = RunAndTestWasmApp(buildArgs, buildDir: _projectDir, expectedExitCode: 42, host: host, id: id);
            Assert.Contains("Main running", output);
        }

        [Theory]
        [BuildAndRun(host: RunHost.V8)]
        public void DllImportWithFunctionPointersCompilesWithWarning(BuildArgs buildArgs, RunHost host, string id)
        {
            string code = @"
                using System;
                using System.Runtime.InteropServices;
                public class Test
                {
                    public static int Main()
                    {
                        Console.WriteLine($""Main running"");
                        return 42;
                    }

                    [DllImport(""variadic"", EntryPoint=""sum"")]
                    public unsafe static extern int using_sum_one(delegate* unmanaged<char*, IntPtr, void> callback);

                    [DllImport(""variadic"", EntryPoint=""sum"")]
                    public static extern int sum_one(int a, int b);
                }";

            (buildArgs, string output) = BuildForVariadicFunctionTests(code,
                                                          buildArgs with { ProjectName = $"fnptr_{buildArgs.Config}_{id}" },
                                                          id);
            Assert.Matches("warning.*Skipping.*because.*function pointer", output);
            Assert.Matches("warning.*using_sum_one", output);

            output = RunAndTestWasmApp(buildArgs, buildDir: _projectDir, expectedExitCode: 42, host: host, id: id);
            Assert.Contains("Main running", output);
        }

        [Theory]
        [BuildAndRun(host: RunHost.V8)]
        public void DllImportWithFunctionPointers_ForVariadicFunction_CompilesWithWarning(BuildArgs buildArgs, RunHost host, string id)
        {
            string code = @"
                using System;
                using System.Runtime.InteropServices;
                public class Test
                {
                    public static int Main()
                    {
                        Console.WriteLine($""Main running"");
                        return 42;
                    }

                    [DllImport(""variadic"", EntryPoint=""sum"")]
                    public unsafe static extern int using_sum_one(delegate* unmanaged<char*, IntPtr, void> callback);
                }";

            (buildArgs, string output) = BuildForVariadicFunctionTests(code,
                                                          buildArgs with { ProjectName = $"fnptr_variadic_{buildArgs.Config}_{id}" },
                                                          id);
            Assert.Matches("warning.*Skipping.*because.*function pointer", output);
            Assert.Matches("warning.*using_sum_one", output);

            output = RunAndTestWasmApp(buildArgs, buildDir: _projectDir, expectedExitCode: 42, host: host, id: id);
            Assert.Contains("Main running", output);
        }

        [Theory]
        [BuildAndRun(host: RunHost.V8, parameters: new object[] { "tr_TR.UTF-8" })]
        public void BuildNativeInNonEnglishCulture(BuildArgs buildArgs, string culture, RunHost host, string id)
        {
            // Check that we can generate interp tables in non-english cultures
            // Prompted by https://github.com/dotnet/runtime/issues/71149

            string code = @"
                using System;
                using System.Runtime.InteropServices;

                Console.WriteLine($""square: {square(5)}"");
                return 42;

                [DllImport(""simple"")] static extern int square(int x);
            ";

            buildArgs = ExpandBuildArgs(buildArgs,
                                        extraItems: @$"<NativeFileReference Include=""simple.c"" />",
                                        extraProperties: buildArgs.AOT
                                                            ? string.Empty
                                                            : "<WasmBuildNative>true</WasmBuildNative>");

            var extraEnvVars = new Dictionary<string, string> {
                { "LANG", culture },
                { "LC_ALL", culture },
            };

            (_, string output) = BuildProject(buildArgs,
                                        id: id,
                                        new BuildProjectOptions(
                                            InitProject: () =>
                                            {
                                                File.WriteAllText(Path.Combine(_projectDir!, "Program.cs"), code);
                                                File.Copy(Path.Combine(BuildEnvironment.TestAssetsPath, "native-libs", "simple.c"),
                                                            Path.Combine(_projectDir!, "simple.c"));
                                            },
                                            Publish: buildArgs.AOT,
                                            DotnetWasmFromRuntimePack: false,
                                            ExtraBuildEnvironmentVariables: extraEnvVars
                                            ));

            output = RunAndTestWasmApp(buildArgs,
                                       buildDir: _projectDir,
                                       expectedExitCode: 42,
                                       host: host,
                                       id: id,
                                       envVars: extraEnvVars);
            Assert.Contains("square: 25", output);
        }

        private (BuildArgs, string) BuildForVariadicFunctionTests(string programText, BuildArgs buildArgs, string id)
        {
            string filename = "variadic.o";
            buildArgs = ExpandBuildArgs(buildArgs,
                                        extraItems: $"<NativeFileReference Include=\"{filename}\" />",
                                        extraProperties: "<AllowUnsafeBlocks>true</AllowUnsafeBlocks><_WasmDevel>true</_WasmDevel>");

            (_, string output) = BuildProject(buildArgs,
                                        id: id,
                                        new BuildProjectOptions(
                                            InitProject: () =>
                                            {
                                                File.WriteAllText(Path.Combine(_projectDir!, "Program.cs"), programText);
                                                File.Copy(Path.Combine(BuildEnvironment.TestAssetsPath, "native-libs", filename),
                                                            Path.Combine(_projectDir!, filename));
                                            },
                                            Publish: buildArgs.AOT,
                                            DotnetWasmFromRuntimePack: false));

            return (buildArgs, output);
        }
    }
}

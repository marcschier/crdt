// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Crdt.Interop.Tests;

internal static class YjsHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly SemaphoreSlim InstallLock = new(1, 1);
    private static bool _installChecked;

    public static async Task<YjsResult> RunAsync(CrdtScenario scenario)
    {
        string repoRoot = FindRepoRoot();
        string harnessDir = Path.Combine(repoRoot, "interop", "yjs-harness");
        await EnsureNpmInstallAsync(harnessDir);

        string indexPath = Path.Combine(harnessDir, "index.js");
        string scenarioJson = JsonSerializer.Serialize(scenario, JsonOptions);
        ProcessResult result = await RunProcessAsync("node", Quote(indexPath), repoRoot, scenarioJson);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Yjs harness failed: {result.StandardError}");
        }

        YjsResult? yjs = JsonSerializer.Deserialize<YjsResult>(result.StandardOutput, JsonOptions);
        return yjs ?? throw new InvalidOperationException("Yjs harness returned invalid JSON.");
    }

    private static async Task EnsureNpmInstallAsync(string harnessDir)
    {
        if (Volatile.Read(ref _installChecked))
        {
            return;
        }

        await InstallLock.WaitAsync();
        try
        {
            if (_installChecked)
            {
                return;
            }

            if (!Directory.Exists(Path.Combine(harnessDir, "node_modules")))
            {
                string command = $"/c npm install --silent";
                ProcessResult result = await RunProcessAsync(GetCommandProcessor(), command, harnessDir, null);
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException($"npm install failed: {result.StandardError}");
                }
            }

            _installChecked = true;
        }
        finally
        {
            InstallLock.Release();
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string harness = Path.Combine(directory.FullName, "interop", "yjs-harness", "index.js");
            if (File.Exists(harness))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate interop\\yjs-harness\\index.js from test output.");
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        string? standardInput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (fileName.Equals("node", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Node.js is required for Yjs interop tests but was not found on PATH.",
                ex);
        }

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            process.StandardInput.Close();
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static string GetCommandProcessor() =>
        Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";

    private static string Quote(string path) => $"\"{path}\"";

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

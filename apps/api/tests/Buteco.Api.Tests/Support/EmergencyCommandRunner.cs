using System.Diagnostics;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Roda o comando de emergência das 2 etapas pelo binário do <c>apps/api</c>, como um processo à
/// parte: a mesma entrada do <c>Program.cs</c> que o <c>docker compose run … api two-factor-reset</c>
/// usa (change verificacao-2-etapas-obrigatoria, D6). Devolve o código de saída e tudo que o
/// processo escreveu.
/// </summary>
public static class EmergencyCommandRunner
{
    public sealed record Result(int ExitCode, string Output);

    public static async Task<Result> RunAsync(string connectionString, params string[] args)
    {
        var dll = typeof(Program).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(dll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(dll);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["ConnectionStrings__Postgres"] = connectionString;
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await process.WaitForExitAsync(timeout.Token);
        return new Result(process.ExitCode, await stdout + await stderr);
    }
}

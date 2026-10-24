using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CodeTwin.Chat;

// The Copilot SDK does not report the account's monthly AI credit total. GitHub's undocumented
// copilot_internal/user endpoint does, so this reader is best effort: every failure yields null and the UI hides the figure.
internal sealed record MonthlyCredits(double CreditsUsed, string? Plan, string? ResetDateUtc);

internal sealed class MonthlyCreditsReader : IDisposable
{
    private static readonly string[] TokenVariables = ["COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN"];
    private static readonly TimeSpan GhTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient http = new() { BaseAddress = new Uri("https://api.github.com/"), Timeout = TimeSpan.FromSeconds(10) };

    // The token must belong to the account the Copilot CLI is signed in with. An environment token is what the CLI
    // itself uses, so it is accepted when the CLI login is unknown; a gh token always has to match the login.
    public async Task<MonthlyCredits?> ReadAsync(string? copilotLogin, CancellationToken cancellationToken)
    {
        try
        {
            string? token = TokenVariables.Select(Environment.GetEnvironmentVariable).FirstOrDefault(value => !string.IsNullOrEmpty(value));
            bool fromVariable = token is not null;
            token ??= await ReadGhTokenAsync(cancellationToken).ConfigureAwait(false);
            if (token is null || (!fromVariable && copilotLogin is null))
            {
                return null;
            }

            using HttpRequestMessage request = new(HttpMethod.Get, "copilot_internal/user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd("CodeTwin");
            request.Headers.Accept.ParseAdd("application/json");
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                await Console.Error.WriteLineAsync($"Reading the monthly AI credits returned HTTP {(int)response.StatusCode}.").ConfigureAwait(false);
                return null;
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (copilotLogin is not null
                && !(root.TryGetProperty("login", out JsonElement login) && string.Equals(login.GetString(), copilotLogin, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            return root.TryGetProperty("quota_snapshots", out JsonElement snapshots)
                && snapshots.TryGetProperty("premium_interactions", out JsonElement premium)
                && premium.TryGetProperty("credits_used", out JsonElement used)
                && used.TryGetDouble(out double credits)
                ? new MonthlyCredits(credits, ReadString(root, "copilot_plan"), ReadString(root, "quota_reset_date_utc"))
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            await Console.Error.WriteLineAsync($"Reading the monthly AI credits failed: {exception.Message}").ConfigureAwait(false);
            return null;
        }
    }

    public void Dispose() => http.Dispose();

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<string?> ReadGhTokenAsync(CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new("gh", "auth token --hostname github.com")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("gh did not start.");
        }
        catch (Win32Exception)
        {
            return null;
        }

        using (process)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(GhTimeout);
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                _ = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                string token = (await output.ConfigureAwait(false)).Trim();
                return process.ExitCode == 0 && token.Length > 0 ? token : null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                await Console.Error.WriteLineAsync("gh auth token timed out.").ConfigureAwait(false);
                return null;
            }
        }
    }
}

namespace CodeTwin.Chat;

/// <summary>One AI provider behind the chat window. Every host sends the same JSON lines to the chat UI.</summary>
internal interface IChatHost : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    Task PublishModelsAsync(CancellationToken cancellationToken);

    Task PublishQuotaAsync(CancellationToken cancellationToken);

    Task<bool> PublishAuthStatusAsync(CancellationToken cancellationToken);

    Task<bool> SignInAsync(CancellationToken cancellationToken);

    Task SetModelAsync(string? id, string? effort, CancellationToken cancellationToken);

    Task SetModeAsync(string? name, CancellationToken cancellationToken);

    void SetToolPermissions(string? name);

    Task SendAsync(string prompt, string? solution, string? activeElement, CancellationToken cancellationToken);

    Task AbortAsync(CancellationToken cancellationToken);

    Task NewChatAsync(bool isPrivate);

    Task PublishSessionsAsync(string? solution, CancellationToken cancellationToken);

    Task ResumeAsync(string id, string? solution, CancellationToken cancellationToken);

    Task DeleteAsync(string id, CancellationToken cancellationToken);

    void ResolvePermission(string id, bool approved);

    void ResolvePlan(string id, bool approved, string? action, string? feedback);
}

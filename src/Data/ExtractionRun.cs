namespace MenuToOrder.Data;

public sealed class ExtractionRun
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid MenuVersionId { get; private set; }
    public int Pass { get; private set; }
    public string PromptVersion { get; private set; }
    public string Model { get; private set; }
    public string InputHash { get; private set; }
    // text, not jsonb: jsonb reorders keys and drops whitespace, and this has to stay verbatim.
    public string? RawResponse { get; private set; }
    public string? Error { get; private set; }
    public TimeSpan Duration { get; private set; }
    public int? InputTokens { get; private set; }
    public int? OutputTokens { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }

    public ExtractionRun(
        Guid menuVersionId, int pass, string promptVersion, string model, string inputHash,
        string? rawResponse, string? error, TimeSpan duration,
        int? inputTokens, int? outputTokens, DateTimeOffset startedAt)
    {
        if (pass is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(pass), pass, "A menu is read in exactly two passes.");
        if (rawResponse is null && error is null)
            throw new ArgumentException("A run without a response must record why.", nameof(error));

        MenuVersionId = menuVersionId;
        Pass = pass;
        PromptVersion = promptVersion;
        Model = model;
        InputHash = inputHash;
        RawResponse = rawResponse;
        Error = error;
        Duration = duration;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        StartedAt = startedAt;
    }
}

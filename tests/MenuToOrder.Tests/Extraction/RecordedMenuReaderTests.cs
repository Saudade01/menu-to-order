using MenuToOrder.Extraction;

namespace MenuToOrder.Tests.Extraction;

public class RecordedMenuReaderTests
{
    private readonly RecordedMenuReader _reader = new(Repo.At("samples", "recorded"), TimeProvider.System);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_recorded_sample_replays_and_says_it_is_a_replay()
    {
        var image = new MenuImage(File.ReadAllBytes(Repo.At("samples", "menus", "sahil-kafe.jpg")), "image/jpeg");

        var read = await _reader.ReadAsync(image, 1, Ct);

        Assert.Null(read.Error);
        Assert.Equal("gemini-3.8-flash (recorded)", read.Model);
        Assert.Equal("sections-v2", read.PromptVersion);
        Assert.Equal(11, read.Menu!.Items.Count);
    }

    [Fact]
    public async Task An_unknown_photo_fails_clearly_instead_of_returning_an_empty_menu()
    {
        var read = await _reader.ReadAsync(new MenuImage([1, 2, 3], "image/jpeg"), 2, Ct);

        Assert.Null(read.Menu);
        Assert.Equal("No recorded response for this image under prompt prices-first-v2.", read.Error);
    }
}

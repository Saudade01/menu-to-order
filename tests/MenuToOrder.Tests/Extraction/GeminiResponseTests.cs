using MenuToOrder.Extraction;

namespace MenuToOrder.Tests.Extraction;

public class GeminiResponseTests
{
    public static TheoryData<string> Recordings =>
        new(Directory.GetFiles(Repo.At("samples", "recorded"), "*.json").Select(Path.GetFileName)!);

    [Theory]
    [MemberData(nameof(Recordings))]
    public void Every_recorded_response_parses_into_a_menu(string file)
    {
        var reply = GeminiResponse.Parse(File.ReadAllText(Repo.At("samples", "recorded", file)));

        Assert.Null(reply.Error);
        Assert.NotEmpty(reply.Menu!.Items);
        Assert.Equal("gemini-3.8-flash", reply.ModelVersion);
        Assert.True(reply.InputTokens > 0 && reply.OutputTokens > 0);
    }

    [Fact]
    public void The_clean_sample_is_read_dish_for_dish()
    {
        var menu = Recorded("kose-lokantasi", pass: 1);

        Assert.Equal(["ÇORBALAR", "ANA YEMEKLER", "TATLILAR", "İÇECEKLER"], menu.Categories);
        Assert.Equal(11, menu.Items.Count);
        Assert.Contains(menu.Items, i => i is { Name: "Kuru Fasulye", PriceText: "180 ₺", Description: "Tereyağlı, pilav ile" });
        Assert.All(menu.Items, i => Assert.Empty(i.Unclear));
    }

    [Fact]
    public void Blurred_prices_come_back_marked_unclear()
    {
        var mezeler = Recorded("mangal-evi", pass: 1).Items.Where(i => i.Category == "MEZELER").ToList();

        Assert.Equal(3, mezeler.Count);
        Assert.All(mezeler, i => Assert.Contains("price", i.Unclear));
    }

    [Fact]
    public void An_api_error_keeps_its_message()
    {
        var reply = GeminiResponse.Parse(File.ReadAllText(Repo.At("tests", "fixtures", "gemini-503.json")));

        Assert.Null(reply.Menu);
        Assert.StartsWith("This model is currently experiencing high demand", reply.Error);
    }

    [Fact]
    public void A_cut_off_generation_is_an_error_not_a_partial_menu()
    {
        const string raw = """
            {"candidates":[{"content":{"parts":[{"text":"{\"categories\":[\"ÇORBALAR\"],\"items\":[{\"name\":\"Merc"}]},
            "finishReason":"MAX_TOKENS"}],"modelVersion":"gemini-3.8-flash"}
            """;

        var reply = GeminiResponse.Parse(raw);

        Assert.Null(reply.Menu);
        Assert.Contains("MAX_TOKENS", reply.Error);
    }

    [Theory]
    [InlineData("""{"candidates":[{"content":{"role":"model"},"finishReason":"STOP"}]}""")]
    [InlineData("""{"candidates":[{"finishReason":"STOP"}]}""")]
    public void A_finished_answer_without_text_is_an_error_not_a_crash(string raw)
    {
        var reply = GeminiResponse.Parse(raw);

        Assert.Null(reply.Menu);
        Assert.Equal(GeminiResponse.NoText, reply.Error);
    }

    [Fact]
    public void Output_missing_a_required_field_is_rejected()
    {
        const string raw = """
            {"candidates":[{"content":{"parts":[{"text":"{\"categories\":[],\"items\":[{\"name\":\"Ayran\",\"price\":\"35 ₺\"}]}"}]},
            "finishReason":"STOP"}]}
            """;

        Assert.Contains("does not match the menu schema", GeminiResponse.Parse(raw).Error);
    }

    [Fact]
    public void A_body_that_is_not_json_is_reported_as_such() =>
        Assert.Equal("The response is not JSON.", GeminiResponse.Parse("<html>502 Bad Gateway</html>").Error);

    private static ExtractedMenu Recorded(string sample, int pass)
    {
        var image = new MenuImage(File.ReadAllBytes(Repo.At("samples", "menus", $"{sample}.jpg")), "image/jpeg");
        var raw = File.ReadAllText(Repo.At("samples", "recorded", RecordedMenuReader.FileName(image, Prompts.For(pass))));
        return GeminiResponse.Parse(raw).Menu!;
    }
}

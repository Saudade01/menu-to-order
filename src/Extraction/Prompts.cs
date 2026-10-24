using System.Text.Json;

namespace MenuToOrder.Extraction;

public sealed record Prompt(string Version, string Text);

public static class Prompts
{
    public static Prompt For(int pass) => pass switch
    {
        1 => BySection,
        2 => ByPrice,
        _ => throw new ArgumentOutOfRangeException(nameof(pass), pass, "A menu is read in exactly two passes."),
    };

    private const string Fields = """
        For every dish, return:
        - name: the dish name exactly as printed
        - category: the section heading it sits under, or null if there is none
        - description: the smaller text printed with the dish, or null
        - price: the price exactly as printed, including any currency sign or letters such as ₺ or TL.
          Use null if no price is printed or it cannot be read.
        - unclear: the fields you could not read with confidence because of blur, glare,
          a cut-off edge or handwriting. Leave it empty when everything was clear.

        Also return every section heading in `categories`, in menu order.

        Copy what is printed. Do not translate, fix spelling, convert prices or fill in a missing price.
        """;

    // The passes read in different orders on purpose: where they disagree is where the photo is ambiguous.
    private static readonly Prompt BySection = new("sections-v1", $"""
        This is a photo of a printed restaurant menu, usually in Turkish.

        Read it section by section, top to bottom.

        {Fields}
        """);

    private static readonly Prompt ByPrice = new("prices-first-v1", $"""
        This is a photo of a printed restaurant menu, usually in Turkish.

        Start from the prices. Find every price printed on the menu and follow its line to the dish
        it belongs to. Then add any dish that has no price next to it.

        {Fields}
        """);

    internal static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "categories": { "type": "array", "items": { "type": "string" } },
            "items": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "category": { "type": "string", "nullable": true },
                  "description": { "type": "string", "nullable": true },
                  "price": { "type": "string", "nullable": true },
                  "unclear": {
                    "type": "array",
                    "items": { "type": "string", "enum": ["name", "category", "description", "price"] }
                  }
                },
                "required": ["name", "category", "description", "price", "unclear"],
                "propertyOrdering": ["name", "category", "description", "price", "unclear"]
              }
            }
          },
          "required": ["categories", "items"],
          "propertyOrdering": ["categories", "items"]
        }
        """).RootElement.Clone();
}

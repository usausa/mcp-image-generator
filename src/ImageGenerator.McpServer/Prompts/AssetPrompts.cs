namespace ImageGenerator.McpServer.Prompts;

using ModelContextProtocol.Server;

// Prompt templates for common application assets. The composition rules follow the
// prompts that produced the existing MAUI template assets.
[McpServerPromptType]
public static class AssetPrompts
{
    private const string SubjectDescription = "What to draw, e.g. 'a smiling orange cat face' or 'a modern mechanical keyboard with blue accent keys'.";
    private const string StyleDescription = "Illustration style: anime (default), anime-cinematic, flat-vector, watercolor-cat, pixel or voxel.";
    private const string PaletteDescription = "Optional colour palette, e.g. 'deep indigo night with warm lantern orange'.";

    private const string StyleAnime = "anime";
    private const string StyleAnimeCinematic = "anime-cinematic";
    private const string StyleFlatVector = "flat-vector";
    private const string StyleWatercolorCat = "watercolor-cat";
    private const string StylePixel = "pixel";
    private const string StyleVoxel = "voxel";

    //--------------------------------------------------------------------------------
    // Prompts
    //--------------------------------------------------------------------------------

    [McpServerPrompt(Name = "app_icon", Title = "App icon")]
    [Description("Prompt for an app or launcher icon: centered subject, simple silhouette, generous margin, transparent background. Generate at 1024x1024, then export sizes with export_image_sizes.")]
    public static string AppIcon(
        [Description(SubjectDescription)] string subject,
        [Description(StyleDescription)] [AllowedValues(StyleAnime, StyleAnimeCinematic, StyleFlatVector, StyleWatercolorCat, StylePixel, StyleVoxel)] string? style = null,
        [Description(PaletteDescription)] string? palette = null) =>
        Build(
            style ?? StyleFlatVector,
            $"App icon of {subject}, centered, simple readable silhouette, generous margin around the subject, plain flat background.",
            palette,
            "quality=high, width=1024, height=1024, background=transparent, outputFormat=png");

    [McpServerPrompt(Name = "avatar", Title = "Avatar")]
    [Description("Prompt for a user or character avatar: front facing, head and shoulders, plain background. Generate at 1024x1024 and set width/height to the final size (e.g. 256).")]
    public static string Avatar(
        [Description(SubjectDescription)] string subject,
        [Description(StyleDescription)] [AllowedValues(StyleAnime, StyleAnimeCinematic, StyleFlatVector, StyleWatercolorCat, StylePixel, StyleVoxel)] string? style = null,
        [Description(PaletteDescription)] string? palette = null) =>
        Build(
            style ?? StyleAnime,
            $"Avatar portrait of {subject}, front facing, head and shoulders only, centered, generous margin around the head, plain background.",
            palette,
            "quality=medium, width=256, height=256");

    [McpServerPrompt(Name = "product_item", Title = "Product item")]
    [Description("Prompt for a product or item illustration on a plain white background with a soft drop shadow, e.g. for shop cards. Generate at 1024x1024 and set width/height to the final size (e.g. 800).")]
    public static string ProductItem(
        [Description(SubjectDescription)] string subject,
        [Description(StyleDescription)] [AllowedValues(StyleAnime, StyleAnimeCinematic, StyleFlatVector, StyleWatercolorCat, StylePixel, StyleVoxel)] string? style = null,
        [Description(PaletteDescription)] string? palette = null) =>
        Build(
            style ?? StyleAnime,
            $"Item illustration of {subject}, shown at a slight three quarter angle from above, floating on a plain white background, soft drop shadow, centered.",
            palette,
            "quality=high, width=800, height=800");

    [McpServerPrompt(Name = "poster", Title = "Poster")]
    [Description("Prompt for a vertical key visual poster (2:3) with empty space at the top and bottom for titles. Generate at 1024x1536 and set width/height to the final size (e.g. 600x900).")]
    public static string Poster(
        [Description(SubjectDescription)] string subject,
        [Description(StyleDescription)] [AllowedValues(StyleAnime, StyleAnimeCinematic, StyleFlatVector, StyleWatercolorCat, StylePixel, StyleVoxel)] string? style = null,
        [Description(PaletteDescription)] string? palette = null) =>
        Build(
            style ?? StyleAnimeCinematic,
            $"Vertical key visual poster for {subject}. Dramatic composition with the subject centered, empty space at the top and bottom for titles.",
            palette,
            "quality=high, size=1024x1536, width=600, height=900");

    [McpServerPrompt(Name = "banner", Title = "Banner")]
    [Description("Prompt for a wide promotional banner (2:1) with one half kept open for a headline. Generate at 1536x1024 and set width/height to the final size (e.g. 1200x600).")]
    public static string Banner(
        [Description(SubjectDescription)] string subject,
        [Description(StyleDescription)] [AllowedValues(StyleAnime, StyleAnimeCinematic, StyleFlatVector, StyleWatercolorCat, StylePixel, StyleVoxel)] string? style = null,
        [Description(PaletteDescription)] string? palette = null) =>
        Build(
            style ?? StyleAnimeCinematic,
            $"Wide promotional banner illustration for {subject}. The subject placed on the right side, the opposite half kept open and uncluttered so a headline can be overlaid.",
            palette,
            "quality=high, size=1536x1024, width=1200, height=600");

    [McpServerPrompt(Name = "hero_visual", Title = "Hero visual")]
    [Description("Prompt for a wide hero or key visual (16:9) with the right half kept open for a title overlay. Generate at 1536x1024 and set width/height to the final size (e.g. 1600x900).")]
    public static string HeroVisual(
        [Description(SubjectDescription)] string subject,
        [Description(StyleDescription)] [AllowedValues(StyleAnime, StyleAnimeCinematic, StyleFlatVector, StyleWatercolorCat, StylePixel, StyleVoxel)] string? style = null,
        [Description(PaletteDescription)] string? palette = null) =>
        Build(
            style ?? StyleAnime,
            $"Wide key visual for {subject}. The right half of the frame kept open and uncluttered for a title overlay.",
            palette,
            "quality=high, size=1536x1024, width=1600, height=900");

    [McpServerPrompt(Name = "onboarding", Title = "Onboarding illustration")]
    [Description("Prompt for a square onboarding page illustration: friendly, simple readable shapes, generous margins. Generate at 1024x1024 and set width/height to the final size (e.g. 1080).")]
    public static string Onboarding(
        [Description(SubjectDescription)] string subject,
        [Description(StyleDescription)] [AllowedValues(StyleAnime, StyleAnimeCinematic, StyleFlatVector, StyleWatercolorCat, StylePixel, StyleVoxel)] string? style = null,
        [Description(PaletteDescription)] string? palette = null) =>
        Build(
            style ?? StyleAnime,
            $"Square illustration for an app onboarding page, {subject}. Friendly and inviting, simple readable shapes, the subject centered with generous empty margin on all four sides.",
            palette,
            "quality=medium, width=1080, height=1080");

    [McpServerPrompt(Name = "scene", Title = "Scene")]
    [Description("Prompt for a wide cinematic scene (16:9), e.g. a video thumbnail or background. Generate at 1536x1024 and set width/height to the final size (e.g. 1280x720).")]
    public static string Scene(
        [Description(SubjectDescription)] string subject,
        [Description(StyleDescription)] [AllowedValues(StyleAnime, StyleAnimeCinematic, StyleFlatVector, StyleWatercolorCat, StylePixel, StyleVoxel)] string? style = null,
        [Description(PaletteDescription)] string? palette = null) =>
        Build(
            style ?? StyleAnimeCinematic,
            $"Wide scene, {subject}. Cinematic framing.",
            palette,
            "quality=high, size=1536x1024, width=1280, height=720");

    //--------------------------------------------------------------------------------
    // Build
    //--------------------------------------------------------------------------------

    private static string Build(string style, string composition, string? palette, string suggestedArguments)
    {
        var prefix = StylePrefix(style);
        var paletteText = String.IsNullOrWhiteSpace(palette) ? string.Empty : $" Colour palette: {palette.Trim()}.";
        var prompt = $"{prefix} {composition}{paletteText}";

        return
            "Generate the asset with the generate_image tool (or edit_image when a reference image should be kept).\n" +
            $"Suggested arguments: {suggestedArguments}, outputPath=<target file in the project>, overwrite=true.\n" +
            "Prompt:\n" +
            prompt;
    }

    private static string StylePrefix(string style) => style.Trim().ToUpperInvariant() switch
    {
        "ANIME-CINEMATIC" => "Japanese anime illustration style, clean lineart, soft cel shading, high detail, cinematic lighting, no text, no watermark, no signature, no logos, no lettering.",
        "FLAT-VECTOR" => "Flat vector mascot icon style, bold simple shapes, thick clean outlines, limited palette, no text, no watermark, no signature.",
        "WATERCOLOR-CAT" => "Soft watercolour illustration in the style of gentle Japanese kitten character art, thin sketchy outlines, muted natural fur colours, plain flat white background, no sparkles, no jewellery, no accessories, no text.",
        "PIXEL" => "16-bit pixel art style, crisp pixels, limited palette, no text, no watermark, no signature.",
        "VOXEL" => "Blocky voxel style like a cube based sandbox video game, built from visible large voxels, simple flat shading, soft ambient occlusion, no text, no watermark, no signature.",
        _ => "Japanese anime illustration style, clean lineart, soft cel shading, bright and airy colours with blue and gold accents, high detail, no text, no watermark, no signature."
    };
}

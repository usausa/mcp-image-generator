namespace ImageGenerator.McpServer.Models;

public enum FitMode
{
    // Center crop to the target aspect ratio, then scale to the target size
    Cover,

    // Scale to fit inside the target without padding (the result can be smaller)
    Contain,

    // Scale to fit inside the target and pad to the exact size
    Pad,

    // Stretch to the target size ignoring the aspect ratio (resize_image only)
    Stretch
}

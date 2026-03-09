using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace Mcp.Proxy.Server.Tools;

public class BlackforestLabWrapper(IBflApiClient bfl)
{
    [McpServerTool]
    [Description("""
        Generate an image using a Black Forest Labs FLUX model. Returns the URL of the generated image.
        Choose model by use-case:
          flux-2-pro-preview  — latest FLUX.2 [pro], best overall quality (default, recommended)
          flux-2-flex         — FLUX.2 [flex], customizable generation and editing
          flux-kontext-pro    — FLUX.1 Kontext [pro], context-aware generation and editing
          flux-kontext-max    — FLUX.1 Kontext [max], advanced context editing
          flux-pro-1.1-ultra  — FLUX1.1 [pro] Ultra, maximum quality up to 4MP
          flux-pro-1.1        — FLUX1.1 [pro], fast high-quality generation
""")]
    public async Task<string> GenerateImage(
        [Description("""
            Text prompt following the Subject + Action + Style + Context framework.
            Word order matters: front-load the most important elements — FLUX weighs earlier words more.
            Ideal length: 30-80 words. For complex scenes 80+ words is fine.
            Use positive language — describe what should appear, not what to exclude (e.g. "peaceful solitude" not "no crowds").
            Build in layers: (1) subject + action, (2) lighting + color + composition, (3) camera/lens details, (4) mood + atmosphere.
            For text in the image: use quotation marks, specify placement, and describe the typographic style.
            Example: "Red fox sitting in tall grass, wildlife documentary photography, golden hour, 85mm f/2.8, misty dawn atmosphere"
            """)] string prompt,
        [Description("Model to use. See tool description for guidance. Default: flux-2-pro-preview")] string model = "flux-2-pro-preview",
        [Description("Output format: jpeg (smaller, faster) or png (lossless). Default: jpeg")] string outputFormat = "jpeg",
        [Description("Safety tolerance 0-6: 0-2 = strict filtering, 3-4 = balanced, 5-6 = permissive. Default: 6")] int safetyTolerance = 6,
        [Description("Seed for reproducible results. Same seed + prompt = same image.")] int? seed = null,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["prompt"] = prompt,
            ["output_format"] = outputFormat,
            ["safety_tolerance"] = safetyTolerance,
        };
        if (seed.HasValue) body["seed"] = seed.Value;

        var id = await bfl.SubmitJobAsync($"/v1/{model}", body, ct);
        return await PollForResult(id, ct);
    }

    [McpServerTool]
    [Description("Inpaint or fill a region of an existing image using FLUX Fill (flux-pro-1.0-fill). Without a mask the model fills the entire image guided by the prompt. Returns the URL of the result.")]
    public async Task<string> FillImage(
        [Description("""
            Text prompt for the filled region. Use the same Subject → Style → Context framework as GenerateImage.
            Match the style of the surrounding image for seamless blending.
            """)] string prompt,
        [Description("Base64-encoded input image (JPEG or PNG).")] string imageBase64,
        [Description("Base64-encoded mask image. White pixels = area to fill, black pixels = keep unchanged. Omit to restyle the whole image.")] string? maskBase64 = null,
        [Description("Guidance strength (1.5-100). Low (5-15) = preserves original style, high (30-70) = follows prompt closely. Default: 30")] float guidance = 30f,
        [Description("Diffusion steps (1-50). More steps = higher quality but slower. 20-30 is a good balance. Default: 28")] int steps = 28,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["prompt"] = prompt,
            ["image"] = imageBase64,
            ["guidance"] = guidance,
            ["steps"] = steps,
        };
        if (maskBase64 is not null) body["mask"] = maskBase64;

        var id = await bfl.SubmitJobAsync("/v1/flux-pro-1.0-fill", body, ct);
        return await PollForResult(id, ct);
    }

    [McpServerTool]
    [Description("Get the current BFL credit balance for the configured API key.")]
    public async Task<string> GetBflCredits(CancellationToken ct = default) =>
        await bfl.GetCreditsAsync(ct);

    // ---

    private async Task<string> PollForResult(string id, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(280);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);

            var json = await bfl.GetResultAsync(id, ct);
            var status = json["status"]!.GetValue<string>();

            switch (status)
            {
                case "Ready":
                    return json["result"]!.ToString();
                case "Error":
                    return $"Generation failed: {json}";
                case "Content Moderated":
                case "Request Moderated":
                    return $"Request moderated ({status}).";
            }
            // "Pending" — keep polling
        }

        return "Timed out waiting for generation result.";
    }
}

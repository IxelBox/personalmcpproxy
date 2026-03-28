using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace Mcp.Proxy.Server.Tools;

public class BlackforestLabWrapper(IBflApiClient bfl, ImageStore imageStore, IHttpContextAccessor httpContextAccessor, IConfiguration config)
{
    [McpServerTool]
    [Description("""
        Generate an image using a Black Forest Labs FLUX model. Returns a URL to the generated image served from this server (expires in 30 minutes).
        Choose model by use-case:
          flux-2-pro-preview      - latest FLUX.2 [pro], best overall quality (default, recommended); ~2x faster as of Mar 2026
          flux-2-pro              - pinned/stable FLUX.2 [pro] snapshot
          flux-2-max              - FLUX.2 top tier; incorporates web knowledge/grounding, up to 10 reference images
          flux-2-flex             - FLUX.2 [flex], exposes steps and guidance for fine control
          flux-2-klein-9b-preview - FLUX.2 Klein rolling latest, sub-second inference, very cheap
          flux-2-klein-9b         - FLUX.2 Klein 9B pinned, fast and cheap
          flux-2-klein-4b         - FLUX.2 Klein 4B, fastest and cheapest ($0.014/image)
          flux-kontext-pro        - FLUX.1 Kontext [pro], context-aware text-to-image and editing
          flux-kontext-max        - FLUX.1 Kontext [max], advanced editing with stricter prompt adherence and better typography
          flux-pro-1.1-ultra      - FLUX1.1 [pro] Ultra, maximum quality up to 4MP
          flux-pro-1.1            - FLUX1.1 [pro], fast high-quality generation
        Width/height apply to FLUX.2 models (must be multiples of 16, up to 4MP total).
        AspectRatio applies to Kontext models (e.g. "16:9", "1:1", "4:3"; range 3:7 to 7:3).
""")]
    public async Task<string> GenerateImage(
        [Description("""
            Text prompt following the Subject + Action + Style + Context framework.
            Word order matters: front-load the most important elements - FLUX weighs earlier words more.
            Ideal length: 30-80 words. For complex scenes 80+ words is fine.
            Use positive language - describe what should appear, not what to exclude (e.g. "peaceful solitude" not "no crowds").
            Build in layers: (1) subject + action, (2) lighting + color + composition, (3) camera/lens details, (4) mood + atmosphere.
            For text in the image: use quotation marks, specify placement, and describe the typographic style.
            Example: "Red fox sitting in tall grass, wildlife documentary photography, golden hour, 85mm f/2.8, misty dawn atmosphere"
            """)] string prompt,
        [Description("Model to use. See tool description for guidance. Default: flux-2-pro-preview")] string model = "flux-2-pro-preview",
        [Description("Output format: jpeg (smaller, faster) or png (lossless). Default: jpeg")] string outputFormat = "jpeg",
        [Description("Safety tolerance 0-6: 0-2 = strict filtering, 3-4 = balanced, 5-6 = permissive. Default: 5")] int safetyTolerance = 5,
        [Description("Seed for reproducible results. Same seed + prompt = same image.")] int? seed = null,
        [Description("Image width in pixels. FLUX.2 models only. Must be a multiple of 16. Combined with height must not exceed 4MP (e.g. 2048x2048). Omit for model default.")] int? width = null,
        [Description("Image height in pixels. FLUX.2 models only. Must be a multiple of 16. Combined with width must not exceed 4MP. Omit for model default.")] int? height = null,
        [Description("Aspect ratio for Kontext models (e.g. \"16:9\", \"1:1\", \"4:3\", \"9:16\"). Range: 3:7 to 7:3. Omit for model default (1:1).")] string? aspectRatio = null,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["prompt"] = prompt,
            ["output_format"] = outputFormat,
            ["safety_tolerance"] = safetyTolerance,
        };
        if (seed.HasValue) body["seed"] = seed.Value;
        if (width.HasValue) body["width"] = width.Value;
        if (height.HasValue) body["height"] = height.Value;
        if (aspectRatio is not null) body["aspect_ratio"] = aspectRatio;

        var id = await bfl.SubmitJobAsync($"/v1/{model}", body, ct);
        var imageUrl = await PollForResultUrl(id, ct);
        var bytes = await bfl.DownloadImageAsync(imageUrl, ct);
        return BuildImageUrl(imageStore.Store(bytes, $"image/{outputFormat}", ImageTtl));
    }

    [McpServerTool]
    [Description("""
        Edit or restyle an existing image using FLUX.2 or FLUX.1 Kontext models. Provide an input image and a prompt describing the desired changes.
        Returns a URL to the result image served from this server (expires in 30 minutes).
        Choose model by use-case:
          flux-2-pro-preview  - best quality editing, handles complex scene changes (default)
          flux-2-max          - top tier; best for multi-reference editing (up to 8 reference images)
          flux-2-flex         - fine-grained control via steps/guidance
          flux-kontext-pro    - natural-language editing, great for targeted changes (e.g. "change shirt to red")
          flux-kontext-max    - stricter prompt adherence, better for text/typography changes
        Width/height apply to FLUX.2 models (multiples of 16, up to 4MP).
        AspectRatio applies to Kontext models (e.g. "16:9", "1:1").
""")]
    public async Task<string> EditImage(
        [Description("""
            Text prompt describing the desired edit or output. Be specific about what to change.
            For targeted edits: "Change the car color to red" or "Replace the background with a forest".
            For style transfer: "Render in watercolor style, keep composition".
            """)] string prompt,
        [Description("Base64-encoded input image (JPEG or PNG) to edit or use as reference.")] string imageBase64,
        [Description("Model to use. See tool description for guidance. Default: flux-2-pro-preview")] string model = "flux-2-pro-preview",
        [Description("Output format: jpeg or png. Default: jpeg")] string outputFormat = "jpeg",
        [Description("Safety tolerance 0-6. Default: 5")] int safetyTolerance = 5,
        [Description("Seed for reproducible results.")] int? seed = null,
        [Description("Image width in pixels. FLUX.2 models only. Must be multiple of 16, up to 4MP total. Omit for model default.")] int? width = null,
        [Description("Image height in pixels. FLUX.2 models only. Must be multiple of 16, up to 4MP total. Omit for model default.")] int? height = null,
        [Description("Aspect ratio for Kontext models (e.g. \"16:9\", \"1:1\"). Omit for model default.")] string? aspectRatio = null,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["prompt"] = prompt,
            ["input_image"] = imageBase64,
            ["output_format"] = outputFormat,
            ["safety_tolerance"] = safetyTolerance,
        };
        if (seed.HasValue) body["seed"] = seed.Value;
        if (width.HasValue) body["width"] = width.Value;
        if (height.HasValue) body["height"] = height.Value;
        if (aspectRatio is not null) body["aspect_ratio"] = aspectRatio;

        var id = await bfl.SubmitJobAsync($"/v1/{model}", body, ct);
        var imageUrl = await PollForResultUrl(id, ct);
        var bytes = await bfl.DownloadImageAsync(imageUrl, ct);
        return BuildImageUrl(imageStore.Store(bytes, $"image/{outputFormat}", ImageTtl));
    }

    [McpServerTool]
    [Description("Inpaint or fill a region of an existing image using FLUX Fill (flux-pro-1.0-fill). Without a mask the model fills the entire image guided by the prompt. Returns a URL to the result image served from this server (expires in 30 minutes).")]
    public async Task<string> FillImage(
        [Description("""
            Text prompt for the filled region. Use the same Subject - Style - Context framework as GenerateImage.
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
        var imageUrl = await PollForResultUrl(id, ct);
        var bytes = await bfl.DownloadImageAsync(imageUrl, ct);
        return BuildImageUrl(imageStore.Store(bytes, "image/jpeg", ImageTtl));
    }

    [McpServerTool]
    [Description("Get the current BFL credit balance for the configured API key.")]
    public async Task<string> GetBflCredits(CancellationToken ct = default) =>
        await bfl.GetCreditsAsync(ct);

    // ---

    private TimeSpan ImageTtl =>
        TimeSpan.FromMinutes(config.GetValue<int>("McpServer:ImageTtlMinutes", 60));

    private string BuildImageUrl(string imageId)
    {
        var req = httpContextAccessor.HttpContext!.Request;
        return $"{req.Scheme}://{req.Host}/images/{imageId}";
    }

    private async Task<string> PollForResultUrl(string id, CancellationToken ct)
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
                    var url = json["result"]?["sample"]?.GetValue<string>()
                        ?? throw new InvalidOperationException($"BFL result missing 'sample': {json}");
                    return url;
                case "Error":
                    throw new InvalidOperationException($"Generation failed: {json}");
                case "Content Moderated":
                case "Request Moderated":
                    throw new InvalidOperationException($"Request moderated ({status}).");
            }
            // "Pending" -- keep polling
        }

        throw new TimeoutException("Timed out waiting for generation result.");
    }
}

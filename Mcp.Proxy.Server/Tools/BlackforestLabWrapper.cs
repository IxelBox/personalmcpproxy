using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace Mcp.Proxy.Server.Tools;

public class BlackforestLabWrapper(IHttpClientFactory factory)
{
    private readonly HttpClient http = factory.CreateClient("bfl");

    [McpServerTool]
    [Description("""
        Generate an image using a Black Forest Labs FLUX model. Returns the URL of the generated image.
        Choose model by use-case:
          flux-2-pro-preview  — best overall quality, latest generation (default)
          flux-2-max          — maximum detail and resolution
          flux-pro-1.1-ultra  — ultra-high resolution, photorealism
          flux-pro-1.1        — fast, high quality
          flux-dev            — open model, good for experimentation
        """)]
    public async Task<string> GenerateImage(
        [Description("""
            Text prompt using Subject → Style → Context framework. Front-load the most important elements.
            Ideal length: 30–80 words. Use positive language — describe what you want, not what to avoid.
            Build progressively: core subject first, then visual style, then technical details, then atmosphere.
            Example: "Red fox sitting in tall grass, wildlife documentary photography, misty dawn, shallow depth of field"
            """)] string prompt,
        [Description("Model to use. See tool description for guidance. Default: flux-2-pro-preview")] string model = "flux-2-pro-preview",
        [Description("Output format: jpeg (smaller, faster) or png (lossless). Default: jpeg")] string outputFormat = "jpeg",
        [Description("Safety tolerance 0–6: 0–2 = strict filtering, 3–4 = balanced, 5–6 = permissive. Default: 2")] int safetyTolerance = 2,
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

        var submit = await http.PostAsync($"/v1/{model}", ToJsonContent(body), ct);
        submit.EnsureSuccessStatusCode();

        var submitJson = JsonNode.Parse(await submit.Content.ReadAsStringAsync(ct))!;
        var id = submitJson["id"]!.GetValue<string>();

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
        [Description("Guidance strength (1.5–100). Low (5–15) = preserves original style, high (30–70) = follows prompt closely. Default: 30")] float guidance = 30f,
        [Description("Diffusion steps (1–50). More steps = higher quality but slower. 20–30 is a good balance. Default: 28")] int steps = 28,
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

        var submit = await http.PostAsync("/v1/flux-pro-1.0-fill", ToJsonContent(body), ct);
        submit.EnsureSuccessStatusCode();

        var submitJson = JsonNode.Parse(await submit.Content.ReadAsStringAsync(ct))!;
        var id = submitJson["id"]!.GetValue<string>();

        return await PollForResult(id, ct);
    }

    [McpServerTool]
    [Description("Get the current BFL credit balance for the configured API key.")]
    public async Task<string> GetBflCredits(CancellationToken ct = default)
    {
        var response = await http.GetAsync("/v1/credits", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    // ---

    private async Task<string> PollForResult(string id, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(280);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);

            var poll = await http.GetAsync($"/v1/get_result?id={id}", ct);
            poll.EnsureSuccessStatusCode();

            var json = JsonNode.Parse(await poll.Content.ReadAsStringAsync(ct))!;
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

    private static StringContent ToJsonContent(JsonObject obj) =>
        new(obj.ToJsonString(), Encoding.UTF8, "application/json");
}

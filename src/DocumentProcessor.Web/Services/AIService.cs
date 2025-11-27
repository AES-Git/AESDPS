using System.Text;
using System.Text.Json;
using System.Globalization;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using DocumentProcessor.Web.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using CsvHelper;
using CsvHelper.Configuration;

namespace DocumentProcessor.Web.Services;

public class AIService(ILogger<AIService> logger, IConfiguration configuration)
{
    private readonly IAmazonBedrockRuntime _bedrockClient = InitializeBedrockClient(configuration);
    private const int MaxContentLength = 50000;

    private static IAmazonBedrockRuntime InitializeBedrockClient(IConfiguration configuration)
    {
        var region = configuration["Bedrock:Region"] ?? "us-west-2";
        var config = new AmazonBedrockRuntimeConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(region) };
        var awsProfile = configuration["Bedrock:AwsProfile"];
        if (!string.IsNullOrEmpty(awsProfile))
        {
            var credentialFile = new Amazon.Runtime.CredentialManagement.SharedCredentialsFile();
            if (credentialFile.TryGetProfile(awsProfile, out var profile))
                return new AmazonBedrockRuntimeClient(profile.GetAWSCredentials(credentialFile), config);
            throw new InvalidOperationException($"AWS profile '{awsProfile}' not found");
        }
        return new AmazonBedrockRuntimeClient(config);
    }

    public async Task<ClassificationResult> ClassifyDocumentAsync(Document document, Stream documentContent)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            var modelId = configuration["Bedrock:ClassificationModelId"] ?? "anthropic.claude-3-haiku-20240307-v1:0";
            var content = FormatExtractedContent(await ExtractContentAsync(document, documentContent));
            var prompt = $"Analyze this document and classify it.\n\nDocument: {document.FileName}\nContent:\n{content}\n\nRespond with JSON: {{\"category\": \"Invoice\", \"confidence\": 0.95, \"tags\": [\"financial\"]}}";
            var response = await InvokeModelAsync(modelId, prompt, CancellationToken.None);
            var result = ParseClassificationResponse(response);
            result.ProcessingTime = DateTime.UtcNow - startTime;
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error classifying {DocumentId}", document.Id);
            return new ClassificationResult { PrimaryCategory = "Error", ProcessingNotes = ex.Message, ProcessingTime = DateTime.UtcNow - startTime };
        }
    }

    public async Task<SummaryResult> SummarizeDocumentAsync(Document document, Stream documentContent)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            var modelId = configuration["Bedrock:SummarizationModelId"] ?? "anthropic.claude-3-haiku-20240307-v1:0";
            var content = FormatExtractedContent(await ExtractContentAsync(document, documentContent));
            var prompt = $"Summarize this document in 1000 characters.\n\nDocument: {document.FileName}\nContent:\n{content}";
            var response = await InvokeModelAsync(modelId, prompt, CancellationToken.None);
            var result = new SummaryResult { Summary = response.Trim(), Language = "en", ProcessingTime = DateTime.UtcNow - startTime };
            foreach (var s in response.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).Take(5))
                if (s.Trim().Length > 20) result.KeyPoints.Add(s.Trim());
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error summarizing {DocumentId}", document.Id);
            return new SummaryResult { Summary = $"Error: {ex.Message}", ProcessingTime = DateTime.UtcNow - startTime };
        }
    }

    private async Task<string> InvokeModelAsync(string modelId, string prompt, CancellationToken ct)
    {
        var maxRetries = int.Parse(configuration["Bedrock:MaxRetries"] ?? "3");
        var delay = int.Parse(configuration["Bedrock:RetryDelayMilliseconds"] ?? "1000");
        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                var request = new ConverseRequest
                {
                    ModelId = modelId,
                    Messages = [new Message { Role = ConversationRole.User, Content = [new ContentBlock { Text = prompt }] }],
                    InferenceConfig = new InferenceConfiguration
                    {
                        MaxTokens = int.Parse(configuration["Bedrock:MaxTokens"] ?? "2000"),
                        Temperature = float.Parse(configuration["Bedrock:Temperature"] ?? "0.3"),
                        TopP = float.Parse(configuration["Bedrock:TopP"] ?? "0.9")
                    }
                };
                var response = await _bedrockClient.ConverseAsync(request, ct);
                return response.Output?.Message?.Content?.FirstOrDefault(c => c.Text != null)?.Text ?? string.Empty;
            }
            catch (AmazonBedrockRuntimeException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests || ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                if (retry >= maxRetries - 1) throw;
                await Task.Delay(delay * (retry + 1), ct);
            }
        }
        throw new InvalidOperationException($"Failed after {maxRetries} retries");
    }

    private async Task<DocumentContent> ExtractContentAsync(Document document, Stream stream)
    {
        var ext = Path.GetExtension(document.FileName)?.ToLower() ?? "";
        try
        {
            var content = ext switch
            {
                ".pdf" => await ExtractPdfContentAsync(stream),
                ".txt" or ".log" or ".md" => await ExtractTextContentAsync(stream),
                ".csv" => await ExtractCsvContentAsync(stream),
                _ => new DocumentContent { Text = $"[Unsupported: {ext}]", ContentType = "unsupported" }
            };
            if (content.Text.Length > MaxContentLength)
            {
                content.Text = content.Text[..MaxContentLength] + "\n[Truncated]";
                content.IsTruncated = true;
            }
            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Extract error");
            return new DocumentContent { Text = $"[Error: {ex.Message}]", ContentType = "error" };
        }
    }

    private async Task<DocumentContent> ExtractPdfContentAsync(Stream pdfStream)
    {
        var sb = new StringBuilder();
        await Task.Run(() =>
        {
            using var doc = PdfDocument.Open(pdfStream);
            foreach (var page in doc.GetPages())
            {
                var text = ContentOrderTextExtractor.GetText(page);
                if (!string.IsNullOrWhiteSpace(text)) { sb.AppendLine($"--- Page {page.Number} ---"); sb.AppendLine(text); }
                if (sb.Length > MaxContentLength) break;
            }
        });
        return new DocumentContent { ContentType = "pdf", Text = sb.ToString() };
    }

    private async Task<DocumentContent> ExtractCsvContentAsync(Stream csvStream)
    {
        var sb = new StringBuilder();
        using var reader = new StreamReader(csvStream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { BadDataFound = null, MissingFieldFound = null });
        await Task.Run(() =>
        {
            csv.Read(); csv.ReadHeader();
            var headers = csv.HeaderRecord?.ToList() ?? [];
            if (headers.Any())
            {
                sb.AppendLine($"Columns: {string.Join(", ", headers)}");
                int rowCount = 0;
                while (csv.Read() && rowCount < 100)
                {
                    sb.AppendLine(string.Join(" | ", headers.Select((_, i) => csv.GetField(i) ?? "")));
                    rowCount++;
                }
                sb.AppendLine($"\nTotal Rows: {rowCount}");
            }
        });
        return new DocumentContent { ContentType = "csv", Text = sb.ToString() };
    }

    private async Task<DocumentContent> ExtractTextContentAsync(Stream textStream)
    {
        textStream.Position = 0;
        using var reader = new StreamReader(textStream, Encoding.UTF8);
        return new DocumentContent { ContentType = "text", Text = await reader.ReadToEndAsync() };
    }

    private string FormatExtractedContent(DocumentContent content) =>
        $"[Type: {content.ContentType}]\n[Content]\n{content.Text}{(content.IsTruncated ? "\n[Truncated]" : "")}";

    private ClassificationResult ParseClassificationResponse(string response)
    {
        try
        {
            var cleaned = response.Replace("```json", "").Replace("```", "").Trim();
            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');
            if (start >= 0 && end > start) cleaned = cleaned.Substring(start, end - start + 1);
            else return new ClassificationResult { PrimaryCategory = "Unknown" };

            var json = JsonDocument.Parse(cleaned);
            var root = json.RootElement;
            var result = new ClassificationResult { PrimaryCategory = root.TryGetProperty("category", out var cat) ? cat.GetString() ?? "Unknown" : "Unknown" };
            if (root.TryGetProperty("confidence", out var conf)) result.CategoryConfidences[result.PrimaryCategory] = conf.GetDouble();
            if (root.TryGetProperty("tags", out var tags)) foreach (var tag in tags.EnumerateArray()) result.Tags.Add(tag.GetString() ?? "");
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Parse error");
            return new ClassificationResult { PrimaryCategory = "Unknown", ProcessingNotes = $"Parse error" };
        }
    }
}

public class DocumentContent
{
    public string Text { get; set; } = "";
    public string ContentType { get; set; } = "";
    public bool IsTruncated { get; set; }
}

public class ClassificationResult
{
    public string PrimaryCategory { get; set; } = "";
    public Dictionary<string, double> CategoryConfidences { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public string ProcessingNotes { get; set; } = "";
    public TimeSpan ProcessingTime { get; set; }
}

public class SummaryResult
{
    public string Summary { get; set; } = "";
    public string Language { get; set; } = "";
    public List<string> KeyPoints { get; set; } = [];
    public TimeSpan ProcessingTime { get; set; }
}

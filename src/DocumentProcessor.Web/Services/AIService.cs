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
        var config = new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(region)
        };

        var awsProfile = configuration["Bedrock:AwsProfile"];
        if (!string.IsNullOrEmpty(awsProfile))
        {
            var credentialFile = new Amazon.Runtime.CredentialManagement.SharedCredentialsFile();
            if (credentialFile.TryGetProfile(awsProfile, out var profile) &&
                profile.GetAWSCredentials(credentialFile) is var credentials)
            {
                return new AmazonBedrockRuntimeClient(credentials, config);
            }
            else
            {
                throw new InvalidOperationException($"AWS profile '{awsProfile}' not found in credentials file");
            }
        }
        else
        {
            return new AmazonBedrockRuntimeClient(config);
        }
    }

    public async Task<ClassificationResult> ClassifyDocumentAsync(Document document, Stream documentContent)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            var modelId = configuration["Bedrock:ClassificationModelId"] ?? "anthropic.claude-3-haiku-20240307-v1:0";
            logger.LogInformation("Classifying document {DocumentId} using Bedrock model: {Model}",
                document.Id, modelId);

            var extractedContent = await ExtractContentAsync(document, documentContent);
            string content = FormatExtractedContent(extractedContent);

            var prompt = $@"Analyze the following document and classify it.

Document name: {document.FileName}
Document content:
{content}

Respond with ONLY a valid JSON object (no additional text) containing:
- category: the document category (e.g., Invoice, Contract, Report, Letter, etc.)
- confidence: a number between 0 and 1
- tags: array of relevant tags

Example response:
{{""category"": ""Invoice"", ""confidence"": 0.95, ""tags"": [""financial"", ""billing""]}}";

            var response = await InvokeModelAsync(modelId, prompt, CancellationToken.None);
            var result = ParseClassificationResponse(response);
            result.ProcessingTime = DateTime.UtcNow - startTime;
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error classifying document {DocumentId}", document.Id);
            return new ClassificationResult
            {
                PrimaryCategory = "Error: Processing Failed",
                ProcessingNotes = $"Error: {ex.Message}",
                ProcessingTime = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<SummaryResult> SummarizeDocumentAsync(Document document, Stream documentContent)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            var modelId = configuration["Bedrock:SummarizationModelId"] ?? "anthropic.claude-3-haiku-20240307-v1:0";
            logger.LogInformation("Summarizing document {DocumentId} using Bedrock model: {Model}",
                document.Id, modelId);

            var extractedContent = await ExtractContentAsync(document, documentContent);
            string content = FormatExtractedContent(extractedContent);

            var prompt = $@"Provide a concise summary of the following document in approximately 1000 characters.
Document name: {document.FileName}
Document ID: {document.Id}

Document content:
{content}

Create a clear, informative summary that captures the main points and purpose of the document.";

            var response = await InvokeModelAsync(modelId, prompt, CancellationToken.None);

            var result = new SummaryResult
            {
                Summary = response.Trim(),
                Language = "en",
                ProcessingTime = DateTime.UtcNow - startTime
            };

            var sentences = response.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var sentence in sentences.Take(5))
            {
                var trimmed = sentence.Trim();
                if (trimmed.Length > 20)
                {
                    result.KeyPoints.Add(trimmed);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error summarizing document {DocumentId}", document.Id);
            return new SummaryResult
            {
                Summary = $"Error generating summary: {ex.Message}",
                ProcessingTime = DateTime.UtcNow - startTime
            };
        }
    }

    private async Task<string> InvokeModelAsync(string modelId, string prompt, CancellationToken cancellationToken)
    {
        var maxRetries = int.Parse(configuration["Bedrock:MaxRetries"] ?? "3");
        var delay = int.Parse(configuration["Bedrock:RetryDelayMilliseconds"] ?? "1000");
        var retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                var message = new Message
                {
                    Role = ConversationRole.User,
                    Content = [new ContentBlock { Text = prompt }]
                };

                var request = new ConverseRequest
                {
                    ModelId = modelId,
                    Messages = [message],
                    InferenceConfig = new InferenceConfiguration
                    {
                        MaxTokens = int.Parse(configuration["Bedrock:MaxTokens"] ?? "2000"),
                        Temperature = float.Parse(configuration["Bedrock:Temperature"] ?? "0.3"),
                        TopP = float.Parse(configuration["Bedrock:TopP"] ?? "0.9")
                    }
                };

                var response = await _bedrockClient.ConverseAsync(request, cancellationToken);

                if (response.Output?.Message?.Content != null && response.Output.Message.Content.Count > 0)
                {
                    var textContent = response.Output.Message.Content
                        .Where(c => c.Text != null)
                        .Select(c => c.Text)
                        .FirstOrDefault();

                    return textContent ?? string.Empty;
                }

                return string.Empty;
            }
            catch (AmazonBedrockRuntimeException ex) when (
                ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                retryCount++;
                if (retryCount >= maxRetries)
                {
                    throw;
                }

                logger.LogWarning("Bedrock API throttled, retrying in {Delay}ms", delay);
                await Task.Delay(delay, cancellationToken);
                delay *= 2;
            }
        }

        throw new InvalidOperationException($"Failed to invoke Bedrock model after {maxRetries} retries");
    }

    private async Task<DocumentContent> ExtractContentAsync(Document document, Stream documentStream)
    {
        var extension = Path.GetExtension(document.FileName)?.ToLower() ?? "";
        logger.LogInformation("Extracting content from {Extension} file: {FileName}", extension, document.FileName);

        try
        {
            var content = extension switch
            {
                ".pdf" => await ExtractPdfContentAsync(documentStream),
                ".txt" or ".log" or ".md" => await ExtractTextContentAsync(documentStream),
                ".csv" => await ExtractCsvContentAsync(documentStream),
                _ => new DocumentContent
                {
                    Text = $"[Unsupported file type: {extension}]",
                    ContentType = "unsupported"
                }
            };

            if (content.Text.Length > MaxContentLength)
            {
                logger.LogWarning("Content truncated from {Original} to {Max} characters",
                    content.Text.Length, MaxContentLength);
                content.Text = content.Text.Substring(0, MaxContentLength) + "\n\n[Content truncated due to length...]";
                content.IsTruncated = true;
            }

            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting content from {FileName}", document.FileName);
            return new DocumentContent
            {
                Text = $"[Error extracting content: {ex.Message}]",
                ContentType = "error"
            };
        }
    }

    private async Task<DocumentContent> ExtractPdfContentAsync(Stream pdfStream)
    {
        var content = new DocumentContent { ContentType = "pdf" };
        var textBuilder = new StringBuilder();
        int pageCount = 0;

        await Task.Run(() =>
        {
            using var document = PdfDocument.Open(pdfStream);
            pageCount = document.NumberOfPages;

            foreach (var page in document.GetPages())
            {
                var pageText = ContentOrderTextExtractor.GetText(page);
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    textBuilder.AppendLine($"--- Page {page.Number} ---");
                    textBuilder.AppendLine(pageText);
                }

                if (textBuilder.Length > MaxContentLength)
                {
                    textBuilder.AppendLine("\n[Remaining pages truncated]");
                    break;
                }
            }
        });

        content.Text = textBuilder.ToString();
        logger.LogInformation("Extracted {Characters} characters from {Pages} page PDF",
            content.Text.Length, pageCount);

        return content;
    }

    private async Task<DocumentContent> ExtractCsvContentAsync(Stream csvStream)
    {
        var content = new DocumentContent { ContentType = "csv" };
        using var reader = new StreamReader(csvStream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = null,
            MissingFieldFound = null
        });

        var textBuilder = new StringBuilder();

        await Task.Run(() =>
        {
            csv.Read();
            csv.ReadHeader();
            var headers = csv.HeaderRecord?.ToList() ?? [];

            if (headers.Count != 0)
            {
                textBuilder.AppendLine("CSV Structure:");
                textBuilder.AppendLine($"Columns: {string.Join(", ", headers)}");
                textBuilder.AppendLine("\nData Sample:");
                textBuilder.AppendLine(string.Join(" | ", headers));
                textBuilder.AppendLine(new string('-', headers.Count * 10));
            }

            int rowCount = 0;
            while (csv.Read() && rowCount < 100)
            {
                List<string> row = [];
                for (int i = 0; i < headers.Count; i++)
                {
                    try
                    {
                        row.Add(csv.GetField(i) ?? "");
                    }
                    catch
                    {
                        row.Add("");
                    }
                }
                textBuilder.AppendLine(string.Join(" | ", row));
                rowCount++;
            }

            while (csv.Read()) rowCount++;

            if (headers.Count != 0)
            {
                textBuilder.AppendLine($"\n\nTotal Rows: {rowCount}");
                textBuilder.AppendLine($"Total Columns: {headers.Count}");
            }
        });

        content.Text = textBuilder.ToString();
        return content;
    }

    private async Task<DocumentContent> ExtractTextContentAsync(Stream textStream)
    {
        var content = new DocumentContent { ContentType = "text" };
        textStream.Position = 0;

        using var reader = new StreamReader(textStream, Encoding.UTF8);
        content.Text = await reader.ReadToEndAsync();

        var lines = content.Text.Split('\n');
        char[] separators = [' ', '\n', '\r', '\t'];
        var words = content.Text.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        logger.LogInformation("Extracted text file with {Lines} lines, {Words} words", lines.Length, words.Length);
        return content;
    }

    private string FormatExtractedContent(DocumentContent extractedContent)
    {
        var formatted = new StringBuilder();
        formatted.AppendLine($"[Document Type: {extractedContent.ContentType}]");
        formatted.AppendLine("\n[Content]");
        formatted.AppendLine(extractedContent.Text);

        if (extractedContent.IsTruncated)
        {
            formatted.AppendLine("\n[Note: Content was truncated due to length limits]");
        }

        return formatted.ToString();
    }

    private ClassificationResult ParseClassificationResponse(string response)
    {
        try
        {
            string cleanedResponse = CleanJsonResponse(response);
            var json = JsonDocument.Parse(cleanedResponse);
            var root = json.RootElement;

            var result = new ClassificationResult
            {
                PrimaryCategory = root.TryGetProperty("category", out var cat)
                    ? cat.GetString() ?? "Unknown" : "Unknown"
            };

            if (root.TryGetProperty("confidence", out var conf))
            {
                result.CategoryConfidences[result.PrimaryCategory] = conf.GetDouble();
            }

            if (root.TryGetProperty("tags", out var tags))
            {
                foreach (var tag in tags.EnumerateArray())
                {
                    result.Tags.Add(tag.GetString() ?? "");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse classification response: {Response}", response);
            return new ClassificationResult
            {
                PrimaryCategory = "Unknown",
                ProcessingNotes = $"Parse error: {ex.Message}"
            };
        }
    }

    private string CleanJsonResponse(string response)
    {
        if (string.IsNullOrEmpty(response))
            return "{}";

        var cleaned = response.Replace("```json", "").Replace("```", "").Trim();

        int startIndex = cleaned.IndexOf('{');
        int endIndex = cleaned.LastIndexOf('}');

        if (startIndex >= 0 && endIndex > startIndex)
        {
            cleaned = cleaned.Substring(startIndex, endIndex - startIndex + 1);
        }
        else
        {
            logger.LogWarning("Could not find valid JSON object markers in response");
            return "{}";
        }

        return cleaned;
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

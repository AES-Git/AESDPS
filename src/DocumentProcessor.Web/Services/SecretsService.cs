using System.Text.Json;
using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace DocumentProcessor.Web.Services;

public class SecretsService
{
    private readonly IAmazonSecretsManager _secretsManager;

    public SecretsService()
    {
        _secretsManager = new AmazonSecretsManagerClient(RegionEndpoint.USEast1);
    }

    public async Task<string> GetSecretAsync(string secretName)
    {
        try
        {
            var request = new GetSecretValueRequest
            {
                SecretId = secretName
            };

            var response = await _secretsManager.GetSecretValueAsync(request);
            return response.SecretString;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error retrieving secret '{secretName}': {ex.Message}", ex);
        }
    }

    public async Task<string> GetSecretByDescriptionPrefixAsync(string descriptionPrefix)
    {
        try
        {
            var listRequest = new ListSecretsRequest();
            var listResponse = await _secretsManager.ListSecretsAsync(listRequest);

            foreach (var secret in listResponse.SecretList)
            {
                if (!string.IsNullOrEmpty(secret.Description) &&
                    secret.Description.StartsWith(descriptionPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var getRequest = new GetSecretValueRequest
                    {
                        SecretId = secret.ARN
                    };
                    var getResponse = await _secretsManager.GetSecretValueAsync(getRequest);
                    return getResponse.SecretString;
                }
            }

            throw new Exception($"No secret found with description starting with '{descriptionPrefix}'");
        }
        catch (Exception ex)
        {
            throw new Exception($"Error finding secret by description prefix '{descriptionPrefix}': {ex.Message}", ex);
        }
    }

    public string GetFieldFromSecret(string secretJson, string fieldName)
    {
        try
        {
            using var document = JsonDocument.Parse(secretJson);
            if (document.RootElement.TryGetProperty(fieldName, out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString() ?? string.Empty,
                    JsonValueKind.Number => value.GetInt32().ToString(),
                    _ => value.ToString()
                };
            }
            throw new Exception($"Field '{fieldName}' not found in secret");
        }
        catch (Exception ex)
        {
            throw new Exception($"Error parsing secret field '{fieldName}': {ex.Message}", ex);
        }
    }

    public async Task<string> BuildConnectionStringAsync(string secretName, string databaseName)
    {
        try
        {
            var secretJson = await GetSecretAsync(secretName);
            using var document = JsonDocument.Parse(secretJson);
            var root = document.RootElement;

            var host = root.GetProperty("host").GetString();
            var port = root.GetProperty("port").GetInt32();
            var username = root.GetProperty("username").GetString();
            var password = root.GetProperty("password").GetString();

            return $"Server={host},{port};Database={databaseName};User Id={username};Password={password};TrustServerCertificate=True;";
        }
        catch (Exception ex)
        {
            throw new Exception($"Error building connection string from secret '{secretName}': {ex.Message}", ex);
        }
    }
}

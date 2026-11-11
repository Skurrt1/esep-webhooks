using System.Text;
using Amazon.Lambda.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace EsepWebhook;

public class Function
{
    // Reuse one HttpClient across invocations instead of creating one per call.
    private static readonly HttpClient Client = new HttpClient();

    /// <summary>
    /// Receives a GitHub "issues" webhook payload (via API Gateway, proxy integration OFF)
    /// and posts the issue link to Slack using the SLACK_URL environment variable.
    /// </summary>
    public string FunctionHandler(object input, ILambdaContext context)
    {
        string raw = input?.ToString() ?? "";
        context.Logger.LogLine($"Received: {raw}");

        // Defensive parsing: GitHub's "ping" event, a plain string, or bad JSON
        // should not crash the function.
        JToken json;
        try
        {
            json = JToken.Parse(raw);
        }
        catch (JsonReaderException)
        {
            context.Logger.LogLine("Input was not valid JSON; nothing to post.");
            return "ignored: not JSON";
        }

        string? issueUrl = (json as JObject)?.SelectToken("issue.html_url")?.ToString();
        if (string.IsNullOrEmpty(issueUrl))
        {
            context.Logger.LogLine("No issue.html_url in payload (e.g. a ping event); nothing to post.");
            return "ignored: no issue";
        }

        string? slackUrl = Environment.GetEnvironmentVariable("SLACK_URL");
        if (string.IsNullOrEmpty(slackUrl))
        {
            context.Logger.LogLine("SLACK_URL environment variable is not set.");
            return "error: SLACK_URL not set";
        }

        string payload = JsonConvert.SerializeObject(new { text = $"Issue Created: {issueUrl}" });

        var webRequest = new HttpRequestMessage(HttpMethod.Post, slackUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = Client.Send(webRequest);
        using var reader = new StreamReader(response.Content.ReadAsStream());
        string result = reader.ReadToEnd();
        context.Logger.LogLine($"Slack responded {(int)response.StatusCode}: {result}");
        return result;
    }
}

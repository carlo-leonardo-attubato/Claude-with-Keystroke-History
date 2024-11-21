using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Text;

namespace KeyloggerProject
{
    public class ChatWindow : Form
    {
        private WebView2 webView;
        private readonly KeystrokeLogger logger;
        private readonly string keystrokeContext;
        private ConversationSession conversation;
        private static readonly HttpClient client = new();

        public ChatWindow(string keystrokeContext, KeystrokeLogger logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.keystrokeContext = keystrokeContext ?? throw new ArgumentNullException(nameof(keystrokeContext));
            this.conversation = new ConversationSession 
            { 
                Messages = new List<Message>()
            };

            this.Size = new Size(800, 600);
            this.Text = "Chat with Claude";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = true;

            webView = new WebView2
            {
                Dock = DockStyle.Fill
            };
            this.Controls.Add(webView);

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                webView.CoreWebView2.WebMessageReceived += HandleWebMessage;
                
                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web", "index.html");
                webView.CoreWebView2.Navigate(new Uri(htmlPath).ToString());

                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                    window.addEventListener('DOMContentLoaded', () => {
                        window.chrome.webview.postMessage({ type: 'ready' });
                    });
                ");

                var config = new
                {
                    models = Configuration.Instance.Models?.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new
                        {
                            name = kvp.Value.Name,
                            inputCost = kvp.Value.InputCost,
                            outputCost = kvp.Value.OutputCost,
                            defaultTokens = kvp.Value.DefaultTokens,
                            maxTokens = kvp.Value.MaxTokens
                        }
                    ),
                    ui = new
                    {
                        defaultModel = Configuration.Instance.Ui?.DefaultModel,
                        defaultMaxTokens = Configuration.Instance.Ui?.DefaultMaxTokens ?? 4000,
                        minTokens = Configuration.Instance.Ui?.MinTokens ?? 1000,
                        maxTokens = Configuration.Instance.Ui?.MaxTokens ?? 8000,
                        tokenStep = Configuration.Instance.Ui?.TokenStep ?? 1000
                    }
                };

                var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true 
                });

                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"console.log('Config:', {configJson}); initializeUI({configJson});");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private async void HandleWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                var messageData = JsonSerializer.Deserialize<WebMessage>(args.WebMessageAsJson);
                if (messageData?.type == "ready") return;
                
                if (messageData?.type == "sendMessage" && !string.IsNullOrEmpty(messageData.message))
                {
                    if (messageData.includeKeystrokes)
                    {
                        conversation.Messages.Add(new Message
                        {
                            Role = "user",
                            Content = $"Hey, I'll send you my recent keystroke history as context for the forthcoming conversation: {keystrokeContext}. Please acknowledge by typing \"Y\"",
                            Timestamp = DateTime.UtcNow
                        });

                        conversation.Messages.Add(new Message
                        {
                            Role = "assistant",
                            Content = "Y",
                            Timestamp = DateTime.UtcNow
                        });
                    }

                    conversation.Messages.Add(new Message
                    {
                        Role = "user",
                        Content = messageData.message,
                        Timestamp = DateTime.UtcNow
                    });

                    await StreamResponse(
                        messageData.model ?? Configuration.Instance.Ui?.DefaultModel ?? "claude-3-haiku-20240307",
                        messageData.message,
                        messageData.maxTokens
                    );
                }
            }
            catch (Exception ex)
            {
                await webView.CoreWebView2.ExecuteScriptAsync($"handleError(`{JsonEncode(ex.Message)}`)");
            }
        }

        private async Task StreamResponse(string model, string message, int maxTokens)
        {
            try 
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            model = model,
                            max_tokens = maxTokens,
                            messages = conversation.Messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
                            stream = true
                        }),
                        Encoding.UTF8,
                        "application/json"
                    ),
                    Headers =
                    {
                        { "x-api-key", Configuration.Instance.Anthropic?.ApiKey },
                        { "anthropic-version", Configuration.Instance.Anthropic?.ApiVersion }
                    }
                };

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"API Error: {response.StatusCode} - {errorContent}");
                }

                var stream = await response.Content.ReadAsStreamAsync();
                var buffer = new byte[1024];
                var fullResponse = new StringBuilder();
                double? cost = null;
                int? inputTokens = null;
                int? outputTokens = null;
                var pendingData = "";

                while (true)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    var chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var data = pendingData + chunk;
                    var lines = data.Split('\n');
                    
                    pendingData = lines[^1];
                    
                    for (var i = 0; i < lines.Length - 1; i++)
                    {
                        var line = lines[i].Trim();
                        if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
                        
                        var jsonData = line.Substring(6);
                        if (jsonData == "[DONE]") goto StreamComplete;

                        try 
                        {
                            var eventData = JsonSerializer.Deserialize<StreamEvent>(jsonData);
                            await webView.CoreWebView2.ExecuteScriptAsync($"console.log('Event:', `{jsonData}`);");

                            if (eventData?.type == "content_block_delta" && 
                                eventData.delta?.type == "text_delta" && 
                                eventData.delta?.text != null)
                            {
                                var textChunk = eventData.delta.text;
                                fullResponse.Append(textChunk);
                                await webView.CoreWebView2.ExecuteScriptAsync(
                                    $"window.receiveChunk(`{JsonEncode(textChunk)}`);");
                            }
                            else if (eventData?.type == "message_delta" && eventData.usage != null)
                            {
                                outputTokens = eventData.usage.output_tokens;
                            }
                        }
                        catch (JsonException) { continue; }
                    }
                }

                StreamComplete:
                if (fullResponse.Length > 0 && Configuration.Instance.Models != null)
                {
                    var modelConfig = Configuration.Instance.Models[model];
                    if (inputTokens.HasValue && outputTokens.HasValue)
                    {
                        cost = (inputTokens.Value / 1000.0 * modelConfig.InputCost) +
                               (outputTokens.Value / 1000.0 * modelConfig.OutputCost);
                    }

                    conversation.Messages.Add(new Message
                    {
                        Role = "assistant",
                        Content = fullResponse.ToString(),
                        Model = model,
                        Cost = cost,
                        Timestamp = DateTime.UtcNow,
                        Usage = inputTokens.HasValue && outputTokens.HasValue ? new Usage 
                        { 
                            input_tokens = inputTokens.Value,
                            output_tokens = outputTokens.Value
                        } : null
                    });

                    await webView.CoreWebView2.ExecuteScriptAsync(
                        $"finishStream({(cost.HasValue ? cost.Value.ToString() : "null")})");
                }
            }
            catch (Exception ex)
            {
                await webView.CoreWebView2.ExecuteScriptAsync($"handleError(`{JsonEncode(ex.Message)}`)");
            }
        }

        private string JsonEncode(string text)
        {
            return text
                .Replace("\\", "\\\\")
                .Replace("`", "\\`")
                .Replace("$", "\\$")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (conversation.Messages.Any())
            {
                logger.SaveConversation(conversation);
            }
            base.OnFormClosing(e);
        }
    }
}
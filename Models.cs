using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace KeyloggerProject
{
    public class Message
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";

        [JsonPropertyName("cost")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Cost { get; set; }

        [JsonPropertyName("source")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Source { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("usage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Usage? Usage { get; set; }
    }

    public class StreamEvent
    {
        [JsonPropertyName("type")]
        public string? type { get; set; }

        [JsonPropertyName("delta")]
        public Delta? delta { get; set; }

        [JsonPropertyName("usage")]
        public Usage? usage { get; set; }

        [JsonPropertyName("message")]
        public MessageData? message { get; set; }
    }

    public class MessageData 
    {
        [JsonPropertyName("usage")]
        public Usage? usage { get; set; }

        [JsonPropertyName("content")]
        public List<ContentBlock>? content { get; set; }
    }

    public class ContentBlock
    {
        [JsonPropertyName("type")]
        public string? type { get; set; }

        [JsonPropertyName("text")]
        public string? text { get; set; }
    }

    public class Usage
    {
        [JsonPropertyName("input_tokens")]
        public int input_tokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int output_tokens { get; set; }
    }

    public class Delta
    {
        [JsonPropertyName("type")]
        public string? type { get; set; }

        [JsonPropertyName("text")]
        public string? text { get; set; }
    }

    public class WebMessage
    {
        [JsonPropertyName("type")]
        public string? type { get; set; }

        [JsonPropertyName("message")]
        public string? message { get; set; }

        [JsonPropertyName("model")]
        public string? model { get; set; }

        [JsonPropertyName("includeKeystrokes")]
        public bool includeKeystrokes { get; set; }

        [JsonPropertyName("maxTokens")]
        public int maxTokens { get; set; }
    }

    public class ConversationSession
    {
        [JsonPropertyName("messages")]
        public List<Message> Messages { get; set; } = new();

        [JsonPropertyName("start_time")]
        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("last_updated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("total_cost")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? TotalCost => Messages.Sum(m => m.Cost);
    }
}
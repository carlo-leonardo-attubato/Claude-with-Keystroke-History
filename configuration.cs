using System;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;

namespace KeyloggerProject
{
    public class Configuration
    {
        public AnthropicConfig? Anthropic { get; set; }
        public BufferConfig? Buffer { get; set; }
        public FilesConfig? Files { get; set; }
        public Dictionary<string, ModelConfig>? Models { get; set; }
        public UiConfig? Ui { get; set; }

        private static Configuration? _instance;
        private static readonly object _lock = new();
        private static bool _initialized;

        public static Configuration Instance
        {
            get
            {
                if (!_initialized)
                {
                    lock (_lock)
                    {
                        if (!_initialized)
                        {
                            _instance = LoadConfiguration();
                            _initialized = true;
                        }
                    }
                }
                return _instance!;
            }
        }

        private static Configuration LoadConfiguration()
        {
            try
            {
                string configPath = "config.json";
                if (!File.Exists(configPath))
                {
                    throw new FileNotFoundException("config.json not found");
                }

                string configJson = File.ReadAllText(configPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };

                var config = JsonSerializer.Deserialize<Configuration>(configJson, options);
                if (config == null)
                {
                    throw new InvalidOperationException("Failed to deserialize config.json");
                }
                return config;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error loading configuration: {ex.Message}", ex);
            }
        }
    }

    public class AnthropicConfig
    {
        public string? ApiKey { get; set; }
        public string? ApiVersion { get; set; }
    }

    public class BufferConfig
    {
        public int Size { get; set; }
        public int WriteInterval { get; set; }
    }

    public class FilesConfig
    {
        public string? Keystrokes { get; set; }
        public string? Conversations { get; set; }
        public string? Key { get; set; }
        public string? Debug { get; set; }
        public string? Error { get; set; }
    }

    public class ModelConfig
    {
        public double InputCost { get; set; }
        public double OutputCost { get; set; }
        public int DefaultTokens { get; set; }
        public int MaxTokens { get; set; }
        public string? Name { get; set; }
    }

    public class UiConfig
    {
        public string DefaultModel { get; set; } = "claude-3-haiku-20240307";
        public int DefaultMaxTokens { get; set; } = 4000;
        public int MinTokens { get; set; } = 1000;
        public int MaxTokens { get; set; } = 8000;
        public int TokenStep { get; set; } = 1000;
    }
}
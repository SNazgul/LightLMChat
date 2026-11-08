using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LightLMChat.Core;
using LightLMChat.PcParts;

namespace LightLMChat.ExampleApp;

public partial class MainWindow : Window
{
    private readonly PcPartsCatalog _catalog = new();
    private readonly ObservableCollection<PcPart> _visibleParts = [];
    private LlamaServerRuntime? _runtime;
    private readonly CancellationTokenSource _lifetime = new();

    public MainWindow()
    {
        InitializeComponent();
        PartsList.ItemsSource = _visibleParts;
        ShowParts(_catalog.All);
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void ShowParts(IReadOnlyList<PcPart> parts)
    {
        _visibleParts.Clear();
        foreach (var part in parts) _visibleParts.Add(part);
        PartsList.SelectedIndex = _visibleParts.Count > 0 ? 0 : -1;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        ShowParts(query.Length == 0 ? _catalog.All : _catalog.Search(query));
    }

    private void PartsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PartsList.SelectedItem is not PcPart part)
        {
            DetailSubtype.Text = DetailMeta.Text = DetailCharacteristics.Text = DetailDescription.Text = string.Empty;
            return;
        }
        DetailSubtype.Text = part.Subtype;
        DetailMeta.Text = $"{part.Type} · {part.Company}";
        DetailCharacteristics.Text = part.Characteristics;
        DetailDescription.Text = part.Description;
    }

    private static string? FindInTree(string startDir, string subdir, string pattern, bool recursive = false)
    {
        for (var dir = new DirectoryInfo(startDir); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, subdir);
            if (!Directory.Exists(candidate)) continue;
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var match = Directory.EnumerateFiles(candidate, pattern, option).FirstOrDefault();
            if (match is not null) return match;
        }
        return null;
    }

    private static (string? Server, string? Model) LoadConfiguration()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "lightlmchat.config.json");
        if (!File.Exists(configPath))
            return (null, null);

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = doc.RootElement;
        string? server = root.TryGetProperty("serverPath", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString()
            : null;
        string? model = root.TryGetProperty("modelPath", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()
            : null;
        return (server, model);
    }

    private static (string? Server, string? Model) ResolveRuntime()
    {
        // lightlmchat.config.json takes precedence; otherwise auto-discover the runtime
        // (a bundled runtime/ folder, or the scripts/prepare-runtime.ps1 layout).
        var (server, model) = LoadConfiguration();

        var baseDir = AppContext.BaseDirectory;
        var cwd = Directory.GetCurrentDirectory();

        if (string.IsNullOrWhiteSpace(server))
        {
            server = FindInTree(baseDir, "runtime", "llama-server.exe")
                     ?? FindInTree(cwd, "artifacts", "llama-server.exe", recursive: true)
                     ?? FindInTree(baseDir, "artifacts", "llama-server.exe", recursive: true);
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            model = FindInTree(baseDir, "runtime", "*.gguf")
                    ?? FindInTree(cwd, "models", "*.gguf")
                    ?? FindInTree(baseDir, "models", "*.gguf");
        }

        return (server, model);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var (server, model) = ResolveRuntime();
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(model))
        {
            ShowModelNotice(
                "Local model not found. Run scripts/prepare-runtime.ps1 to download the engine and model, or set their paths in lightlmchat.config.json. The catalog is still browsable.",
                inChat: true);
            return;
        }

        ShowModelNotice("Starting local model…");
        try
        {
            var runtime = await LlamaServerRuntime.StartAsync(
                new LlamaServerOptions(server, model), _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                await runtime.DisposeAsync();
                return;
            }
            _runtime = runtime;

            var tools = new ToolCatalog([
                new SearchPartsTool(_catalog),
                new ListPartTypesTool(_catalog),
                new RecommendBuildTool(_catalog)
            ]);
            Chat.Session = new LlamaChatSession(_runtime, tools, systemPrompt:
                "You are a helpful assistant for an IBM PC parts catalog (286-486 era). " +
                "Use the tools to look up parts or assemble a build. " +
                "You can compare parts only by price and listed specifications. " +
                "If the user asks for 'the best', 'fastest', or another subjective quality, " +
                "explain what you can compare and ask what matters to them. " +
                "If a request is ambiguous, ask the user to clarify.");
            ShowModelNotice("Local model active — ask about IBM PC parts (286–486), e.g. \"What Intel 486 CPUs do you have?\"");
        }
        catch (FileNotFoundException)
        {
            ShowModelNotice(
                "Local model files not found. Run scripts/prepare-runtime.ps1, or set their paths in lightlmchat.config.json.",
                inChat: true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed before the model finished loading.
        }
        catch (Exception ex)
        {
            ShowModelNotice($"Local model unavailable: {ex.Message}. The catalog is still browsable.", inChat: true);
        }
    }

    private void ShowModelNotice(string text, bool inChat = false)
    {
        ModelBannerText.Text = text;
        ModelBanner.Visibility = Visibility.Visible;
        Chat.NoticeText = inChat ? text : string.Empty;
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _lifetime.Cancel();
        if (_runtime is not null) await _runtime.DisposeAsync();
        _lifetime.Dispose();
    }
}

using System.Collections.ObjectModel;
using System.IO;
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

    private static (string? Server, string? Model) ResolveRuntime()
    {
        // Precedence: environment variables, then a bundled runtime/ folder next to the app,
        // then the scripts/prepare-runtime.ps1 dev layout (models/ + artifacts/) found by walking up.
        var server = Environment.GetEnvironmentVariable("LIGHTLMCHAT_SERVER_PATH");
        var model = Environment.GetEnvironmentVariable("LIGHTLMCHAT_MODEL_PATH");

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
                "Local model not found. Run scripts/prepare-runtime.ps1 to download the engine and model, or set LIGHTLMCHAT_SERVER_PATH and LIGHTLMCHAT_MODEL_PATH. The catalog is still browsable.",
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
            Chat.Session = new LlamaChatSession(_runtime, tools);
            ShowModelNotice("Local model active — ask about IBM PC parts (286–486), e.g. \"What Intel 486 CPUs do you have?\"");
        }
        catch (FileNotFoundException)
        {
            ShowModelNotice(
                "Local model files not found. Run scripts/prepare-runtime.ps1, or set LIGHTLMCHAT_SERVER_PATH and LIGHTLMCHAT_MODEL_PATH.",
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

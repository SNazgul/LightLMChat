using System.Windows;
using LightLMChat.Core;
using LightLMChat.FamilyTree;

namespace LightLMChat.SampleHost;

public partial class MainWindow : Window
{
    private LlamaServerRuntime? _runtime;
    private readonly CancellationTokenSource _lifetime = new();

    public MainWindow()
    {
        InitializeComponent();
        Chat.Session = new ScriptedChatSession();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var server = Environment.GetEnvironmentVariable("LIGHTLMCHAT_SERVER_PATH");
        var model = Environment.GetEnvironmentVariable("LIGHTLMCHAT_MODEL_PATH");
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(model)) return;

        ModeLabel.Text = "Loading local model…";
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
            var familyApi = new SampleFamilyTreeApi();
            var catalog = new ToolCatalog([new FamilySummaryTool(familyApi)]);
            Chat.Session = new LlamaChatSession(_runtime, catalog);
            ModeLabel.Text = "Local model active — fictional family tree data. Try: When was Mary Example born and who are her children?";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed before the model finished loading.
        }
        catch (Exception ex)
        {
            ModeLabel.Text = $"Local model unavailable ({ex.Message}). Scripted preview remains active.";
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _lifetime.Cancel();
        if (_runtime is not null) await _runtime.DisposeAsync();
        _lifetime.Dispose();
    }
}

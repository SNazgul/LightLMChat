using System.Windows;

namespace LightLMChat.SampleHost;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Chat.Session = new ScriptedChatSession();
    }
}

using System.Text.Json;
using System.Windows;
using LightLMChat.Abstractions;

namespace LightLMChat.SampleHost;

public partial class ApprovalDialog : Window
{
    public ApprovalDialog(ToolActionProposal proposal)
    {
        InitializeComponent();
        ToolNameLabel.Text = proposal.ToolName;
        ArgumentsBox.Text = JsonSerializer.Serialize(
            proposal.Arguments, new JsonSerializerOptions { WriteIndented = true });
    }

    private void ApproveButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void DenyButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

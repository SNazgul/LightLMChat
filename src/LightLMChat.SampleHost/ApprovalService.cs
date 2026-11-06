using System.Windows;
using LightLMChat.Abstractions;

namespace LightLMChat.SampleHost;

/// <summary>Shows a modal approve/deny dialog for every proposed write action.</summary>
internal sealed class ApprovalService(Window owner) : IToolApprovalService
{
    public async ValueTask<bool> ApproveAsync(ToolActionProposal proposal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var app = Application.Current ?? throw new InvalidOperationException("No WPF application is running.");
        return await app.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new ApprovalDialog(proposal) { Owner = owner };
            return dialog.ShowDialog() == true;
        }).Task;
    }
}

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LightLMChat.Wpf;

public sealed class ChatMessage : INotifyPropertyChanged
{
    private string _text;

    public ChatMessage(string role, string text)
    {
        Role = role;
        _text = text;
    }

    public string Role { get; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

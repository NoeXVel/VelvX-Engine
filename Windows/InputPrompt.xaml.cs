using System.Windows;
using System.Windows.Input;
using VelvX.Engine.Theme;

namespace VelvX.Engine.Windows;

/// <summary>Caixa de entrada simples usada para nomes de cenas, prefabs e pastas.</summary>
public partial class InputPrompt : Window
{
    public InputPrompt(string title, string message, string value = "")
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ValueBox.Text = value;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text.Trim();

    public static string? Ask(Window owner, string title, string message, string value = "")
    {
        var prompt = new InputPrompt(title, message, value) { Owner = owner };
        return prompt.ShowDialog() == true ? prompt.Value : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text))
        {
            MessageBox.Show(this, "Digite um nome.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Ok_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}

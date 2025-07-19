using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// AI环境输入对话框
/// </summary>
public partial class AIEnvInputDialog : FluentWindow
{
    public string InputText { get; private set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;

    public AIEnvInputDialog()
    {
        InitializeComponent();
        this.Loaded += OnLoaded;
        this.KeyDown += OnKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InputTextBox.Text = DefaultValue;
        InputTextBox.Focus();
        InputTextBox.SelectAll();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            // Ctrl+Enter 执行
            BtnOkClick(sender, e);
        }
    }

    private void BtnOkClick(object sender, RoutedEventArgs e)
    {
        InputText = InputTextBox.Text?.Trim() ?? string.Empty;
        DialogResult = true;
        Close();
    }

    private void BtnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 显示输入对话框
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>用户输入的文本，如果取消则返回null</returns>
    public static string? ShowInputDialog(Window? owner = null, string defaultValue = "")
    {
        var dialog = new AIEnvInputDialog
        {
            Owner = owner ?? Application.Current.MainWindow,
            DefaultValue = defaultValue
        };

        var result = dialog.ShowDialog();
        return result == true ? dialog.InputText : null;
    }
}

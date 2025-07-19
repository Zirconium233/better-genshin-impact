using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 脚本选择对话框
/// </summary>
public partial class ScriptSelectionDialog : FluentWindow
{
    public string? SelectedScriptPath { get; private set; }
    public List<string> Scripts { get; set; } = new();
    public string ScriptFolder { get; set; } = string.Empty;

    public ScriptSelectionDialog()
    {
        InitializeComponent();
        this.Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ScriptListBox.ItemsSource = Scripts;
        ScriptListBox.SelectionChanged += OnScriptSelectionChanged;
        
        // 默认选择第一个脚本
        if (Scripts.Count > 0)
        {
            ScriptListBox.SelectedIndex = 0;
        }
    }

    private void OnScriptSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScriptListBox.SelectedItem is string selectedScript)
        {
            LoadScriptPreview(selectedScript);
        }
    }

    private void LoadScriptPreview(string scriptName)
    {
        try
        {
            if (scriptName == "根据队伍自动选择")
            {
                PreviewTextBox.Text = "系统将根据当前队伍配置自动生成战斗脚本\n\n包含基础的移动、攻击、技能释放等动作";
                return;
            }

            var scriptPath = Path.Combine(ScriptFolder, scriptName + ".txt");
            if (File.Exists(scriptPath))
            {
                var content = File.ReadAllText(scriptPath);
                PreviewTextBox.Text = string.IsNullOrWhiteSpace(content) ? "脚本文件为空" : content;
            }
            else
            {
                PreviewTextBox.Text = "脚本文件不存在";
            }
        }
        catch
        {
            PreviewTextBox.Text = "无法读取脚本内容";
        }
    }

    private void BtnOkClick(object sender, RoutedEventArgs e)
    {
        if (ScriptListBox.SelectedItem is string selectedScript)
        {
            if (selectedScript == "根据队伍自动选择")
            {
                SelectedScriptPath = "auto";
            }
            else
            {
                var scriptPath = Path.Combine(ScriptFolder, selectedScript + ".txt");
                SelectedScriptPath = File.Exists(scriptPath) ? scriptPath : null;
            }

            DialogResult = true;
            Close();
        }
        else
        {
            System.Windows.MessageBox.Show("请选择一个脚本", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void BtnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 显示脚本选择对话框
    /// </summary>
    /// <param name="scripts">脚本列表</param>
    /// <param name="scriptFolder">脚本文件夹路径</param>
    /// <param name="owner">父窗口</param>
    /// <returns>选中的脚本路径，如果取消则返回null</returns>
    public static string? ShowDialog(List<string> scripts, string scriptFolder, Window? owner = null)
    {
        var dialog = new ScriptSelectionDialog
        {
            Owner = owner ?? Application.Current.MainWindow,
            Scripts = scripts,
            ScriptFolder = scriptFolder
        };

        var result = dialog.ShowDialog();
        return result == true ? dialog.SelectedScriptPath : null;
    }
}

using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using BetterGenshinImpact.GameTask.AIEnv.Schedulers;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// LLM调度器配置对话框
/// </summary>
public partial class LlmConfigDialog : FluentWindow
{
    public string UserPrompt { get; private set; } = string.Empty;
    public LlmSchedulerConfig Config { get; private set; } = new();

    public LlmConfigDialog()
    {
        InitializeComponent();
        this.Loaded += OnLoaded;
        this.KeyDown += OnKeyDown;
        LoadDefaultValues();
    }

    private void LoadDefaultValues()
    {
        // 从全局配置加载默认值
        var aiEnvConfig = TaskContext.Instance().Config.AIEnvConfig;
        
        // 设置默认用户指令
        UserPromptTextBox.Text = aiEnvConfig.UserPrompt;
        
        // 设置默认配置值
        Config = new LlmSchedulerConfig
        {
            // API配置从全局配置读取
            ApiEndpoint = aiEnvConfig.ApiEndpoint,
            ApiKey = "", // API密钥不在这里设置
            ModelName = "", // 模型名称留空，适用于llama.cpp本地模型
            
            // 其他配置使用默认值
            MemoryMode = "NearDense",
            PromptMode = "General",
            EnableThought = false,
            OutputLengthControl = "Variable",
            CallTriggerType = aiEnvConfig.CallTriggerType,
            EndTriggerType = "LlmExit",
            MaxTokens = 1000,
            Temperature = 0.7,
            RecentFrameCount = 3,
            DistantFrameInterval = 2,
            FixedIntervalSeconds = 5
        };
        
        // 更新UI控件
        UpdateUIFromConfig();
    }

    private void UpdateUIFromConfig()
    {
        // 更新记忆模式
        foreach (ComboBoxItem item in MemoryModeComboBox.Items)
        {
            if (item.Content.ToString() == Config.MemoryMode)
            {
                item.IsSelected = true;
                break;
            }
        }

        // 更新Prompt模式
        foreach (ComboBoxItem item in PromptModeComboBox.Items)
        {
            if (item.Content.ToString() == Config.PromptMode)
            {
                item.IsSelected = true;
                break;
            }
        }

        // 更新其他控件
        EnableThoughtCheckBox.IsChecked = Config.EnableThought;
        
        foreach (ComboBoxItem item in OutputLengthComboBox.Items)
        {
            if (item.Content.ToString() == Config.OutputLengthControl)
            {
                item.IsSelected = true;
                break;
            }
        }

        foreach (ComboBoxItem item in CallTriggerComboBox.Items)
        {
            if (item.Content.ToString() == Config.CallTriggerType)
            {
                item.IsSelected = true;
                break;
            }
        }

        foreach (ComboBoxItem item in EndTriggerComboBox.Items)
        {
            if (item.Content.ToString() == Config.EndTriggerType)
            {
                item.IsSelected = true;
                break;
            }
        }

        MaxTokensNumberBox.Value = Config.MaxTokens;
        TemperatureNumberBox.Value = Config.Temperature;
        RecentFrameCountNumberBox.Value = Config.RecentFrameCount;
        DistantFrameIntervalNumberBox.Value = Config.DistantFrameInterval;
        FixedIntervalNumberBox.Value = Config.FixedIntervalSeconds;
    }

    private void UpdateConfigFromUI()
    {
        // 更新用户指令
        UserPrompt = UserPromptTextBox.Text?.Trim() ?? string.Empty;

        // 更新配置
        Config.MemoryMode = ((ComboBoxItem)MemoryModeComboBox.SelectedItem)?.Content.ToString() ?? "NearDense";
        Config.PromptMode = ((ComboBoxItem)PromptModeComboBox.SelectedItem)?.Content.ToString() ?? "General";
        Config.EnableThought = EnableThoughtCheckBox.IsChecked ?? false;
        Config.OutputLengthControl = ((ComboBoxItem)OutputLengthComboBox.SelectedItem)?.Content.ToString() ?? "Variable";
        Config.CallTriggerType = ((ComboBoxItem)CallTriggerComboBox.SelectedItem)?.Content.ToString() ?? "ActionGroupCompleted";
        Config.EndTriggerType = ((ComboBoxItem)EndTriggerComboBox.SelectedItem)?.Content.ToString() ?? "LlmExit";
        
        Config.MaxTokens = (int)(MaxTokensNumberBox.Value ?? 1000);
        Config.Temperature = TemperatureNumberBox.Value ?? 0.7;
        Config.RecentFrameCount = (int)(RecentFrameCountNumberBox.Value ?? 3);
        Config.DistantFrameInterval = (int)(DistantFrameIntervalNumberBox.Value ?? 2);
        Config.FixedIntervalSeconds = (int)(FixedIntervalNumberBox.Value ?? 5);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UserPromptTextBox.Focus();
        UserPromptTextBox.SelectAll();
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
            // Ctrl+Enter 确定
            BtnOkClick(sender, e);
        }
    }

    private void BtnOkClick(object sender, RoutedEventArgs e)
    {
        // 验证用户输入
        if (string.IsNullOrWhiteSpace(UserPromptTextBox.Text))
        {
            System.Windows.MessageBox.Show("请输入用户指令", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            UserPromptTextBox.Focus();
            return;
        }

        UpdateConfigFromUI();
        DialogResult = true;
        Close();
    }

    private void BtnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 显示LLM配置对话框
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <returns>配置结果，如果取消则返回null</returns>
    public static (string? userPrompt, LlmSchedulerConfig? config) ShowConfigDialog(Window? owner = null)
    {
        var dialog = new LlmConfigDialog
        {
            Owner = owner ?? Application.Current.MainWindow
        };

        var result = dialog.ShowDialog();
        return result == true ? (dialog.UserPrompt, dialog.Config) : (null, null);
    }
}

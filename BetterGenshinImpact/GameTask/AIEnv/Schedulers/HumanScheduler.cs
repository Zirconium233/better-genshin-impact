using BetterGenshinImpact.GameTask.AIEnv.Environment;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BetterGenshinImpact.GameTask.AIEnv.Schedulers;

/// <summary>
/// 人工调度器
/// 通过弹窗让用户手动输入动作脚本
/// </summary>
public class HumanScheduler : IAgentScheduler
{
    private readonly AIEnvParam _param;
    private readonly ILogger<HumanScheduler> _logger;
    
    private AIEnvironment? _env;
    private Task? _schedulerTask;
    private CancellationTokenSource? _cts;
    private string _userPrompt = string.Empty;
    private readonly object _promptLock = new();

    public bool IsRunning { get; private set; }

    public HumanScheduler(AIEnvParam param)
    {
        _param = param;
        _logger = App.GetLogger<HumanScheduler>();
    }

    public async Task Start(AIEnvironment env)
    {
        if (IsRunning)
        {
            _logger.LogWarning("人工调度器已经在运行中");
            return;
        }

        _env = env ?? throw new ArgumentNullException(nameof(env));
        _logger.LogInformation("启动人工调度器...");

        _cts = new CancellationTokenSource();
        _schedulerTask = Task.Run(SchedulerLoop, _cts.Token);
        IsRunning = true;

        _logger.LogInformation("人工调度器启动完成");
        await Task.CompletedTask;
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _logger.LogInformation("停止人工调度器...");

        IsRunning = false;
        _cts?.Cancel();

        try
        {
            _schedulerTask?.Wait(5000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "等待调度器任务完成时发生异常");
        }

        _cts?.Dispose();
        _logger.LogInformation("人工调度器已停止");
    }

    public void SendUserPrompt(string prompt)
    {
        lock (_promptLock)
        {
            _userPrompt = prompt ?? string.Empty;
            _logger.LogInformation("接收到用户指令: {Prompt}", prompt);
        }
    }

    public string GetStatus()
    {
        if (!IsRunning)
        {
            return "未启动";
        }

        return "等待用户输入";
    }

    /// <summary>
    /// 调度器主循环
    /// </summary>
    private async Task SchedulerLoop()
    {
        _logger.LogInformation("开始人工调度器循环");

        while (!_cts!.Token.IsCancellationRequested && IsRunning)
        {
            try
            {
                // 检查是否需要触发用户输入
                if (ShouldTriggerUserInput())
                {
                    await TriggerUserInput();
                }

                // 短暂等待
                await Task.Delay(500, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "调度器循环中发生异常");
                await Task.Delay(1000, _cts.Token);
            }
        }

        _logger.LogInformation("人工调度器循环已结束");
    }

    /// <summary>
    /// 判断是否应该触发用户输入
    /// </summary>
    private bool ShouldTriggerUserInput()
    {
        if (_env == null)
        {
            return false;
        }

        var queueStatus = _env.GetActionQueueStatus();
        
        // 当动作队列为空或只剩一个动作组时触发
        var remainingActions = queueStatus.RemainingActions;
        if (string.IsNullOrEmpty(remainingActions))
        {
            return true;
        }

        // 检查是否只剩一个动作组
        var actionGroups = remainingActions.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return actionGroups.Length <= 1;
    }

    /// <summary>
    /// 触发用户输入
    /// </summary>
    private async Task TriggerUserInput()
    {
        try
        {
            _logger.LogInformation("触发用户输入对话框");

            // 在UI线程上显示输入对话框
            string? userInput = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                userInput = ShowInputDialog();
            });

            if (!string.IsNullOrWhiteSpace(userInput))
            {
                // 处理特殊指令
                if (userInput.Trim().ToLower() == "exit")
                {
                    _logger.LogInformation("用户输入退出指令，停止调度器");
                    Stop();
                    return;
                }

                // 提交动作脚本到环境
                _env?.AddCommands(userInput);
                _logger.LogInformation("用户输入动作脚本: {ActionScript}", userInput);
            }
            else
            {
                _logger.LogInformation("用户取消输入或输入为空");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发用户输入时发生异常");
        }
    }

    /// <summary>
    /// 显示输入对话框
    /// </summary>
    private string? ShowInputDialog()
    {
        try
        {
            var dialog = new InputDialog
            {
                Title = "AI环境 - 人工调度器",
                Prompt = "请输入动作脚本 (例如: w(1.0),e 或 exit 退出):",
                DefaultValue = "",
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var result = dialog.ShowDialog();
            return result == true ? dialog.InputText : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示输入对话框时发生异常");
            return null;
        }
    }
}

/// <summary>
/// 简单的输入对话框
/// </summary>
public partial class InputDialog : Window
{
    public string InputText { get; private set; } = string.Empty;
    public string Prompt { get; set; } = "请输入:";
    public string DefaultValue { get; set; } = string.Empty;

    public InputDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void InitializeComponent()
    {
        Title = "输入";
        Width = 400;
        Height = 200;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

        // 提示文本
        var promptLabel = new System.Windows.Controls.Label
        {
            Content = Prompt,
            Margin = new Thickness(10)
        };
        System.Windows.Controls.Grid.SetRow(promptLabel, 0);
        grid.Children.Add(promptLabel);

        // 输入框
        var inputTextBox = new System.Windows.Controls.TextBox
        {
            Name = "InputTextBox",
            Text = DefaultValue,
            Margin = new Thickness(10, 0, 10, 10),
            Height = 25
        };
        System.Windows.Controls.Grid.SetRow(inputTextBox, 1);
        grid.Children.Add(inputTextBox);

        // 按钮面板
        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10)
        };

        var okButton = new System.Windows.Controls.Button
        {
            Content = "确定",
            Width = 75,
            Height = 25,
            Margin = new Thickness(0, 0, 10, 0),
            IsDefault = true
        };
        okButton.Click += (s, e) =>
        {
            InputText = inputTextBox.Text;
            DialogResult = true;
        };

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "取消",
            Width = 75,
            Height = 25,
            IsCancel = true
        };
        cancelButton.Click += (s, e) =>
        {
            DialogResult = false;
        };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        System.Windows.Controls.Grid.SetRow(buttonPanel, 2);
        grid.Children.Add(buttonPanel);

        Content = grid;

        // 设置焦点到输入框
        Loaded += (s, e) => inputTextBox.Focus();
    }
}

using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.DataCollector.Model;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.DataCollector;

/// <summary>
/// AI数据采集器任务
/// </summary>
public class DataCollectorTask : ISoloTask
{
    public string Name => "AI数据采集器";

    private readonly ILogger<DataCollectorTask> _logger = App.GetLogger<DataCollectorTask>();
    private readonly DataCollectorParam _taskParam;
    private readonly DataCollectorConfig _config;
    private readonly InputMonitor _inputMonitor;
    private readonly StateExtractor _stateExtractor;
    private readonly List<DataRecord> _dataBuffer = new();
    private readonly object _bufferLock = new();
    
    private CancellationToken _ct;
    private Timer? _collectionTimer;
    private int _frameIndex = 0;
    private long _sessionStartTime;
    private string _sessionPath = string.Empty;
    private string _framesPath = string.Empty;
    private long _lastNoActionFrameTime = 0;
    private bool _isCollecting = false;

    public DataCollectorTask(DataCollectorParam taskParam)
    {
        _taskParam = taskParam;
        _config = TaskContext.Instance().Config.DataCollectorConfig;
        _inputMonitor = new InputMonitor();
        _stateExtractor = new StateExtractor();
    }

    public async Task Start(CancellationToken ct)
    {
        _ct = ct;
        _logger.LogInformation("启动AI数据采集器");

        try
        {
            await InitializeSession();
            await StartCollection();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("数据采集被取消");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "数据采集过程中发生异常");
            throw;
        }
        finally
        {
            await Cleanup();
        }
    }

    /// <summary>
    /// 初始化会话
    /// </summary>
    private async Task InitializeSession()
    {
        _sessionStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // 创建会话目录
        _sessionPath = Path.Combine(Path.GetFullPath(_taskParam.DatasetPath), _taskParam.SessionId);
        _framesPath = Path.Combine(_sessionPath, "frames");
        
        Directory.CreateDirectory(_sessionPath);
        Directory.CreateDirectory(_framesPath);

        _logger.LogInformation("会话初始化完成: {SessionId}, 路径: {SessionPath}", 
            _taskParam.SessionId, _sessionPath);

        // 初始化输入监控
        var gameHandle = SystemControl.FindGenshinImpactHandle();
        if (gameHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("未找到原神游戏窗口");
        }

        _inputMonitor.StartMonitoring(gameHandle);
        _logger.LogInformation("输入监控已启动");

        // 初始化状态提取器
        using var imageRegion = TaskControl.CaptureToRectArea();
        _stateExtractor.InitializeCombatScenes(imageRegion);
        _logger.LogInformation("状态提取器已初始化");
    }

    /// <summary>
    /// 开始数据采集
    /// </summary>
    private async Task StartCollection()
    {
        _isCollecting = true;
        var collectionInterval = 1000 / _taskParam.CollectionFps; // 毫秒
        
        _collectionTimer = new Timer(CollectFrame, null, 0, collectionInterval);
        _logger.LogInformation("数据采集已启动, FPS: {Fps}, 间隔: {Interval}ms", 
            _taskParam.CollectionFps, collectionInterval);

        // 等待取消信号或游戏失焦
        while (!_ct.IsCancellationRequested && _isCollecting)
        {
            await Task.Delay(1000, _ct);
            
            // 检查游戏是否失焦
            if (_config.StopOnGameUnfocused && !SystemControl.IsGenshinImpactActive())
            {
                _logger.LogInformation("游戏失焦，停止数据采集");
                break;
            }

            // 检查内存使用量
            CheckMemoryUsage();
        }
    }

    /// <summary>
    /// 采集单帧数据
    /// </summary>
    private void CollectFrame(object? state)
    {
        if (!_isCollecting || _ct.IsCancellationRequested)
            return;

        try
        {
            using var imageRegion = TaskControl.CaptureToRectArea();
            
            // 检测玩家动作
            var playerAction = _inputMonitor.DetectPlayerAction();
            
            // 检查是否需要采集无动作帧
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (playerAction == null && !_taskParam.CollectNoActionFrames)
            {
                // 检查是否需要采集无动作帧
                if (currentTime - _lastNoActionFrameTime < _taskParam.NoActionFrameInterval)
                {
                    return; // 跳过此帧
                }
                _lastNoActionFrameTime = currentTime;
            }

            // 提取结构化状态
            var structuredState = _stateExtractor.ExtractStructuredState(imageRegion);

            // 保存截图
            var framePath = SaveScreenshot(imageRegion.SrcMat);

            // 创建数据记录
            var dataRecord = new DataRecord
            {
                SessionId = _taskParam.SessionId,
                FrameIndex = _frameIndex++,
                TimeOffsetMs = currentTime - _sessionStartTime,
                FramePath = framePath,
                PlayerAction = playerAction,
                StructuredState = structuredState
            };

            // 添加到缓冲区
            lock (_bufferLock)
            {
                _dataBuffer.Add(dataRecord);
            }

            if (_taskParam.DebugMode && _frameIndex % 100 == 0)
            {
                _logger.LogDebug("已采集 {FrameCount} 帧数据", _frameIndex);
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "采集帧数据时发生异常");
        }
    }

    /// <summary>
    /// 保存截图
    /// </summary>
    private string SaveScreenshot(Mat image)
    {
        if (!_taskParam.SaveRawScreenshots)
            return string.Empty;

        var fileName = $"frame_{_frameIndex:D8}.jpg";
        var filePath = Path.Combine(_framesPath, fileName);

        // 异步保存以避免阻塞采集线程
        Task.Run(() =>
        {
            try
            {
                Mat imageToSave = image;

                // 检查是否需要遮罩UID
                if (TaskContext.Instance().Config.CommonConfig.ScreenshotUidCoverEnabled)
                {
                    imageToSave = image.Clone();
                    var assetScale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
                    var rect = new Rect(
                        (int)(image.Width - MaskWindowConfig.UidCoverRightBottomRect.X * assetScale),
                        (int)(image.Height - MaskWindowConfig.UidCoverRightBottomRect.Y * assetScale),
                        (int)(MaskWindowConfig.UidCoverRightBottomRect.Width * assetScale),
                        (int)(MaskWindowConfig.UidCoverRightBottomRect.Height * assetScale)
                    );
                    imageToSave.Rectangle(rect, Scalar.White, -1);
                }

                // 压缩保存
                var encodeParams = new int[] { (int)ImwriteFlags.JpegQuality, _taskParam.ScreenshotQuality };
                imageToSave.ImWrite(filePath, encodeParams);

                // 如果创建了副本，需要释放
                if (imageToSave != image)
                {
                    imageToSave.Dispose();
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "保存截图时发生异常: {FilePath}", filePath);
            }
        });

        return Path.Combine("frames", fileName); // 返回相对路径
    }

    /// <summary>
    /// 检查内存使用量
    /// </summary>
    private void CheckMemoryUsage()
    {
        var process = Process.GetCurrentProcess();
        var memoryUsageMb = process.WorkingSet64 / 1024 / 1024;
        
        if (memoryUsageMb > _taskParam.MaxMemoryUsageMb)
        {
            _logger.LogWarning("内存使用量超过限制: {Current}MB > {Max}MB，触发OOM保护", 
                memoryUsageMb, _taskParam.MaxMemoryUsageMb);
            
            throw new OutOfMemoryException($"内存使用量超过限制: {memoryUsageMb}MB");
        }

        if (_taskParam.DebugMode && memoryUsageMb > _taskParam.MaxMemoryUsageMb * 0.8)
        {
            _logger.LogDebug("内存使用量警告: {Current}MB (限制: {Max}MB)", 
                memoryUsageMb, _taskParam.MaxMemoryUsageMb);
        }
    }

    /// <summary>
    /// 清理资源
    /// </summary>
    private async Task Cleanup()
    {
        _logger.LogInformation("开始清理资源");
        
        _isCollecting = false;
        _collectionTimer?.Dispose();
        _inputMonitor?.Dispose();

        // 保存数据到文件
        if (_dataBuffer.Count > 0)
        {
            await SaveDataToFile();
        }
        else
        {
            _logger.LogWarning("没有采集到任何数据");
        }

        _logger.LogInformation("资源清理完成");
    }

    /// <summary>
    /// 保存数据到文件
    /// </summary>
    private async Task SaveDataToFile()
    {
        try
        {
            var dataFilePath = Path.Combine(_sessionPath, "data.jsonl");
            var metadataFilePath = Path.Combine(_sessionPath, "metadata.json");

            // 保存数据记录
            using var writer = new StreamWriter(dataFilePath);

            // 复制数据到临时列表以避免在lock中使用await
            List<DataRecord> tempBuffer;
            lock (_bufferLock)
            {
                tempBuffer = new List<DataRecord>(_dataBuffer);
            }

            foreach (var record in tempBuffer)
            {
                var json = JsonConvert.SerializeObject(record, Formatting.None);
                await writer.WriteLineAsync(json);
            }

            // 保存元数据
            var metadata = new
            {
                session_id = _taskParam.SessionId,
                start_time = _sessionStartTime,
                end_time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                total_frames = _frameIndex,
                collection_fps = _taskParam.CollectionFps,
                dataset_version = "1.0"
            };

            var metadataJson = JsonConvert.SerializeObject(metadata, Formatting.Indented);
            await File.WriteAllTextAsync(metadataFilePath, metadataJson);

            // 计算数据集大小
            var datasetSize = CalculateDirectorySize(_sessionPath);
            
            _logger.LogInformation("数据集保存完成:");
            _logger.LogInformation("  会话ID: {SessionId}", _taskParam.SessionId);
            _logger.LogInformation("  保存路径: {Path}", _sessionPath);
            _logger.LogInformation("  总帧数: {Frames}", _frameIndex);
            _logger.LogInformation("  数据集大小: {Size:F2} MB", datasetSize / 1024.0 / 1024.0);
            _logger.LogInformation("  数据文件: {DataFile}", dataFilePath);
            _logger.LogInformation("  元数据文件: {MetadataFile}", metadataFilePath);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "保存数据到文件时发生异常");
            throw;
        }
    }

    /// <summary>
    /// 计算目录大小
    /// </summary>
    private long CalculateDirectorySize(string directoryPath)
    {
        var directory = new DirectoryInfo(directoryPath);
        return directory.GetFiles("*", SearchOption.AllDirectories)
            .Sum(file => file.Length);
    }
}

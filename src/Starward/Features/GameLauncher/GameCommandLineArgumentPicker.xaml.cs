using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 命令行参数勾选面板：展示常用 Unity/社区预设，勾选或改取值后回写组合结果。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class GameCommandLineArgumentPicker : UserControl
{

    private readonly List<GameCommandLineArgumentOption> _options;

    private string _customArgument = "";

    private bool _suppressCombinedSync;

    private bool _suppressOptionSync;

    /// <summary>加载初始参数时不向父级回写，避免仅打开面板就把配置标脏。</summary>
    private bool _suppressCombinedNotify;

    private bool _isDx12ManagedByApp;

    private bool _isDx12Enabled;


    public GameCommandLineArgumentPicker()
    {
        _options = GameCommandLineArgumentHelper.CreateOptions();
        Groups = GameCommandLineArgumentHelper.CreateGroups(_options);
        foreach (GameCommandLineArgumentOption option in _options)
        {
            option.PropertyChanged += Option_PropertyChanged;
        }
        this.InitializeComponent();
    }


    /// <summary>分类后的预设列表。</summary>
    public ObservableCollection<GameCommandLineArgumentGroup> Groups { get; }


    /// <summary>
    /// 组合后的完整命令行参数。外部可 TwoWay 绑定到配置文件的 Argument 字段。
    /// </summary>
    public string? CombinedArgument
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && !_suppressCombinedNotify)
            {
                CombinedArgumentChanged?.Invoke(this, value);
            }
        }
    }


    /// <summary>组合结果变更（勾选或手动编辑输入框）。</summary>
    public event EventHandler<string?>? CombinedArgumentChanged;


    /// <summary>
    /// 用已有命令行字符串初始化勾选状态与自定义残余段。
    /// </summary>
    /// <param name="argument">当前配置文件中的命令行参数。</param>
    /// <param name="isDx12ManagedByApp">
    /// 为 true 时表示 <c>-use-d3d12</c> 由应用全局 DX12 开关附加（如绝区零），
    /// 列表项置灰、勾选状态与全局开关同步，且不写入配置文件参数。
    /// </param>
    /// <param name="isDx12Enabled">全局 DX12 是否已开启（仅在 <paramref name="isDx12ManagedByApp"/> 时生效）。</param>
    public void LoadFromArgument(string? argument, bool isDx12ManagedByApp = false, bool isDx12Enabled = false)
    {
        _isDx12ManagedByApp = isDx12ManagedByApp;
        _isDx12Enabled = isDx12Enabled;
        _suppressOptionSync = true;
        _suppressCombinedNotify = true;
        try
        {
            _customArgument = GameCommandLineArgumentHelper.ApplyFromArgumentString(_options, argument);
            ApplyDx12ManagedState();
            // 面板内预览与外部输入框保持一致（含未识别参数的原始写法），避免打开时重排写回
            // 全局管理的 -use-d3d12 不进入配置参数，从预览中剥离以免误导
            string? preview = argument;
            if (_isDx12ManagedByApp)
            {
                preview = StripManagedDx12Tokens(preview);
            }
            CombinedArgument = string.IsNullOrWhiteSpace(preview) ? null : preview.Trim();
        }
        finally
        {
            _suppressOptionSync = false;
            _suppressCombinedNotify = false;
        }
    }


    /// <summary>
    /// 同步 force_d3d12 项：全局管理时只读展示，勾选跟全局 DX12；否则恢复可编辑。
    /// </summary>
    private void ApplyDx12ManagedState()
    {
        GameCommandLineArgumentOption? d3d12 = _options.FirstOrDefault(o => o.Id == "force_d3d12");
        if (d3d12 is null)
        {
            return;
        }
        if (_isDx12ManagedByApp)
        {
            d3d12.IsEnabled = false;
            d3d12.IncludeInBuild = false;
            d3d12.IsSelected = _isDx12Enabled;
            // 配置里若曾手写 -use-d3d12 / -force-d3d12，从自定义残余中去掉，避免重复
            _customArgument = StripManagedDx12Tokens(_customArgument) ?? "";
        }
        else
        {
            d3d12.IsEnabled = true;
            d3d12.IncludeInBuild = true;
            // IsSelected 已由 ApplyFromArgumentString 解析，保持不变
        }
    }


    /// <summary>
    /// 去掉由全局 DX12 管理的 token，保留其余参数顺序。
    /// </summary>
    private static string? StripManagedDx12Tokens(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return argument;
        }
        List<string> tokens = GameCommandLineArgumentHelper.Tokenize(argument);
        tokens.RemoveAll(t =>
            t.Equals("-use-d3d12", StringComparison.OrdinalIgnoreCase)
            || t.Equals("-force-d3d12", StringComparison.OrdinalIgnoreCase));
        return tokens.Count == 0 ? null : string.Join(' ', tokens);
    }


    private void Option_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressOptionSync)
        {
            return;
        }
        if (e.PropertyName is nameof(GameCommandLineArgumentOption.IsSelected)
            or nameof(GameCommandLineArgumentOption.Value))
        {
            if (sender is GameCommandLineArgumentOption option
                && option.IsSelected
                && option.IsEnabled
                && !string.IsNullOrEmpty(option.ExclusiveGroup)
                && e.PropertyName == nameof(GameCommandLineArgumentOption.IsSelected))
            {
                ApplyExclusiveGroup(option);
            }
            // 只读全局项的勾选变化不驱动组合结果重写
            if (sender is GameCommandLineArgumentOption { IncludeInBuild: false }
                && e.PropertyName == nameof(GameCommandLineArgumentOption.IsSelected))
            {
                return;
            }
            PushCombinedFromOptions();
        }
    }


    private void ApplyExclusiveGroup(GameCommandLineArgumentOption selected)
    {
        _suppressOptionSync = true;
        try
        {
            foreach (GameCommandLineArgumentOption o in _options)
            {
                // 跳过置灰的全局管理项，避免被其它图形选项互斥清掉展示状态
                if (!o.IsEnabled)
                {
                    continue;
                }
                if (!ReferenceEquals(o, selected)
                    && o.ExclusiveGroup == selected.ExclusiveGroup
                    && o.IsSelected)
                {
                    o.IsSelected = false;
                }
            }
        }
        finally
        {
            _suppressOptionSync = false;
        }
    }


    private void PushCombinedFromOptions()
    {
        string built = GameCommandLineArgumentHelper.BuildArgumentString(_options, _customArgument);
        _suppressCombinedSync = true;
        try
        {
            CombinedArgument = string.IsNullOrWhiteSpace(built) ? null : built;
        }
        finally
        {
            _suppressCombinedSync = false;
        }
    }


    private void Option_CheckedChanged(object sender, RoutedEventArgs e)
    {
        // PropertyChanged 已处理；此处理器保证部分模板场景下也能刷新
        if (!_suppressOptionSync)
        {
            PushCombinedFromOptions();
        }
    }


    private void OptionValue_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressOptionSync)
        {
            return;
        }
        PushCombinedFromOptions();
    }


    private void CombinedArgument_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressCombinedSync)
        {
            return;
        }
        // 用户直接改组合结果：重新解析勾选，保留无法识别的部分为 custom
        string text = (sender as TextBox)?.Text ?? CombinedArgument ?? "";
        _suppressOptionSync = true;
        try
        {
            _customArgument = GameCommandLineArgumentHelper.ApplyFromArgumentString(_options, text);
            ApplyDx12ManagedState();
            if (_isDx12ManagedByApp)
            {
                text = StripManagedDx12Tokens(text) ?? "";
            }
            // 不反向再 Build，避免改写用户正在输入的空白/顺序（仅剥离全局 DX12 token）
            if (!string.Equals(CombinedArgument, text, StringComparison.Ordinal))
            {
                CombinedArgument = string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        finally
        {
            _suppressOptionSync = false;
        }
    }

}

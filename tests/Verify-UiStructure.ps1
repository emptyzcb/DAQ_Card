$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$mainForm = Get-Content -LiteralPath (Join-Path $root 'MainForm.cs') -Raw
$libraryRoot = Join-Path $root 'Library'

function Assert-Contains([string]$text, [string]$pattern, [string]$message) {
    if ($text -notmatch $pattern) {
        throw $message
    }
}

function Assert-NotContains([string]$text, [string]$pattern, [string]$message) {
    if ($text -match $pattern) {
        throw $message
    }
}

Assert-Contains $mainForm 'BuildWorkspaceWithLog\(\)' '主窗口必须使用全局底部日志工作区。'
Assert-NotContains $mainForm 'monitorSplit\.Panel2\.Controls\.Add\(BuildLogPanel\(\)\)' '实时监视页不得再单独嵌套通信日志。'
Assert-Contains $mainForm 'BuildRs485Page\(\)' '上位机必须提供 RS485 传感器动态配置页。'
Assert-Contains $mainForm 'Rs485ConfigurationProtocol' 'RS485 配置必须通过独立协议服务下载。'
Assert-NotContains $mainForm 'OnWriteOutputClicked' '不得绕过 IO 联合控制器直接写数字输出。'
Assert-NotContains $mainForm 'System\.IO\.Ports' '以太网版上位机不得继续依赖串口客户端。'
Assert-Contains $mainForm 'class CodeMinimap' '代码编辑器必须实现右侧缩略预览控件。'
Assert-Contains $mainForm 'codeMinimap\.Dock\s*=\s*DockStyle\.Right' '代码缩略预览必须停靠在编辑器右侧。'
Assert-Contains $mainForm 'codeMinimap\.Invalidate\(\)' '代码变化或滚动时必须刷新缩略预览。'
Assert-Contains $mainForm 'DrawString\(' 'minimap 必须绘制真实缩小代码字符，不能只画行长横线。'
Assert-NotContains $mainForm 'e\.Graphics\.DrawLine\(pen, x, y, x \+ width, y\)' 'minimap 不得继续使用横线代替代码字符。'
Assert-NotContains $mainForm 'var logFooter' '通信日志不得保留占用整行的底部按钮区。'
Assert-Contains $mainForm 'BuildLogHeader\(\)' '清空日志按钮应放入日志标题栏。'

$expectedLibraries = @(
    'io_logic_library.h',
    'io_logic_types.h',
    'io_logic_input.h',
    'io_logic_output.h',
    'io_logic_timer.h',
    'io_logic_analog.h'
)
foreach ($library in $expectedLibraries) {
    if (-not (Test-Path -LiteralPath (Join-Path $libraryRoot $library))) {
        throw "缺少分类库文件：$library"
    }
}
Assert-Contains (Get-Content -LiteralPath (Join-Path $libraryRoot 'io_logic_types.h') -Raw) 'RELAY_1\s+1' '数字输出库必须定义板级命名常量。'
Assert-Contains (Get-Content -LiteralPath (Join-Path $libraryRoot 'io_logic_analog.h') -Raw) 'IO_AO_SetVoltage' '规则库必须支持 0~10 V 输出。'
Assert-Contains (Get-Content -LiteralPath (Join-Path $libraryRoot 'io_logic_analog.h') -Raw) 'IO_AO_SetCurrent' '规则库必须支持 0~20 mA 输出。'
if (Test-Path -LiteralPath (Join-Path $libraryRoot 'io_logic_library.c')) {
    throw '旧的单文件 io_logic_library.c 必须移除。'
}

Write-Output 'UI structure checks passed.'

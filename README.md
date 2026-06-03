# HeartRateMonitor — BLE 多设备心率对比工具

Windows 桌面应用，支持同时连接多个蓝牙心率设备，实时对比心率数据并进行 HRV 分析。

## 功能特性

### 蓝牙设备管理
- **BLE 心率设备扫描**：扫描附近广播 Heart Rate Service (0x180D) 的蓝牙设备，15 秒自动停止
- **多设备同时连接**：支持同时连接多个心率带、手环、手表等设备
- **设备自动清理**：扫描到的设备 30 秒内未连接则自动从列表移除
- **已连接设备保护**：已连接设备不会出现在扫描列表中
- **自动重连**：设备意外断开后自动尝试重连（最多 5 次，间隔 3 秒）

### 实时数据展示
- **心率曲线图**：基于 OxyPlot 的实时心率曲线，每条曲线带半透明色块填充
- **X / Y 轴自定义**：默认显示最近 10 秒、Y 轴 40-100 bpm，可手动调整
- **显示全部模式**：切换后查看完整历史数据
- **数据表格**：实时显示各设备的心率、RR 间期、SDNN、RMSSD、电量
- **自适应列显示**：RR / SDNN / RMSSD 列在无数据时自动隐藏
- **设备名称滚动**：名称过长时自动滚动显示完整内容

### HRV 分析
- **SDNN**：RR 间期标准差，最基本的 HRV 指标
- **RMSSD**：相邻 RR 间期差值的均方根，反映副交感神经活性
- **DFA 分析**：Detrended Fluctuation Analysis，计算短期 (α1, 4-16拍) 和长期 (α2, 16-64拍) 分形标度指数
- **自适应异常值过滤**：基于滑动中值的 RR 间期异常值剔除

### 数据记录与导出
- **手动记录控制**：点击"记录"按钮开始采集数据，再次点击停止
- **CSV 导出**：支持导出历史数据或当前快照，可选择导出字段
- **自动保存**（已移除）：早期版本支持，当前版本改为手动记录

### 界面设计
- **Fluent 风格圆角**：全局 CornerRadius=10 的圆角设计
- **颜色自定义**：红、橙、绿、蓝、紫、黑 6 种颜色可选
- **圆角滚动条**：自定义 ScrollBar 样式，轨道和滑块均为圆角
- **文字链接交互**：扫描列表使用"连接"文字链接，数据表格使用"断开"文字链接

## 系统要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows 10 1803+ 或 Windows 11 |
| .NET 运行时 | .NET 8.0（自包含发布则无需安装） |
| 蓝牙 | Bluetooth 4.0+ LE 适配器 |
| 开发环境 | Visual Studio 2022 / .NET 8 SDK |

## 项目架构

```
HeartRateMonitor/
├── Models/
│   ├── DeviceModel.cs          # BLE 设备数据模型（心率、RR、HRV、电量）
│   ├── DeviceStatus.cs         # 设备状态枚举
│   ├── HeartRateDataPoint.cs   # 心率数据点
│   ├── HrvMetrics.cs           # HRV 分析指标
│   └── ColorOption.cs          # 颜色选项
├── Services/
│   ├── BluetoothService.cs     # BLE 扫描、连接、数据解析、自动重连
│   ├── ExportService.cs        # CSV 数据导出
│   └── HrvAnalysisService.cs   # HRV 时域 + DFA 分析算法
├── ViewModels/
│   ├── MainViewModel.cs        # 主窗口逻辑（扫描、连接、记录、图表、导出）
│   ├── ObservableObject.cs     # INotifyPropertyChanged 基类
│   └── RelayCommand.cs         # ICommand 实现
├── App.xaml/.cs                # 应用入口 + 全局异常处理
├── MainWindow.xaml/.cs         # 主窗口视图
├── BoolToGridLengthConverter.cs  # Bool → GridLength 转换器（动态隐藏列）
├── OxyColorToBrushConverter.cs   # OxyColor → Brush 转换器
├── ExportOptionsDialog.xaml/.cs  # 导出选项对话框
├── ConfirmDialog.xaml/.cs        # 确认对话框
└── MessageDialog.xaml/.cs        # 消息对话框
```

### 架构模式

项目采用 **MVVM** 架构：
- **Model**：`DeviceModel` 实现 `INotifyPropertyChanged`，支持 WPF 数据绑定
- **ViewModel**：`MainViewModel` 通过 `StateFlow` / 事件驱动管理所有状态
- **View**：`MainWindow.xaml` 纯声明式 UI，通过绑定和命令与 ViewModel 交互

### BLE 通信流程

```
扫描阶段:
  BluetoothLEAdvertisementWatcher (Active模式, UUID过滤)
    → OnAdvertisementReceived → BleScanResult
    → ViewModel.OnDeviceDiscovered → 扫描列表更新

连接阶段:
  BluetoothLEDevice.FromBluetoothAddressAsync
    → GetGattServicesAsync
    → SubscribeHeartRate (Notify)
    → SubscribeBattery (Read + Notify)
    → ConnectionStatusChanged 监听

数据解析:
  HeartRate Measurement (UUID 0x2A37):
    → Flags → UINT8/UINT16 心率值
    → Energy Expended (可选)
    → RR-Interval (1/1024秒精度, 300-2000ms 范围过滤)

  Battery Level (UUID 0x2A19):
    → 单字节百分比值
```

## 使用指南

### 1. 扫描设备

点击左上角 **"扫描设备"** 按钮，按钮变为 **"扫描中..."**，15 秒后自动停止。扫描到的设备出现在列表中，每个设备右侧有 **"连接"** 文字链接。

> 已连接的设备不会出现在扫描列表中。未连接的设备 30 秒后自动清除。

### 2. 连接设备

点击设备右侧的 **"连接"**，设备出现在下方 **"实时数据"** 表格中，分配默认颜色。

### 3. 开始记录

点击 **"记录"** 按钮，按钮变为 **"记录中..."**，图表开始绘制心率曲线。再次点击停止记录。

### 4. 查看数据

- **左侧表格**：显示设备名称、心率、RR、SDNN、RMSSD、电量、颜色
- **右侧图表**：实时心率曲线 + 半透明色块，鼠标悬停设备名称可查看完整名称
- **图例行**：图表上方的复选框可控制各设备曲线的显示/隐藏

### 5. 调整图表

- **Y 轴范围**：在控制栏输入最小值和最大值，点击"应用"
- **显示全部**：切换按钮查看全部历史数据
- **清空曲线**：清除所有历史数据并重置计时

### 6. 断开设备

点击设备行右侧的 **"断开"** 文字链接。最后一个设备断开时，记录自动停止。

### 7. 导出数据

点击 **"导出数据"** 按钮，选择导出类型和字段，保存为 CSV 文件。

## 技术栈

| 组件 | 技术 |
|------|------|
| UI 框架 | WPF (.NET 8) |
| 图表库 | OxyPlot.Wpf 2.2.0 |
| BLE 通信 | Windows.Devices.Bluetooth (UWP API) |
| 架构模式 | MVVM |
| 发布方式 | 自包含单文件 exe (win-x64) |

## 构建与发布

### 开发构建

```bash
dotnet build
```

### 发布单文件 exe

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true
```

产物位于 `bin/Release/net8.0-windows10.0.26100.0/win-x64/publish/HeartRateMonitor.exe`。

## HRV 指标说明

| 指标 | 说明 | 典型值 |
|------|------|--------|
| SDNN | RR 间期标准差，反映整体 HRV | 健康成人 50-100ms |
| RMSSD | 相邻 RR 差值均方根，反映副交感神经活性 | 健康成人 20-60ms |
| DFA α1 | 短期分形标度指数 (4-16拍) | 健康静息 ≈ 1.0 |
| DFA α2 | 长期分形标度指数 (16-64拍) | 需较长数据，通常 > 0.5 |

## 应用场景

- **运动科学**：对比不同心率设备的测量准确性
- **设备测试**：验证心率带、手表等设备的数据一致性
- **算法研究**：收集多设备 RR 间期数据用于 HRV 算法验证
- **教学演示**：直观展示心率变化和 HRV 指标的实时计算

## 许可证

MIT License

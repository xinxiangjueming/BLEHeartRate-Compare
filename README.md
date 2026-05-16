# BLEHeartRate-Compare

<img width="1915" height="991" alt="image" src="https://github.com/user-attachments/assets/41017c84-32a2-452b-a45d-062b2657cb1b" />

## 功能特性

- **多设备同时连接**：支持同时连接多个蓝牙心率设备（如心率带、手环等），实时显示各设备数据
- **实时心率曲线**：每个设备对应一条彩色曲线，可直观对比心率变化趋势
- **数据显示表格**：实时显示设备的心率、RR间期、SDNN、RMSSD、DFA α1/α2、电量等信息
- **HRV 分析**：支持 SDNN、RMSSD、SDSD、pNN50、pNN20 等时域 HRV 指标计算
- **DFA 分析**：Detrended Fluctuation Analysis，计算短期 (α1, 4-16拍) 和长期 (α2, 16-64拍) 分形标度指数
- **自适应异常值过滤**：基于滑动中值的 RR 间期异常值剔除，高 HRV 场景下误删率低
- **颜色自定义**：提供红、橙、绿、蓝、紫、黑6种颜色可选，每个设备默认分配不同颜色
- **Y轴范围调节**：可自定义Y轴范围（默认40-180），支持重置和清空曲线
- **数据导出**：支持导出历史数据为CSV格式，可选择导出字段（心率、RR、SDNN、RMSSD、DFA α1/α2、电量）
- **自动保存**：连接首个设备后自动提示保存路径，每秒将实时数据写入 CSV 文件
- **自动重连**：设备断开后自动尝试重连（最多5次，间隔3秒）
- **圆角UI设计**：整体界面柔和舒适，所有对话框均采用圆角设计

## 系统要求

- 操作系统：Windows 10 或更高版本（需支持蓝牙4.0及以上）
- 开发环境：Visual Studio 2022
- .NET版本：.NET 8.0
- 蓝牙适配器：需要电脑具备蓝牙功能

## 项目架构

项目采用 MVVM 架构：

```
HeartRateMonitor/
├── Models/               # 数据模型
│   ├── DeviceModel.cs        # BLE 设备数据模型
│   ├── DeviceStatus.cs       # 设备状态枚举
│   ├── HeartRateDataPoint.cs # 心率数据点
│   └── HrvMetrics.cs         # HRV 分析指标
├── Services/             # 业务服务层
│   ├── BluetoothService.cs   # BLE 蓝牙通信服务
│   ├── ExportService.cs      # CSV 数据导出服务
│   └── HrvAnalysisService.cs # HRV 分析算法
├── ViewModels/           # 视图模型
│   ├── MainViewModel.cs      # 主窗口 ViewModel
│   ├── ObservableObject.cs   # ViewModel 基类
│   └── RelayCommand.cs       # 命令实现
├── MainWindow.xaml/.cs   # 主窗口视图
├── ExportOptionsDialog   # 导出选项对话框
├── ConfirmDialog         # 确认对话框
└── MessageDialog         # 消息对话框
```

## 使用指南

1. **扫描设备**
   点击"扫描设备"按钮，程序将搜索附近的蓝牙心率设备。

2. **连接设备**
   - 在设备列表中选择一个设备
   - 双击或点击"连接设备"按钮
   - 每个新连接设备会默认分配不同颜色循环
   - 首次连接时会弹出保存对话框，选择 CSV 文件路径后自动保存即开始

3. **查看实时数据**
   - 左侧表格：实时显示各设备的心率、RR、SDNN、RMSSD、电量
   - 右侧图表：实时心率曲线，每个设备用其对应颜色显示
   - 颜色切换：可在表格最后一列的下拉框中随时更换设备颜色

4. **图表操作**
   - 显示全部：切换按钮可查看所有历史数据或最近60秒数据
   - Y轴范围：可自定义Y轴最小值/最大值，点击"应用"生效
   - 清空曲线：清除所有历史数据并重新计时

5. **数据导出**
   点击"导出数据"按钮，可选择：
   - 导出类型：历史数据（按时间行输出）或当前快照（每个设备一行）
   - 导出字段：设备、心率、RR、SDNN、RMSSD、DFA α1、DFA α2、电量

## HRV 指标说明

| 指标 | 全称 | 说明 |
|------|------|------|
| SDNN | Standard Deviation of NN intervals | RR间期标准差，最基本的HRV指标 |
| RMSSD | Root Mean Square of Successive Differences | 相邻RR间期差值的均方根，反映副交感神经活性 |
| SDSD | Standard Deviation of Successive Differences | 相邻RR间期差值的标准差 |
| pNN50 | Percentage of successive RR intervals differing >50ms | 相邻RR间期差值超过50ms的百分比 |
| pNN20 | Percentage of successive RR intervals differing >20ms | 相邻RR间期差值超过20ms的百分比 |
| DFA α1 | Detrended Fluctuation Analysis short-term | 短期标度指数 (4-16拍)，反映短期分形相关性，健康值 ≈ 1.0 |
| DFA α2 | Detrended Fluctuation Analysis long-term | 长期标度指数 (16-64拍)，反映长期分形相关性，需较长数据 |

## 应用场景

- **运动科学研究**：对比不同心率设备的准确性
- **医疗设备测试**：验证新开发的心率设备性能
- **心率算法研究**：收集多个设备的RR间期数据用于算法验证
- **教育演示**：直观展示心率变化和HRV指标

# claw0.net

.NET 版本 [claw0](https://github.com/shareAI-lab/claw0) - 从 0 到 1 构建 OpenClaw 风格的 AI Agent Gateway

10 个渐进式阶段, 每个阶段引入一个核心机制. 每个阶段都是一个可独立运行的程序.

使用 **DeepSeek API** (OpenAI 兼容格式).

## 架构概览

```
+--------- claw0 architecture ---------+
|                                      |
| s10: Delivery Queue (可靠投递)       |
| s09: Cron Scheduler (定时任务)       |
| s08: Heartbeat (主动行为)            |
| s07: Soul & Memory (人格 + 记忆)     |
| s06: Routing (多 Agent 路由)         |
| s05: Gateway (WebSocket/HTTP 网关)   |
| s04: Multi-Channel (多通道抽象)      |
| s03: Sessions (会话持久化)           |
| s02: Tools (bash/read/write/edit)    |
| s01: Agent Loop (while + stop_reason)|
|                                      |
+--------------------------------------+
```

## 快速开始

### 1. 克隆并配置

```bash
git clone <repository>
cd claw0.net
```

### 2. 配置 DeepSeek API Key

编辑 `appsettings.json`:

```json
{
  "DEEPSEEK_API_KEY": "sk-xxxxx",
  "MODEL_ID": "deepseek-chat"
}
```

或使用环境变量:

```bash
# Windows PowerShell
$env:DEEPSEEK_API_KEY="sk-xxxxx"

# Windows CMD
set DEEPSEEK_API_KEY=sk-xxxxx

# Linux/macOS
export DEEPSEEK_API_KEY=sk-xxxxx
```

获取 DeepSeek API Key: https://platform.deepseek.com/

### 3. 运行任意阶段

```bash
# 运行第 1 阶段: Agent Loop (最基础的对话循环)
dotnet run 01

# 运行第 2 阶段: Tool Use (添加工具调用)
dotnet run 02

# 运行第 3 阶段: Sessions (会话持久化)
dotnet run 03

# 运行第 10 阶段: Delivery Queue (完整功能)
dotnet run 10
```

## 各阶段说明

| # | Section | 格言 | 核心机制 | 新概念 |
|---|---------|------|----------|--------|
| 01 | Agent Loop | "One loop to rule them all" | while + stop_reason | LLM API, 消息历史 |
| 02 | Tool Use | "Give the model hands" | TOOL_HANDLERS 调度表 | 工具 Schema, 安全执行 |
| 03 | Sessions | "Conversations that survive restarts" | SessionStore + JSONL | 持久化, 会话 key |
| 04 | Multi-Channel | "Same brain, many mouths" | Channel 插件接口 | 抽象, 标准化 |
| 05 | Gateway Server | "The switchboard" | WebSocket + JSON-RPC | 服务器架构, RPC |
| 06 | Routing | "Every message finds its home" | Binding 解析 | 多 Agent, 路由优先级 |
| 07 | Soul & Memory | "Give it a soul, let it remember" | SOUL.md + MemoryStore | 人格, 向量搜索 |
| 08 | Heartbeat | "Not just reactive - proactive" | HeartbeatRunner | 主动行为 |
| 09 | Cron Scheduler | "The right thing at the right time" | CronService + 3 种类型 | at/every/cron |
| 10 | Delivery Queue | "Messages never get lost" | DeliveryQueue + 退避 | At-least-once, 磁盘队列 |

## 项目结构

```
claw0.net/
├── Agents/                 # 各阶段实现
│   ├── S01_AgentLoop.cs   # 阶段 1
│   ├── S02_ToolUse.cs     # 阶段 2
│   ├── S03_Sessions.cs    # 阶段 3
│   ├── S04_MultiChannel.cs
│   ├── S05_Gateway.cs
│   ├── S06_Routing.cs
│   ├── S07_SoulMemory.cs
│   ├── S08_Heartbeat.cs
│   ├── S09_Cron.cs
│   └── S10_Delivery.cs    # 阶段 10
├── Channels/              # 通道插件
│   ├── IChannel.cs
│   ├── CliChannel.cs
│   ├── FileChannel.cs
│   └── ChannelRegistry.cs
├── Common/                # 通用工具
│   ├── Colors.cs
│   ├── Config.cs
│   ├── DeepSeekClient.cs  # DeepSeek API 客户端
│   └── MessageModels.cs
├── Gateway/               # 网关服务器
│   ├── GatewayServer.cs
│   └── JsonRpcMessage.cs
├── Queue/                 # 投递队列
│   ├── DeliveryQueue.cs
│   ├── DeliveryWorker.cs
│   └── DeliveryMessage.cs
├── Routing/               # 路由系统
│   ├── Router.cs
│   └── Binding.cs
├── Scheduler/             # 定时任务
│   ├── CronService.cs
│   └── CronJob.cs
├── Sessions/              # 会话存储
│   └── SessionStore.cs
├── Soul/                  # 人格与记忆
│   ├── SoulStore.cs
│   ├── SoulConfig.cs
│   └── MemoryStore.cs
├── Tools/                 # 工具定义
│   ├── ToolRegistry.cs
│   └── ToolDefinition.cs
├── Program.cs             # 程序入口
├── appsettings.json       # 配置文件
└── README.md              # 本文件
```

## 模型支持

默认使用 `deepseek-chat`，也支持其他 OpenAI 兼容的 API:

```json
{
  "DEEPSEEK_API_KEY": "your-api-key",
  "MODEL_ID": "deepseek-chat",
  "DEEPSEEK_BASE_URL": "https://api.deepseek.com/v1"
}
```

## 依赖项

- .NET 10.0+
- [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite) - SQLite 数据库
- [NCrontab](https://www.nuget.org/packages/NCrontab) - Cron 表达式解析
- Microsoft.Extensions.Configuration - 配置管理

## 许可

MIT - 自由用于学习和教学.

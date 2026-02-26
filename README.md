# Claw0.net - .NET 复刻版

一个基于 .NET 10.0 实现的 AI Agent Gateway 框架，从 0 到 1 构建完整的 AI 代理系统。本项目是 [claw0](https://github.com/shareAI-lab/claw0) 的 .NET 复刻版本，采用渐进式架构设计，每个阶段都引入一个核心机制。

## 🚀 核心特性

- **渐进式学习**: 10 个独立阶段，从最简单的 Agent Loop 到完整的 Delivery Queue
- **模块化架构**: 每个组件都可独立使用和扩展
- **多通道支持**: CLI、文件、WebSocket 等多种交互方式
- **持久化存储**: 会话、记忆、队列的完整持久化支持
- **定时任务**: 支持 at/every/cron 三种定时任务类型
- **可靠投递**: 基于 SQLite 的 At-least-once 消息投递保证

## 🏗️ 架构概览

```
+--------- Claw0.net 架构 ---------+
|                                  |
| s10: Delivery Queue (可靠投递)   |
| s09: Cron Scheduler (定时任务)   |
| s08: Heartbeat (主动行为)        |
| s07: Soul & Memory (人格 + 记忆) |
| s06: Routing (多 Agent 路由)     |
| s05: Gateway (WebSocket/HTTP 网关)|
| s04: Multi-Channel (多通道抽象)  |
| s03: Sessions (会话持久化)       |
| s02: Tools (工具调用)            |
| s01: Agent Loop (基础对话循环)   |
|                                  |
+----------------------------------+
```

## 📦 技术栈

- **运行时**: .NET 10.0
- **数据库**: SQLite (Microsoft.Data.Sqlite)
- **定时任务**: NCrontab
- **控制台**: Spectre.Console
- **配置管理**: Microsoft.Extensions.Configuration
- **依赖注入**: Microsoft.Extensions.DependencyInjection

## 🚀 快速开始

### 1. 环境要求

- .NET 10.0 SDK 或更高版本
- DeepSeek API Key (或其他兼容 OpenAI API 的密钥)

### 2. 克隆项目

```bash
git clone https://github.com/yourusername/claw0.net.git
cd claw0.net
```

### 3. 配置 API 密钥

编辑 `appsettings.json`:

```json
{
  "DEEPSEEK_API_KEY": "sk-your-api-key-here",
  "MODEL_ID": "deepseek-chat",
  "DEEPSEEK_BASE_URL": "https://api.deepseek.com/v1",
  "WORKSPACE_DIR": "./workspace"
}
```

或使用环境变量:

```bash
# Windows
set DEEPSEEK_API_KEY=sk-your-api-key-here

# Linux/macOS
export DEEPSEEK_API_KEY=sk-your-api-key-here
```

### 4. 运行项目

```bash
# 构建项目
dotnet build

# 运行第 1 阶段: 基础 Agent Loop
dotnet run 01

# 运行第 2 阶段: 工具调用
dotnet run 02

# 运行第 10 阶段: 完整系统
dotnet run 10
```

## 📚 各阶段详解

### 阶段 01: Agent Loop
**格言**: "One loop to rule them all"
- 实现基础的 AI 对话循环
- 理解消息历史和上下文管理
- 学习 LLM API 的基本调用方式

### 阶段 02: Tool Use
**格言**: "Give the model hands"
- 实现工具调用机制
- 支持 bash、文件读写等基础工具
- 学习工具 Schema 定义和安全执行

### 阶段 03: Sessions
**格言**: "Conversations that survive restarts"
- 实现会话持久化
- 基于 JSONL 的会话存储
- 支持会话恢复和上下文保持

### 阶段 04: Multi-Channel
**格言**: "Same brain, many mouths"
- 抽象通道接口 (IChannel)
- 实现 CLI 和文件通道
- 学习插件化架构设计

### 阶段 05: Gateway Server
**格言**: "The switchboard"
- 实现 WebSocket 网关服务器
- 支持 JSON-RPC 协议
- 学习服务器架构和并发处理

### 阶段 06: Routing
**格言**: "Every message finds its home"
- 实现多 Agent 路由系统
- 基于绑定规则的消息分发
- 学习路由优先级和匹配算法

### 阶段 07: Soul & Memory
**格言**: "Give it a soul, let it remember"
- 实现人格系统 (SOUL.md)
- 基于向量搜索的记忆存储
- 学习长期记忆和上下文增强

### 阶段 08: Heartbeat
**格言**: "Not just reactive - proactive"
- 实现主动行为机制
- 定时心跳和状态检查
- 学习主动式 AI 代理设计

### 阶段 09: Cron Scheduler
**格言**: "The right thing at the right time"
- 实现定时任务调度器
- 支持 at/every/cron 三种类型
- 学习任务调度和并发控制

### 阶段 10: Delivery Queue
**格言**: "Messages never get lost"
- 实现可靠消息投递队列
- 基于 SQLite 的持久化存储
- 学习 At-least-once 投递保证

## 🗂️ 项目结构

```
claw0.net/
├── Agents/                 # 各阶段实现
│   ├── S01_AgentLoop.cs   # 阶段 1: 基础对话循环
│   ├── S02_ToolUse.cs     # 阶段 2: 工具调用
│   ├── S03_Sessions.cs    # 阶段 3: 会话持久化
│   ├── S04_MultiChannel.cs # 阶段 4: 多通道
│   ├── S05_Gateway.cs     # 阶段 5: 网关服务器
│   ├── S06_Routing.cs     # 阶段 6: 路由系统
│   ├── S07_SoulMemory.cs  # 阶段 7: 人格与记忆
│   ├── S08_Heartbeat.cs   # 阶段 8: 主动行为
│   ├── S09_Cron.cs        # 阶段 9: 定时任务
│   └── S10_Delivery.cs    # 阶段 10: 可靠投递
├── Channels/              # 通道插件
│   ├── IChannel.cs       # 通道接口
│   ├── CliChannel.cs     # CLI 通道
│   ├── FileChannel.cs    # 文件通道
│   └── ChannelRegistry.cs # 通道注册表
├── Common/                # 通用组件
│   ├── Colors.cs         # 控制台颜色
│   ├── Config.cs         # 配置管理
│   ├── DeepSeekClient.cs # DeepSeek API 客户端
│   └── MessageModels.cs  # 消息模型
├── Gateway/               # 网关系统
│   ├── GatewayServer.cs  # 网关服务器
│   └── JsonRpcMessage.cs # JSON-RPC 消息
├── Queue/                 # 消息队列
│   ├── DeliveryQueue.cs  # 投递队列
│   ├── DeliveryWorker.cs # 队列工作器
│   └── DeliveryMessage.cs # 队列消息
├── Routing/               # 路由系统
│   ├── Router.cs         # 路由器
│   └── Binding.cs        # 绑定规则
├── Scheduler/             # 定时任务
│   ├── CronService.cs    # Cron 服务
│   └── CronJob.cs        # Cron 任务
├── Sessions/              # 会话管理
│   └── SessionStore.cs   # 会话存储
├── Soul/                  # 人格与记忆
│   ├── SoulStore.cs      # 人格存储
│   ├── SoulConfig.cs     # 人格配置
│   └── MemoryStore.cs    # 记忆存储
├── Tools/                 # 工具系统
│   ├── ToolRegistry.cs   # 工具注册表
│   └── ToolDefinition.cs # 工具定义
├── Program.cs            # 程序入口
├── appsettings.json      # 配置文件
├── Claw0.csproj          # 项目文件
└── README.md             # 说明文档
```

## 🔧 配置说明

### 基础配置
```json
{
  "DEEPSEEK_API_KEY": "sk-your-api-key",
  "MODEL_ID": "deepseek-chat",
  "DEEPSEEK_BASE_URL": "https://api.deepseek.com/v1",
  "WORKSPACE_DIR": "./workspace"
}
```

### 支持的模型
- `deepseek-chat` (默认)
- `deepseek-reasoner`
- 其他 OpenAI 兼容的模型

### 工作空间
所有持久化数据存储在 `WORKSPACE_DIR` 目录下:
- `./workspace/.sessions/` - 会话数据
- `./workspace/.souls/` - 人格配置
- `./workspace/.queue/` - 消息队列
- `./workspace/.channels/` - 通道数据

## 🧪 开发指南

### 添加新工具
1. 在 `Tools/ToolRegistry.cs` 中注册新工具
2. 实现工具处理逻辑
3. 更新工具 Schema 定义

### 添加新通道
1. 实现 `IChannel` 接口
2. 在 `Channels/ChannelRegistry.cs` 中注册
3. 配置通道初始化逻辑

### 扩展路由规则
1. 修改 `Routing/Binding.cs` 中的绑定规则
2. 更新 `Routing/Router.cs` 中的路由逻辑
3. 测试新的路由匹配规则

## 📊 性能特性

- **轻量级**: 基于 .NET 10.0，启动快速
- **可扩展**: 插件化架构，易于扩展
- **可靠**: 基于 SQLite 的持久化存储
- **高效**: 异步编程模型，支持并发处理

## 🤝 贡献指南

1. Fork 本仓库
2. 创建功能分支 (`git checkout -b feature/amazing-feature`)
3. 提交更改 (`git commit -m 'Add some amazing feature'`)
4. 推送到分支 (`git push origin feature/amazing-feature`)
5. 开启 Pull Request

## 📄 许可证

本项目采用 MIT 许可证 - 查看 [LICENSE](LICENSE) 文件了解详情。

## 🙏 致谢

本项目是 [claw0](https://github.com/shareAI-lab/claw0) 的 .NET 复刻版本。特别感谢原项目作者的开源贡献，为我们提供了优秀的学习和实现参考。

**感谢 [shareAI-lab/claw0](https://github.com/shareAI-lab/claw0) 的开源项目！**

## 📞 联系方式

如有问题或建议，请通过以下方式联系：
- 提交 GitHub Issue

---

**Happy Coding! 🚀**

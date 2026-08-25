# Iridium 安装系统操作文档

面向 Minecraft Java Edition 的安装管线。本文档说明安装系统的生命周期与组件逻辑、如何用 `VanillaInstaller` 跑通一次安装，以及如何用 `InstallTask` DSL 声明式地创建普通 / 特殊 / 并行安装流程。

## 一、生命周期模型

Minecraft 有两种截然不同的状态，由两套 Provider 分别负责：

```text
Game Root
    ├── 已有 Minecraft ──→ MinecraftProvider ──→ MinecraftContext
    └── 新 Minecraft ──→ MinecraftTarget.Create ──→ MinecraftTarget ──→ Installer ──→ ...
```

| 概念 | 含义 |
|---|---|
| `MinecraftContext` | **已经存在、已经被 Provider 解析完成**的实例（`.minecraft/versions/1.21.8`） |
| `MinecraftTarget` | **待安装**的目标（可以是完全空、甚至还不存在的目录 + 目标布局） |

核心原则：

> `MinecraftProvider` 解析已存在的 Minecraft；`MinecraftTarget.Create` 创建待安装的目标。
> `Installer` 依赖 `MinecraftTarget`，而不是 `MinecraftContext`。

## 二、架构总览

```text
Game Root
    │
    ├── 已有 Minecraft
    │       ↓
    │   MinecraftProvider          （识别 + 解析）
    │       ↓
    │   MinecraftContext           （已存在 / 已解析）
    │
    └── 新 Minecraft
            ↓
        MinecraftTarget.Create     （创建安装目标）
            ↓
        MinecraftTarget            （安装前：实例目录 + 布局）
            ↓
        Installer                  （安装策略，构造时绑定 Target）
            ↓
        InstallTask.Define         （声明式 DSL：Do / Then / After / Before / Parallel / Combine）
            ↓
        InstallStep DAG            （具体步骤 + 依赖）
            ↓
        InstallTaskExecutor        （调度 / 并行 / 取消 / 进度聚合）
            ↓
        Minecraft Installed
            ↓
        MinecraftContext           （安装完成后可取得）
```

职责划分：

| 组件 | 职责 |
|---|---|
| `MinecraftProvider` | 识别并解析**已存在**的 Minecraft（Game Root + instanceId → `MinecraftContext`） |
| `MinecraftTarget.Create` | 为**新安装**创建目标（Game Root + instanceId + Layout → `MinecraftTarget`） |
| `MinecraftTarget` | 安装目标：`InstanceId` / `Root`（实例目录，可不存在）/ `Layout`；`Format = Layout.Format` |
| `MinecraftContext` | 描述一个已解析完成的实例（`Format` / `Layout` / `Entry` / `Root`） |
| `Installer` | 一种安装策略，构造时绑定 `MinecraftTarget`，负责创建 `InstallTask` |
| `InstallTask` | 一次完整安装任务（Step 集合 + 依赖关系，即 DAG），本身即声明式 DSL，用 `Define` / `Combine` 构建，可被 `Then` / `After` / `Before` / `Parallel` 就地扩展 |
| `IInstallStep` | 一个逻辑阶段；简单阶段用 delegate，复杂 / 复用阶段才用类 |
| `InstallContext` | 单次执行共享状态（Target、下载源、State 袋；执行基础设施由 Executor 内部管理） |
| `InstallTaskExecutor` | DAG 调度、并行、依赖、取消、失败、进度聚合、资源控制（`Default` 共享无状态单例） |
| `InstallProgress` | 整个安装任务当前状态的完整快照 |

> `InstallTaskBuilder` / `InstallStepHandle` 等中间对象已移除：`InstallTask` 本身就是 Builder，`Define` 传入回调直接编写步骤。

## 三、组件逻辑说明

### 3.1 MinecraftProvider（已有 Minecraft）

```csharp
var provider = new MinecraftProvider(new DirectoryInfo("/path/to/.minecraft"));
var context = await provider.GetAsync("1.21.8");          // 已存在实例 → MinecraftContext
var contexts = await provider.GetMinecraftsAsync();       // 枚举全部实例
```

`MinecraftContext` 表示"已经存在、已经解析完成"，后续的 ArgumentParser / Launcher 只消费它。

### 3.2 MinecraftTarget.Create（新安装目标）

```csharp
// 普通 Standard（默认 StandardLayout）
var target = MinecraftTarget.Create(
    new DirectoryInfo("/path/to/new/.minecraft"),
    "1.21.8");

// 特殊 Layout（例如 Prism）
var prismTarget = MinecraftTarget.Create(
    new DirectoryInfo("/path/to/prism"),
    "example",
    new PrismLayout());
```

`MinecraftTarget.Create(root, instanceId)` 接收的是 **Game Root**（如 `~/.minecraft`）；实例目录完全由 `layout.GetInstanceDirectory(instanceId)` 推导，因此 `target.Root` = `{gameRoot}/versions/1.21.8`（该目录**可以不存在**）。`target.Format` 恒等于 `target.Layout.Format`，不单独维护格式字符串。

### 3.3 Installer

```csharp
var installer = new VanillaInstaller(target);   // 构造时绑定安装目标
```

- `CreateTask(version)`：只把安装工作描述成 DAG，不执行。
- `InstallAsync(version)`：内部自动完成 `CreateTask → InstallTaskExecutor.Default.ExecuteAsync`，返回 `MinecraftInstallResult`（含安装完成后的 `MinecraftContext`）。
- 普通用户不需要接触 Executor / InstallContext。

### 3.4 InstallTask DSL（核心）

`InstallTask.Define` 是流程描述入口。`InstallTask` 本身就是 Builder，`Do` / `Then` / `After` / `Before` / `Parallel` 都返回自身，可链式 / 就地扩展：

```csharp
public InstallTask CreateTask(VersionManifestEntry version) =>
    InstallTask.Define(task => {
        task
            .Do(VanillaSteps.DownloadVersion, "Download Version", DownloadVersionAsync)
            .Then(VanillaSteps.ResolveVersion, "Resolve Version", ResolveVersionAsync)
            .Then(VanillaSteps.DownloadResources, "Download Resources", DownloadResourcesAsync)
            .Then(VanillaSteps.ReconstructAssets, "Reconstruct Assets", ReconstructAssetsAsync);
    });
```

- `Do`：添加一个独立步骤（新 DAG 根）。
- `Then`：追加一个步骤，等待当前 frontier（最近添加的步骤）完成。
- `After(key, ...)`：把步骤**插入**到指定步骤之后（该步骤原来的后继会改为等待新步骤）。
- `Before(key, ...)`：把步骤**插入**到指定步骤之前（新步骤等待其前驱）。
- `Parallel(...)`：从当前 frontier 扇出多个并行步骤；后续 `Then` 等待全部并行分支（汇合点）。
- `Combine`（静态）：把多个 `InstallTask` 合并成一个 DAG。

步骤的**稳定 Key**（类型安全的 `InstallStepKey`，如 `VanillaSteps.ResolveVersion`）与**显示名称**
（如 `Resolve Version`）严格分离：依赖 / 插入用 `InstallStepKey`，`InstallProgress` 同时展示两者。
每种 Installer 自己维护自己的 Key（如 `VanillaSteps`），字符串只出现在 Key 定义处。

特殊格式通过 `CreateTask(version)` 返回的 Task 直接扩展，无需 Contributor：

```csharp
// 追加到末尾
var task = installer.CreateTask(version)
    .Then("Install Forge", InstallForgeAsync);

// 插入到 Resolve Version 之后
var task = installer.CreateTask(version)
    .After(VanillaSteps.ResolveVersion, "Special Processing", SpecialProcessingAsync);
```

简单步骤就是 **delegate**，不需要任何 Step 类：

```csharp
private static async ValueTask DownloadVersionAsync(
    InstallContext context,
    IProgress<InstallStepProgress> progress,
    CancellationToken ct) {
    progress.Report(new InstallStepProgress { Completed = 0, Total = 1 });
    // ...
}
```

### 3.5 InstallStep：什么时候才用类

原则：**能用 delegate 表达的简单 Step 不要创建 Step 类**。只有满足以下任一情况才创建实现 `IInstallStep` 的类：

1. 逻辑非常复杂；
2. 逻辑被多个 Installer 复用；
3. Step 有大量独立状态；
4. Step 有复杂生命周期。

```csharp
public sealed class InstallForgeStep : IInstallStep {
    public string Id => "install-forge";
    public string Name => "Install Forge";

    public async ValueTask ExecuteAsync(InstallContext context, IProgress<InstallStepProgress> progress, CancellationToken ct = default) {
        progress.Report(new InstallStepProgress { Completed = 0, Total = 1 });
        // ...
        progress.Report(new InstallStepProgress { Completed = 1, Total = 1 });
    }
}

// DSL 中直接组合
task.Do(new InstallForgeStep(...));
```

### 3.6 特殊安装步骤 = 普通步骤

特殊格式的独特行为不需要 `IInstallContributor` 之类的扩展点，直接写进自己的 Task：

```csharp
public sealed class SpecialInstaller {
    // ... 构造绑定 MinecraftTarget ...

    public InstallTask CreateTask(VersionManifestEntry version) =>
        InstallTask.Define(task => {
            task
                .Do("Download Version", DownloadVersionAsync)
                .Then("Resolve Version", ResolveVersionAsync)
                .Then("Install Core", InstallCoreAsync)
                .Then("Special Processing", SpecialProcessingAsync)   // ← 格式特有逻辑
                .Then("Finalize", FinalizeAsync);
        });
}
```

`MinecraftFormat`（文件布局）与 `Installer`（安装策略）始终分离：格式由 `MinecraftTarget.Layout` 表达，流程由 Installer 自己的 Task 表达，Provider 绝不反向修改 Installer。

### 3.7 并行流程（Modpack）

```csharp
public InstallTask CreateTask(Modpack modpack) =>
    InstallTask.Define(task => {
        var resolve = task.Do("Resolve Modpack", ResolveModpackAsync);

        var downloads = resolve.Parallel(
            ("Download Assets", DownloadAssetsAsync),
            ("Download Mods", DownloadModsAsync),
            ("Download Libraries", DownloadLibrariesAsync));

        downloads.Then("Finalize", FinalizeAsync);
    });
```

```text
             Resolve
            /   |   \
           ↓    ↓    ↓
       Assets  Mods  Libraries
           \    |    /
            \   |   /
             Finalize
```

无需操作 DAG Node / Edge 等底层结构。

### 3.8 Task 组合（Vanilla + Forge）

组合的是 **InstallTask**，不是 Installer：

```csharp
var vanillaTask = new VanillaInstaller(target).CreateTask(vanillaVersion);
var forgeTask = new ForgeInstaller(target).CreateTask(forgeVersion);

var task = InstallTask.Combine(vanillaTask, forgeTask);
```

无依赖的 Task 分支自动并行；需要顺序（如 Vanilla → Forge）时用 Step 依赖显式表达。

### 3.9 InstallTaskExecutor 与进度

Executor 是**进度聚合的唯一地点**：维护每个 Step 的 `Status`（Pending / Running / Completed / Failed / Cancelled）与 `Completed / Total`，每次变化生成完整快照：

```csharp
public sealed record InstallProgress {
    IReadOnlyList<InstallStepProgress> Steps;   // 每个 Step 的 Status / Completed / Total
    int CompletedSteps;                          // 已完成 Step 数
    int TotalSteps;                              // 总 Step 数
    long CompletedUnits;                         // Σ Step.Completed
    long TotalUnits;                             // Σ Step.Total
    double Progress;                             // CompletedUnits / TotalUnits
}
```

并行 Step 会在同一快照中同时出现（如 `Assets 2000/3000 Running` 与 `Mods 400/500 Running` 并存）。

### 3.10 InstallContext

```csharp
new InstallContext {
    Target = target,                    // 安装目标
    Source = DownloadSource.Official
}
```

Step 之间通过 `context.SetState / GetState<T>` 传递中间结果（如 version-json-path、resolved-context）。
下载步骤通过 `context.CreateResourceDownloader(layout)` 取得绑定到本次执行统一下载并发的
`ResourceDownloader`——每个下载步骤应用同一个 `maxDownloadConcurrency`。

## 四、普通调用链示例（VanillaInstaller）

### 4.1 全新空目录安装 Vanilla

```csharp
using Iridium;
using Iridium.Installation.Installer;
using Iridium.Models.Minecraft;

IridiumConfig.Configure(new IridiumContext());

// 1. 创建安装目标（Game Root + InstanceId，目录甚至尚不存在）
var target = MinecraftTarget.Create(
    new DirectoryInfo("/path/to/new/.minecraft"),
    "1.21.8");

// 2. 创建安装器（绑定目标；并发不属于 Installer，无需传入）
var installer = new VanillaInstaller(target);

// 3. 订阅完整进度快照
installer.ProgressChanged += (_, args) => {
    var p = args.Progress;
    Console.WriteLine($"总进度 {p.Progress:P1}  步骤 {p.CompletedSteps}/{p.TotalSteps}  单位 {p.CompletedUnits}/{p.TotalUnits}");
    foreach (var step in p.Steps)
        Console.WriteLine($"  [{step.Status,-9}] {step.Name,-24} {step.Completed,6}/{step.Total,6}");
};

// 4. 获取要安装的版本
var versions = await VersionManifestSource.GetVersionsAsync();
var version = versions!.First(v => v.Id == "1.21.8");

// 5. 执行安装（下载并发作为本次执行参数，默认 32）
var result = await installer.InstallAsync(version);

Console.WriteLine(result.VersionJsonPath);
Console.WriteLine(result.ClientJarPath);
Console.WriteLine(result.Minecraft?.Format);   // 安装完成后已解析的 MinecraftContext
```

`InstallAsync` 内部等价于：

```csharp
public async Task<MinecraftInstallResult> InstallAsync(
    VersionManifestEntry version,
    Action<InstallTask>? configure = null,
    IProgress<InstallProgress>? progress = null,
    int maxConcurrency = 32,
    CancellationToken ct = default) {
    var task = CreateTask(version);                             // 构建 DAG
    configure?.Invoke(task);                                    // 可选的特殊步骤扩展
    var installContext = new InstallContext {                  // 本次执行的环境
        Target = _target,
        Source = _source
    };
    var result = await InstallTaskExecutor.Default.ExecuteAsync( // 共享无状态 Executor
        task, installContext, maxConcurrency, new Progress<InstallProgress>(ReportProgress), ct);
    ...
}
```

特殊安装（追加 / 插入步骤）不必另外建 Installer：

```csharp
await installer.InstallAsync(version, task => task
    .Then("Install Forge", InstallForgeAsync)
    .After("resolve", "Special Processing", SpecialProcessingAsync));
```

执行基础设施（Step 并发、每步下载并发）由 `InstallTaskExecutor.Default` 在每次执行内部创建，
Installer 不创建 Executor / Downloader，也不保存任何并发状态。

Vanilla DAG：

```text
Download Version
        ↓
  Resolve Version
        ↓
Download Resources
        ↓
Reconstruct Assets
```

### 4.2 已存在 Minecraft 的解析（不安装）

```csharp
var provider = new MinecraftProvider(new DirectoryInfo("/path/to/.minecraft"));
var context = await provider.GetAsync("1.21.8");      // 已解析的 MinecraftContext
var arguments = new ArgumentParser().Build(context, config);
await new Launcher().LaunchAsync(context, config);
```

### 4.3 Vanilla + Forge 组合安装

组合的是 **InstallTask**，不是 Installer：

```csharp
var vanillaTask = new VanillaInstaller(target).CreateTask(vanillaVersion);
var forgeTask = new ForgeInstaller(target).CreateTask(forgeVersion);   // 特殊 Installer

var combined = InstallTask.Combine(vanillaTask, forgeTask);            // 组合 Task
await new VanillaInstaller(target).InstallAsync(combined);
```

无依赖的 Task 分支自动并行；需要顺序（如 Vanilla → Forge）时用步骤依赖显式表达。

## 五、如何新建一个安装器（分几步）

以"新建一个 `ForgeInstaller`"为例，共 **3 步**：

### 第 1 步：创建 Installer 类，构造时绑定 MinecraftTarget

```csharp
public sealed class ForgeInstaller : InstallerBase {
    private readonly MinecraftTarget _target;
    private readonly DownloadSource _source;

    public ForgeInstaller(MinecraftTarget target, DownloadSource? source = null) {
        _target = target;
        _source = source ?? DownloadSource.Official;
    }
}
```

### 第 2 步：实现 `CreateTask`，用 DSL 声明安装流程

可以复用 Vanilla 基础任务再追加 / 插入特殊步骤，也可以完全自定义：

```csharp
// 方式一：在 Vanilla 基础上追加 / 插入
public InstallTask CreateTask(VersionManifestEntry version) =>
    new VanillaInstaller(_target, _source).CreateTask(version)
        .Then("Install Forge", new InstallForgeStep(_target, version));

// 方式二：完全自定义流程
public InstallTask CreateTask(VersionManifestEntry version) =>
    InstallTask.Define(task => {
        task
            .Do("Download Forge Installer", (context, progress, ct) => DownloadAsync(version, context, progress, ct))
            .Then("Install Forge", new InstallForgeStep(_target, version));
    });
```

### 第 3 步：实现 `InstallAsync`，交给 Executor

```csharp
public async Task<MinecraftInstallResult> InstallAsync(
    VersionManifestEntry version,
    Action<InstallTask>? configure = null,
    IProgress<InstallProgress>? progress = null,
    int maxConcurrency = 32,
    CancellationToken ct = default) {
    var task = CreateTask(version);
    configure?.Invoke(task);

    var installContext = new InstallContext { Target = _target, Source = _source };
    var result = await InstallTaskExecutor.Default.ExecuteAsync(
        task, installContext, maxConcurrency, new Progress<InstallProgress>(p => {
            progress?.Report(p);
            ReportProgress(p);
        }), ct);

    return new MinecraftInstallResult { Target = _target, VersionJsonPath = ..., ClientJarPath = ... };
}
```

### 要点

- **普通安装**：`InstallAsync(version)` 一步完成，Executor / Context 内部自动处理。
- **特殊格式**：`CreateTask(version)` 返回的 Task 直接用 `Then` / `After` / `Before` / `Parallel` 扩展，不需要任何 Contributor 抽象。
- **并行**：`Parallel` 扇出；后续 `Then` 自动等待全部并行分支（汇合点）。
- **组合**：`InstallTask.Combine` 合并多个 Task。
- **步骤 ID**：插入 / 依赖用稳定 ID（`resolve`），显示名称可自由改动不影响 Task 结构。
- **不要**把 `MinecraftFormat` / `IMinecraftLayoutFactory` / `MinecraftContext` 塞进 Installer 构造函数——安装器只依赖 `MinecraftTarget`，格式已由 Target 的 Layout 表达。
- **只有**真正需要解析父级 Minecraft（另一个"已存在的 Minecraft"）的安装器才依赖 `IMinecraftProvider`，且依赖放在特殊 Installer / InstallStep 内部。

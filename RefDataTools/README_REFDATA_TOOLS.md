# TryGame RefData Tools

## 导表入口

Unity 菜单：
- `TryGame/RefData/打开导表窗口`
- `TryGame/RefData/导出全部配表并生成入口`
- `TryGame/RefData/生成 Config 读取入口`

## 默认目录

- Excel 配表：`RefDataSource/TryGameRefdataRes/v2`
- 源仓库非运行时导表输出：`RefDataSource/TryGameRefdataRes/v2/Output`（bytes 仅在同步期间暂存）
- 运行时 bytes：`Assets/Resources/TryGameRefdataRes/v2/Output`
- 源仓库中的 `fb_data/*.bytes` 和 `txt_data/Language.bytes` 是临时导出物：导出前会清理旧残留，同步并校验成功后会删除，同时由 Git 忽略；正式 bytes 只提交到运行时仓库。
- cltabtoy 生成表代码：`Assets/TryGameRefdataScripts/GeneratedTables`
- 自动生成 Config：`Assets/TryGameRefdataScripts/GeneratedConfig`
- 工具二进制：`Assets/TryGameToolScripts/RefDataTools/Bin`
- 导表前必须初始化源表和运行时两个子模块；运行时子模块缺失时工具会明确报错并在启动 cltabtoy 前中止。

一次普通导表可能同时产生三个子仓库的差异：源表仓库保存 Excel/非运行时输出，运行时仓库保存正式 bytes，`TryGameRefdataScripts` 保存生成的表代码和 Config。最后再由根仓库记录三个子模块的新提交指针。

## 命名规则

内部表名按 `模块名 + 具体表名` 写，生成器取第一个 PascalCase 单词当模块：

```text
PetBase      -> PetConfig.GetBase(id)
PetAnimation -> PetConfig.GetAnimation(id)
CommonAudio  -> CommonConfig.GetAudio(id)
BattleSkill  -> BattleConfig.GetSkill(id)
```

生成器也会在表支持 `ContainsKey(id)` 时额外生成 `HasXxx(id)`。

## 特殊表

- 语言表不生成普通 `GetXxx(id)`，运行时走 `Lang.Get("#key", args)`。
- `General` 是全局单例参数表，生成 `GeneralConfig.Data`。

## 单人开发推荐工作流

1. 在 `RefDataSource/TryGameRefdataRes/v2` 新增或修改 Excel。
2. 表名按 `PetBase`、`CommonAudio` 这类规则命名。
3. 打开导表窗口，导出选中表。
4. 工具把本次生成的 bytes 增量同步并逐字节校验到 Resources，再生成表代码和 `XxxConfig` 入口；同步失败会输出错误并中止成功流程。
5. 业务代码只读 `PetConfig.GetBase(id)` / `GeneralConfig.Data.xxx` / `Lang.Get(key)`。

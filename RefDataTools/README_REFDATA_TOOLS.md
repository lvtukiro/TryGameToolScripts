# TryGame RefData Tools

## 导表入口

Unity 菜单：

- `TryGame/RefData/打开导表窗口`
- `TryGame/RefData/导出全部配表并生成入口`
- `TryGame/RefData/生成 Config 读取入口`

## 默认目录

- Excel 配表：`Assets/TryGameRefdataRes/v2`
- bytes 输出：`Assets/TryGameRefdataRes/v2/Output/fb_data`
- cltabtoy 生成表代码：`Assets/TryGameRefdataScripts/GeneratedTables`
- 自动生成 Config：`Assets/TryGameRefdataScripts/GeneratedConfig`
- 工具二进制：`Assets/TryGameToolScripts/RefDataTools/Bin`

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

1. 在 `Assets/TryGameRefdataRes/v2` 新增或修改 Excel。
2. 表名按 `PetBase`、`CommonAudio` 这类规则命名。
3. 打开导表窗口，导出选中表。
4. 工具自动刷新 bytes、生成表代码、生成 `XxxConfig` 入口。
5. 业务代码只读 `PetConfig.GetBase(id)` / `GeneralConfig.Data.xxx` / `Lang.Get(key)`。

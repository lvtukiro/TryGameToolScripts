# TryGame RefData Tools

## 导表入口

Unity 菜单：

- `TryGame/RefData/打开导表窗口`
- `TryGame/RefData/导出全部配表并生成入口`

导出模式：

- 导表窗口中的“单项导出”和“导出选中项”是增量导出，会保留未选中 Excel 的既有产物；它不会判断某张表是否已删除或改名。所有增量与全量导出都会在 staging 内基于完整 GeneratedTables 重新生成 Config，不提供跳过或直接写正式 Config 的入口。
- 菜单“导出全部配表并生成入口”固定读取正式源表目录，并执行全量清洁重建。删除或改名表后必须使用该入口。

## 三仓库目录

- Excel 配表：`RefDataSource/TryGameRefdataRes/v2`
- 源仓库非运行时输出：`RefDataSource/TryGameRefdataRes/v2/Output`
- 运行时 bytes：`Assets/Resources/TryGameRefdataRes/v2/Output`
- cltabtoy 生成表代码：`Assets/TryGameRefdataScripts/GeneratedTables`
- 自动生成 Config：`Assets/TryGameRefdataScripts/GeneratedConfig`
- 工具二进制：`Assets/TryGameToolScripts/RefDataTools/Bin`

源仓库中的 `fb_data/*.bytes` 和 `txt_data/Language.bytes` 只是在 staging 内同步时使用的临时产物，发布到源仓库前会删除。正式 bytes 只保存在运行时仓库。

导表前必须初始化以下三个 Git 仓库，否则事务会在启动 cltabtoy 前报错并中止：

- `RefDataSource/TryGameRefdataRes`
- `Assets/Resources/TryGameRefdataRes`
- `Assets/TryGameRefdataScripts`

## 导表事务

每次导表都在 `Temp/TryGameRefDataTransactions/<事务 ID>` 下执行，正式目录不会被 cltabtoy、Language 导出器或 Config 生成器直接修改。

流程如下：

1. 增量导出会把源仓库 Output、运行时 Output、GeneratedTables、GeneratedConfig 复制到 `staging`；全量清洁重建则从四个空 staging 目录开始生成。
2. 建立本次有效输入集合。普通业务表和语言表属于显式输入；只要本次包含 cltabtoy 普通表，同目录的 `共用枚举结构体.xlsx` 就作为隐式依赖参与哈希与 manifest，但不会作为独立业务表传给 cltabtoy。
3. cltabtoy 和 Language 输出完成后，固定基于 staging 中的完整 GeneratedTables 重新生成 Config；所有产物都只写 staging。
4. 把本次生成的 bytes 同步到 staging 运行时目录，并从 staging 源目录删除临时 bytes。
5. 把生成的 JSON/FBS/Java/TXT、文本型 `Language.bytes` 和生成 C# 统一为 LF，并保留各文件既有 UTF-8 BOM 约定；若与旧正式文件仅有行末空白差异则沿用旧格式，再在 staging 内完成全部验证。
6. 验证通过后生成 manifest v2。除输入、表、行数和工具版本外，`payloadFiles` 会记录四个待发布目录全部有效文件的逻辑根、相对路径和 SHA256；`.meta` 与 manifest 自身不参与递归哈希。
7. 同一份 `RefDataManifest.json` 分别写入源仓库 Output、运行时 Output 和 GeneratedTables。发布前要求三份都逐字节等于本次内存中的 manifest，并按 `payloadFiles` 核对 staging 的文件集合与逐文件哈希。全量清洁重建只给仍存在的同路径文件和目录恢复旧 `.meta`，并打印旧产物删除计划。
8. 再次确认全部显式输入和隐式依赖的 SHA256 未变化；随后先把每个旧正式目录移动到 `backup`，再把对应 staging 目录移动为正式目录。
9. 四个目录移动完成后、清理 backup 前，重新要求三份正式 manifest 等于本次 manifest，并按 manifest 核对四个正式目录的完整文件集合与逐文件 SHA256。任一文件缺失、多出或哈希不符都会打印具体逻辑根、路径、期望值和实际值，并自动回滚。
10. 任一目录发布或发布后校验失败时，从当前失败项开始反向回滚；已发布的新目录保留到 `failed-new`，失败日志会打印 target、backup、failed-new 和异常。
11. 发布及正式门禁全部成功后再刷新 Unity，并分别用 `git diff`、`git diff --cached` 和 untracked 查询打印三个仓库的真实内容差异，避免 `git status` 把换行/stat 缓存噪声误报为修改。

成功事务会尝试自动删除事务目录。若发布前的导出或验证失败，staging 会保留并在错误日志中打印完整路径，便于检查。若发布成功后 Unity 刷新、Git 差异读取或 backup 清理失败，只记录明确错误，不会把已经完成的发布误报成“正式目录未改变”。

### 发布前验证

当前验证包括：

- 客户端 JSON 与运行时 bytes 的表集合一致。
- JSON 能解析，数据行数有效，同一表中的 ID 不重复。
- 每张表都有 FBS、bytes 和生成 C#。
- 每份 bytes 都能用对应 FBS 经 `flatc` 反序列化；同次生成的客户端 JSON 会再按该 FBS 编码为 expected bytes，并与运行时 bytes 做长度、SHA256 和逐字节一致性校验，避免旧 bytes 或错内容通过发布。
- `Language.bytes` 包含 `id`、`zh_cn`、`en_US`，必要字段非空且 key 唯一。
- staging 中的 GeneratedTables 和 GeneratedConfig 能通过临时 `dotnet build`。临时工程从 Unity 生成的 `TryGame.RefData.Runtime.csproj` 派生，并把 GeneratedTables、GeneratedConfig 路径替换为 staging 路径；如果 Unity 尚未生成该工程，验证会明确失败，不回退使用 `Assembly-CSharp.csproj`。
- 本次显式 Excel 输入与 `共用枚举结构体.xlsx` 隐式依赖在导出前后及正式发布前的 SHA256 一致，避免导出过程中改表却发布旧输出。
- manifest v2 能完整序列化，并记录输入角色、项目相对路径、表名、行数、工具版本，以及四个正式目录完整有效产物的相对路径和 SHA256。
- staging 与正式目录都必须通过 manifest 精确副本及完整 payload 文件集合/哈希门禁；发布后失败会在清理 backup 前自动回滚。

cltabtoy 0.3.0.0 完成后会在控制台显示“按任意键退出”。必须在该控制台按任意键让进程正常退出；直接关闭窗口会被视为导出失败，正式目录不会发布。

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
- `共用枚举结构体.xlsx` 只能由 cltabtoy 作为同目录隐式依赖读取，不能在窗口或内部接口中作为普通业务表显式导出。

## 单人开发推荐工作流

1. 在 `RefDataSource/TryGameRefdataRes/v2` 新增或修改 Excel。
2. 表名按 `PetBase`、`CommonAudio` 这类规则命名。
3. 普通数值修改可在导表窗口导出单项或选中项；删除/改名表时使用菜单“导出全部配表并生成入口”执行清洁重建。
4. 等待 staging 导出；cltabtoy 提示后在控制台按任意键退出。
5. 只有全部验证通过后，工具才会同时发布源输出、正式 bytes、生成表代码和 Config。
6. 查看 Unity 日志末尾打印的三个仓库差异，再分别检查 Git diff。
7. 业务代码只读 `PetConfig.GetBase(id)`、`GeneralConfig.Data.xxx`、`Lang.Get(key)`。

# 数据库（SQLite + Dapper）

`Features/Database/DatabaseService.cs`：`StarwardDatabase.db`；`KVT`、`Setting` + 各游戏业务表。

- 迁移：`DatabaseSqls` 中每个 `Sql_vN` 一次迁移，按 `PRAGMA USER_VERSION` 跳过已执行。
- **改 schema 只追加新 `Sql_vN` 并更新 `USER_VERSION`**，禁止改已发布迁移。
- 自定义类型：`DapperSqlMapper`（如 `GameBiz` 存字符串）。
- 备份与还原：`DatabaseBackupService` 按 `BackupDataPolicy` 决定每张表、每个设置键是否进备份、还原时怎么取舍。**新增保存凭证（Cookie、token、设备指纹）、本机路径或本机缓存的表 / 设置键时，要在 `BackupDataPolicy` 登记**，否则会被当成普通数据备份出去、还原时被覆盖；同时补 `tests/Starward.Tests/BackupDataPolicyTests.cs`。

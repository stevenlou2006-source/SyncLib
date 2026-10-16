## CHANGELOG.md

```markdown
# 更新日志

## [1.1.0] - 2026-05-24
### 新增
- 快照补发系统：`EnableSnapshotSync` / `DisableSnapshotSync` / `RequestSnapshot`
  - 新玩家加入时自动生成状态快照并发送
  - 客机可主动请求补发
- 动态配置同步：`RegisterSyncedConfig` / `SetSyncedConfig` / `GetSyncedConfig`
  - 房主修改配置后自动通过 RaiseEvent 推送给所有客机
  - 支持配置快照序列化（`SerializeConfigSnapshot` / `ApplyConfigSnapshot`），配合快照补发使用
- API 稳定性承诺：所有 public API 自本版本起锁定，未来只增不改

### 修复
- 编译期错误：添加 `using BepInEx;`，解决 `Debug` 和 `Hashtable` 类型歧义
- `SendResponse` 参数类型修正（`int` → `Player`）

## [1.0.0] - 2026-05-24
### 新增
- 初始发布
- RPC 封装：`Broadcast` / `BroadcastBuffered` / `SendToPlayer` / `RequestHost`
- RaiseEvent 封装：`RaiseEventBroadcast` / `RegisterEventHandler`
- 请求-响应模式：`SendRequest` / `SendResponse` / `HandleResponse`
- 房间属性同步：`SetRoomProperty` / `GetRoomProperty`
- 新玩家加入回调：`RegisterOnPlayerJoinedCallback`
- 自动挂载 PunManager 并跨场景持久化
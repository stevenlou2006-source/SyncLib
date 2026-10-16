# SyncLib - 联机同步库

为 REPO 模组开发提供开箱即用的网络同步能力。封装 Photon RPC、RaiseEvent、房间属性同步、快照补发与动态配置同步。

版本：1.1.0

> **⚠️ 免责声明 / Disclaimer**
> 
> 本库为个人项目辅助工具，按“现状 (AS IS)”提供，不提供任何形式的明示或暗示担保，包括但不限于对适销性、特定用途适用性或不侵权的担保。作者不保证代码无缺陷、无中断或无错误。因使用本库产生的任何直接、间接或附带损失，作者不承担责任。功能可能根据作者自身需求随时变更。
>
> This library is a personal development utility provided "AS IS" without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose, and noninfringement. The author makes no guarantee that the code is defect-free, uninterrupted, or error-free. The author shall not be liable for any direct, indirect, or consequential damages arising from the use of this library. Features may change at any time based on the author's needs.

## ✨ 功能

| 分类 | 功能 | 说明 |
|------|------|------|
| RPC | 广播 / 缓冲广播 / 点对点 / 请求房主 | 封装四种常用 RPC 模式，自动检查权限 |
| RaiseEvent | 事件广播 / 请求-响应 | 无需 PhotonView 的轻量通信 |
| 房间属性 | 设置 / 读取 | 键值对存储，适合全局配置 |
| 快照补发 | 自动捕获 / 主动请求 | 新玩家加入时自动补发当前状态 |
| 动态配置 | 注册 / 房主同步 / 快照序列化 | 配置变更自动推送给所有客机 |

## 安装

- 依赖 BepInExPack
- 将 `SyncLib.dll` 放入 `BepInEx/plugins/SyncLib/`
- 在你的模组项目中引用 `SyncLib.dll`
- 在模组的 `manifest.json` 中添加依赖：`"cn_xc-SyncLib-1.1.0"`

## ⚙️ 快速开始

```csharp
using cn_xc.SyncLib;

// 1. 初始化（自动挂载到 PunManager，跨场景不销毁）
var relay = SyncRelay.Instance;

// 2. RPC 广播
SyncRelay.RegisterRpc("OnSomethingChanged");
relay.Broadcast("OnSomethingChanged", arg1, arg2);

// 3. 房间属性
SyncRelay.SetRoomProperty("MyModSetting", 5);
int val = SyncRelay.GetRoomProperty<int>("MyModSetting", 1);

// 4. 动态配置（房主改，自动同步）
SyncRelay.RegisterSyncedConfig("TokenMultiplier", "10", (key, val) => {
    MyMod.UpdateMultiplier(int.Parse(val));
});
SyncRelay.SetSyncedConfig("TokenMultiplier", "20");

// 5. 快照补发（新玩家加入自动补状态）
SyncRelay.EnableSnapshotSync(
    snapshotProvider: () => {
        return JsonUtility.ToJson(myState);
    },
    snapshotApplier: (snapshot) => {
        myState = JsonUtility.FromJson<MyState>(snapshot);
    }
);

// 6. 新玩家加入回调
SyncRelay.RegisterOnPlayerJoinedCallback(player => {
    // 自定义补发逻辑
});

---

## 许可证

本项目以 **GNU General Public License v3.0** 发布。

Copyright (C) 2026 cn_xc

完整条款见仓库根目录 [LICENSE](LICENSE)。

using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace cn_xc.SyncLib
{
	[BepInPlugin("cn_xc.SyncLib", "SyncLib", "1.1.0")]
	public class SyncRelay : MonoBehaviourPunCallbacks, IOnEventCallback
	{
		private static SyncRelay _instance;
		public static SyncRelay Instance
		{
			get
			{
				if (_instance == null)
				{
					var pm = FindObjectOfType<PunManager>();
					if (pm != null)
					{
						_instance = pm.GetComponent<SyncRelay>();
						if (_instance == null)
							_instance = pm.gameObject.AddComponent<SyncRelay>();
						DontDestroyOnLoad(_instance.gameObject);
					}
				}
				return _instance;
			}
		}

		private static readonly HashSet<string> RegisteredRpcs = new HashSet<string>();
		private static readonly List<Action<Player>> OnPlayerJoinedCallbacks = new List<Action<Player>>();
		private static readonly Dictionary<byte, Action<EventData>> EventHandlers = new Dictionary<byte, Action<EventData>>();
		private static readonly Dictionary<int, Action<object[]>> PendingRequests = new Dictionary<int, Action<object[]>>();
		private static int requestIdCounter;

		private static readonly Dictionary<string, Action<string, string>> ConfigChangeHandlers = new Dictionary<string, Action<string, string>>();
		private static readonly Dictionary<string, string> RegisteredConfigs = new Dictionary<string, string>();
		private const byte CONFIG_SYNC_EVENT = 200;
		private const byte SNAPSHOT_REQUEST_EVENT = 201;
		private const byte SNAPSHOT_RESPONSE_EVENT = 202;

		private static Func<string> _snapshotProvider;
		private static Action<string> _snapshotApplier;
		private static readonly Queue<Player> PendingSnapshotPlayers = new Queue<Player>();
		private static bool _snapshotEnabled;

		private void Awake()
		{
			if (_instance != null && _instance != this) { Destroy(this); return; }
			_instance = this;
			if (GetComponent<PhotonView>() == null) gameObject.AddComponent<PhotonView>();
			DontDestroyOnLoad(gameObject);
			PhotonNetwork.AddCallbackTarget(this);
			UnityEngine.Debug.Log("[SyncLib] 初始化完成");
		}

		private void OnDestroy()
		{
			PhotonNetwork.RemoveCallbackTarget(this);
		}

		// ==================== RPC ====================
		public static void RegisterRpc(string name)
		{
			if (RegisteredRpcs.Add(name))
				UnityEngine.Debug.Log($"[SyncLib] RPC 已注册: {name}");
		}

		public void Broadcast(string method, params object[] args)
		{
			if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
			photonView.RPC(method, RpcTarget.All, args);
		}

		public void BroadcastBuffered(string method, params object[] args)
		{
			if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
			photonView.RPC(method, RpcTarget.AllBuffered, args);
		}

		public void SendToPlayer(Player target, string method, params object[] args)
		{
			if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom || target == null) return;
			photonView.RPC(method, target, args);
		}

		public void RequestHost(string method, params object[] args)
		{
			if (PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
			photonView.RPC(method, RpcTarget.MasterClient, args);
		}

		// ==================== RaiseEvent ====================
		public static void RegisterEventHandler(byte eventCode, Action<EventData> handler)
		{
			EventHandlers[eventCode] = handler;
			UnityEngine.Debug.Log($"[SyncLib] 事件已注册: {eventCode}");
		}

		public void RaiseEventBroadcast(byte eventCode, object content, bool reliable = true)
		{
			if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
			var options = new RaiseEventOptions { Receivers = ReceiverGroup.All };
			var sendOptions = new SendOptions { Reliability = reliable };
			PhotonNetwork.RaiseEvent(eventCode, content, options, sendOptions);
		}

		void IOnEventCallback.OnEvent(EventData photonEvent)
		{
			if (EventHandlers.TryGetValue(photonEvent.Code, out var handler))
				handler?.Invoke(photonEvent);
		}

		// ==================== 请求-响应 ====================
		public void SendRequest(byte eventCode, object content, Action<object[]> callback, bool reliable = true)
		{
			int reqId = ++requestIdCounter;
			PendingRequests[reqId] = callback;

			var options = new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient };
			var sendOptions = new SendOptions { Reliability = reliable };
			var payload = new object[] { reqId, content };
			PhotonNetwork.RaiseEvent(eventCode, payload, options, sendOptions);
		}

		public void SendResponse(byte eventCode, int reqId, object content, Player target, bool reliable = true)
		{
			var options = new RaiseEventOptions { TargetActors = new[] { target.ActorNumber } };
			var sendOptions = new SendOptions { Reliability = reliable };
			var payload = new object[] { reqId, content };
			PhotonNetwork.RaiseEvent(eventCode, payload, options, sendOptions);
		}

		public void HandleResponse(int reqId, object[] data)
		{
			if (PendingRequests.TryGetValue(reqId, out var callback))
			{
				PendingRequests.Remove(reqId);
				callback?.Invoke(data);
			}
		}

		// ==================== 房间属性 ====================
		public static void SetRoomProperty(string key, object value)
		{
			if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
			var props = new ExitGames.Client.Photon.Hashtable { [key] = value };
			PhotonNetwork.CurrentRoom.SetCustomProperties(props);
		}

		public static T GetRoomProperty<T>(string key, T fallback = default)
		{
			if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return fallback;
			var props = PhotonNetwork.CurrentRoom.CustomProperties;
			return props.ContainsKey(key) && props[key] is T v ? v : fallback;
		}

		// ==================== 新玩家加入 ====================
		public static void RegisterOnPlayerJoinedCallback(Action<Player> callback)
		{
			if (!OnPlayerJoinedCallbacks.Contains(callback))
				OnPlayerJoinedCallbacks.Add(callback);
		}

		public override void OnPlayerEnteredRoom(Player newPlayer)
		{
			foreach (var cb in OnPlayerJoinedCallbacks)
			{
				try { cb?.Invoke(newPlayer); }
				catch (Exception e) { UnityEngine.Debug.LogError($"[SyncLib] 回调失败: {e.Message}"); }
			}

			if (_snapshotEnabled && PhotonNetwork.IsMasterClient)
			{
				PendingSnapshotPlayers.Enqueue(newPlayer);
				UnityEngine.Debug.Log($"[SyncLib] 新玩家 {newPlayer.NickName} 加入快照队列");
			}
		}

		// ==================== 快照补发 ====================
		public static void EnableSnapshotSync(Func<string> snapshotProvider, Action<string> snapshotApplier)
		{
			_snapshotProvider = snapshotProvider;
			_snapshotApplier = snapshotApplier;
			_snapshotEnabled = true;

			RegisterEventHandler(SNAPSHOT_RESPONSE_EVENT, OnSnapshotResponseReceived);
			UnityEngine.Debug.Log("[SyncLib] 快照补发已启用");
		}

		public static void DisableSnapshotSync()
		{
			_snapshotEnabled = false;
			_snapshotProvider = null;
			_snapshotApplier = null;
			PendingSnapshotPlayers.Clear();
			UnityEngine.Debug.Log("[SyncLib] 快照补发已禁用");
		}

		public void RequestSnapshot()
		{
			if (PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
			var options = new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient };
			PhotonNetwork.RaiseEvent(SNAPSHOT_REQUEST_EVENT, null, options, SendOptions.SendReliable);
			UnityEngine.Debug.Log("[SyncLib] 已请求快照补发");
		}

		private void ProcessSnapshotRequest(Player requester)
		{
			if (_snapshotProvider == null) return;
			string snapshot = _snapshotProvider();
			if (string.IsNullOrEmpty(snapshot)) return;

			SendResponse(SNAPSHOT_RESPONSE_EVENT, 0, snapshot, requester, true);
			UnityEngine.Debug.Log($"[SyncLib] 快照已发送给 {requester.NickName}");
		}

		private static void OnSnapshotResponseReceived(EventData eventData)
		{
			if (eventData.CustomData is string snapshot)
			{
				_snapshotApplier?.Invoke(snapshot);
				UnityEngine.Debug.Log("[SyncLib] 快照已应用");
			}
		}

		private void Update()
		{
			if (_snapshotEnabled && PhotonNetwork.IsMasterClient && PendingSnapshotPlayers.Count > 0)
			{
				Player player = PendingSnapshotPlayers.Dequeue();
				ProcessSnapshotRequest(player);
			}
		}

		// ==================== 动态配置同步 ====================
		public static void RegisterSyncedConfig(string configKey, string defaultValue, Action<string, string> onConfigChanged)
		{
			if (!RegisteredConfigs.ContainsKey(configKey))
			{
				RegisteredConfigs[configKey] = defaultValue;
				ConfigChangeHandlers[configKey] = onConfigChanged;
				UnityEngine.Debug.Log($"[SyncLib] 配置已注册: {configKey} = {defaultValue}");
			}
		}

		public static void SetSyncedConfig(string configKey, string newValue)
		{
			if (!PhotonNetwork.IsMasterClient)
			{
				UnityEngine.Debug.LogWarning($"[SyncLib] 非房主尝试设置同步配置: {configKey}");
				return;
			}
			if (!RegisteredConfigs.ContainsKey(configKey))
			{
				UnityEngine.Debug.LogWarning($"[SyncLib] 未注册的配置键: {configKey}");
				return;
			}

			RegisteredConfigs[configKey] = newValue;
			UnityEngine.Debug.Log($"[SyncLib] 房主设置配置: {configKey} = {newValue}");

			if (ConfigChangeHandlers.TryGetValue(configKey, out var handler))
				handler?.Invoke(configKey, newValue);

			var payload = new object[] { configKey, newValue };
			var options = new RaiseEventOptions { Receivers = ReceiverGroup.Others };
			PhotonNetwork.RaiseEvent(CONFIG_SYNC_EVENT, payload, options, SendOptions.SendReliable);
		}

		public static string GetSyncedConfig(string configKey)
		{
			return RegisteredConfigs.TryGetValue(configKey, out var value) ? value : null;
		}

		private static void OnConfigSyncReceived(EventData eventData)
		{
			if (eventData.CustomData is object[] payload && payload.Length >= 2)
			{
				string key = payload[0] as string;
				string value = payload[1] as string;
				if (key != null && RegisteredConfigs.ContainsKey(key))
				{
					RegisteredConfigs[key] = value;
					if (ConfigChangeHandlers.TryGetValue(key, out var handler))
						handler?.Invoke(key, value);
					UnityEngine.Debug.Log($"[SyncLib] 配置已同步: {key} = {value}");
				}
			}
		}

		public static string SerializeConfigSnapshot()
		{
			var sb = new StringBuilder();
			foreach (var kvp in RegisteredConfigs)
			{
				sb.AppendLine($"{kvp.Key}={kvp.Value}");
			}
			return sb.ToString();
		}

		public static void ApplyConfigSnapshot(string snapshot)
		{
			if (string.IsNullOrEmpty(snapshot)) return;
			var lines = snapshot.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var line in lines)
			{
				var parts = line.Split('=');
				if (parts.Length == 2 && RegisteredConfigs.ContainsKey(parts[0]))
				{
					RegisteredConfigs[parts[0]] = parts[1];
					if (ConfigChangeHandlers.TryGetValue(parts[0], out var handler))
						handler?.Invoke(parts[0], parts[1]);
				}
			}
			UnityEngine.Debug.Log($"[SyncLib] 配置快照已应用: {lines.Length} 项");
		}
	}
}
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Pineglow.Launcher.Services;

/// <summary>
/// Phase 3B v3 修复批次：AI 诊断操作注册表。
///
/// 键：(<see cref="InstanceId"/>, <see cref="OperationId"/>) 复合键，避免跨实例串扰。
/// 支持 cancel-before-register 的有界短 TTL tombstone；
/// Register/Cancel/Get/Count 等入口都会清理过期项（opportunistic sweep）；
/// 容量 = active + tombstone。
/// </summary>
public sealed class AiOperationRegistry
{
	public const int DefaultCapacity = 1000;
	public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);
	public static readonly TimeSpan TombstoneTtl = TimeSpan.FromSeconds(30);

	/// <summary>不可混淆的 (instanceId, operationId) 值类型键。</summary>
	public readonly struct Key : IEquatable<Key>
	{
		public string InstanceId { get; }
		public string OperationId { get; }
		public Key(string instanceId, string operationId)
		{
			InstanceId = instanceId;
			OperationId = operationId;
		}
		public bool Equals(Key other) => string.Equals(InstanceId, other.InstanceId, StringComparison.Ordinal) && string.Equals(OperationId, other.OperationId, StringComparison.Ordinal);
		public override bool Equals(object? obj) => obj is Key k && Equals(k);
		public override int GetHashCode() => HashCode.Combine(InstanceId, OperationId);
	}

	private enum Kind { Active, Tombstone }

	private readonly struct Entry
	{
		public Kind Kind { get; }
		public CancellationTokenSource? Cts { get; }
		public DateTime ExpiresAtUtc { get; }
		public Entry(Kind kind, CancellationTokenSource? cts, DateTime expiresAtUtc)
		{
			Kind = kind;
			Cts = cts;
			ExpiresAtUtc = expiresAtUtc;
		}
	}

	private readonly ConcurrentDictionary<Key, Entry> _entries = new ConcurrentDictionary<Key, Entry>();
	private readonly int _capacity;
	private readonly TimeSpan _activeTtl;
	private readonly TimeSpan _tombstoneTtl;

	public AiOperationRegistry(int capacity = DefaultCapacity, TimeSpan? activeTtl = null, TimeSpan? tombstoneTtl = null)
	{
		_capacity = capacity;
		_activeTtl = activeTtl ?? DefaultTtl;
		_tombstoneTtl = tombstoneTtl ?? TombstoneTtl;
	}

	/// <summary>注册一个 active operation。命中已存在的 tombstone 时立即 cancel 并清除。</summary>
	public bool Register(string instanceId, string operationId, CancellationTokenSource cts)
	{
		if (string.IsNullOrEmpty(instanceId)) throw new ArgumentException("instanceId required", nameof(instanceId));
		if (string.IsNullOrEmpty(operationId)) throw new ArgumentException("operationId required", nameof(operationId));
		if (cts == null) throw new ArgumentNullException(nameof(cts));
		SweepExpired();
		var key = new Key(instanceId, operationId);
		if (_entries.TryGetValue(key, out var existing) && existing.Kind == Kind.Tombstone)
		{
			_entries.TryUpdate(key,
				new Entry(Kind.Active, cts, DateTime.UtcNow.Add(_activeTtl)),
				existing);
			try { cts.Cancel(); } catch { }
			try { cts.Token.Register(() => Remove(key)); } catch { }
			return true;
		}
		if (_entries.Count >= _capacity) return false;
		DateTime expiresAt = DateTime.UtcNow.Add(_activeTtl);
		bool added = _entries.TryAdd(key, new Entry(Kind.Active, cts, expiresAt));
		if (added)
		{
			cts.Token.Register(() => Remove(key));
		}
		return added;
	}

	/// <summary>取消一个 operation。cancel-before-register 创建 tombstone。</summary>
	public bool Cancel(string instanceId, string operationId)
	{
		if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(operationId)) return false;
		SweepExpired();
		var key = new Key(instanceId, operationId);
		if (_entries.TryGetValue(key, out var existing))
		{
			if (existing.Kind == Kind.Active && existing.Cts != null)
			{
				_entries.TryUpdate(key,
					new Entry(Kind.Tombstone, null, DateTime.UtcNow.Add(_tombstoneTtl)),
					existing);
				try { existing.Cts.Cancel(); } catch { }
				return true;
			}
			if (existing.Kind == Kind.Tombstone)
			{
				return false;
			}
		}
		if (_entries.Count >= _capacity) return false;
		DateTime expiresAt = DateTime.UtcNow.Add(_tombstoneTtl);
		Entry tombstone = new Entry(Kind.Tombstone, null, expiresAt);
		bool added = _entries.TryAdd(key, tombstone);
		return added;
	}

	/// <summary>取出 operationId 对应的 CTS（不取消）。返回 null 表示不存在或已过期。</summary>
	public CancellationTokenSource? Get(string instanceId, string operationId)
	{
		if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(operationId)) return null;
		var key = new Key(instanceId, operationId);
		if (_entries.TryGetValue(key, out var entry))
		{
			if (entry.ExpiresAtUtc <= DateTime.UtcNow)
			{
				_entries.TryRemove(key, out _);
				if (entry.Kind == Kind.Active)
				{
					try { entry.Cts?.Dispose(); } catch { }
				}
				return null;
			}
			return entry.Cts;
		}
		return null;
	}

	/// <summary>当前注册数量（含未过期 tombstone）。触发清理后回写。</summary>
	public int Count
	{
		get
		{
			SweepExpired();
			return _entries.Count;
		}
	}

	/// <summary>清空所有注册项并 dispose 所有 CTS。</summary>
	public void Clear()
	{
		foreach (var kvp in _entries)
		{
			try { kvp.Value.Cts?.Dispose(); } catch { }
		}
		_entries.Clear();
	}

	/// <summary>token 触发时回调。Remove 不会清 tombstone。</summary>
	private void Remove(Key key)
	{
		if (_entries.TryGetValue(key, out var entry) && entry.Kind == Kind.Active)
		{
			if (_entries.TryRemove(key, out var removed))
			{
				try { removed.Cts?.Dispose(); } catch { }
			}
		}
	}

	/// <summary>opportunistic 清理过期项；active dispose CTS，tombstone 直接移除。</summary>
	private void SweepExpired()
	{
		DateTime now = DateTime.UtcNow;
		foreach (var kvp in _entries.ToArray())
		{
			if (kvp.Value.ExpiresAtUtc <= now)
			{
				if (_entries.TryRemove(kvp.Key, out var removed))
				{
					try { removed.Cts?.Dispose(); } catch { }
				}
			}
		}
	}
}
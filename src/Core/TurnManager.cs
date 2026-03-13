using Godot;
using System;
using System.Collections.Generic;

namespace VoxelSiege.Core;

public partial class TurnManager : Node
{
    private readonly List<PlayerSlot> _turnOrder = new List<PlayerSlot>();
    private int _turnIndex;
    private float _remainingTurnTime;
    private bool _isRunning;
    private PlayerSlot? _forcedPlayer;

    public IReadOnlyList<PlayerSlot> TurnOrder => _turnOrder;
    public int RoundNumber { get; private set; } = 1;
    public PlayerSlot? CurrentPlayer => _turnOrder.Count == 0 ? null : _turnOrder[_turnIndex];
    public float RemainingTurnTime => _remainingTurnTime;
    public bool IsRunning => _isRunning;

    /// <summary>
    /// When false, the turn timer in _Process() will NOT auto-advance turns.
    /// Set to false on clients in multiplayer so only the host drives turn timing.
    /// </summary>
    public bool IsAuthoritative { get; set; } = true;

    /// <summary>
    /// Fired when the turn timer expires. GameManager should handle this
    /// via AdvanceTurnAuthoritative() to ensure proper network sync.
    /// </summary>
    public event Action? TurnTimedOut;

    public void Configure(IEnumerable<PlayerSlot> alivePlayers, float turnTimeSeconds, RandomNumberGenerator? rng = null)
    {
        _turnOrder.Clear();
        _turnOrder.AddRange(alivePlayers);

        // Use System.Random with a high-entropy seed to guarantee a truly
        // random starting player.  The previous Godot RandomNumberGenerator +
        // Randomize() path could produce the same seed when called in quick
        // succession (e.g. rapid restarts) because Randomize() only uses the
        // current OS tick.  Guid.NewGuid().GetHashCode() pulls from the OS
        // CSPRNG and avoids that pitfall.
        Random sysRng = new Random(Guid.NewGuid().GetHashCode());

        // Fisher-Yates shuffle so every player has an equal chance of going first
        for (int index = _turnOrder.Count - 1; index > 0; index--)
        {
            int swapIndex = sysRng.Next(0, index + 1);
            PlayerSlot temp = _turnOrder[index];
            _turnOrder[index] = _turnOrder[swapIndex];
            _turnOrder[swapIndex] = temp;
        }

        _turnIndex = 0;
        RoundNumber = 1;
        _remainingTurnTime = turnTimeSeconds;
        _isRunning = _turnOrder.Count > 0;

        GD.Print($"[TurnManager] Random turn order: {string.Join(", ", _turnOrder)}");
        EmitTurnChanged(turnTimeSeconds);
    }

    /// <summary>
    /// Sets the turn order from an explicit list (used by online clients receiving
    /// the host's shuffled order). No randomization is performed.
    /// </summary>
    public void ConfigureWithOrder(IReadOnlyList<PlayerSlot> order, float turnTimeSeconds)
    {
        _turnOrder.Clear();
        for (int i = 0; i < order.Count; i++)
            _turnOrder.Add(order[i]);

        _turnIndex = 0;
        RoundNumber = 1;
        _remainingTurnTime = turnTimeSeconds;
        _isRunning = _turnOrder.Count > 0;

        GD.Print($"[TurnManager] Received turn order: {string.Join(", ", _turnOrder)}");
        EmitTurnChanged(turnTimeSeconds);
    }

    public void StartTurnClock(float turnTimeSeconds)
    {
        _remainingTurnTime = turnTimeSeconds;
        _isRunning = _turnOrder.Count > 0;
        EmitTurnChanged(turnTimeSeconds);
    }

    public void StopTurnClock()
    {
        _isRunning = false;
    }

    public bool AdvanceTurn(float turnTimeSeconds)
    {
        if (_turnOrder.Count == 0)
        {
            return false;
        }

        // Artillery Dominance: always snap back to the forced player
        if (_forcedPlayer.HasValue)
        {
            RoundNumber++;
            ForceCurrentPlayer(_forcedPlayer.Value);
            return true;
        }

        _turnIndex++;
        if (_turnIndex >= _turnOrder.Count)
        {
            _turnIndex = 0;
            RoundNumber++;
        }

        _remainingTurnTime = turnTimeSeconds;
        _isRunning = true;
        EmitTurnChanged(turnTimeSeconds);
        return true;
    }

    /// <summary>
    /// Skips the current player's turn and advances to the next player.
    /// </summary>
    public bool SkipTurn(float turnTimeSeconds)
    {
        return AdvanceTurn(turnTimeSeconds);
    }

    /// <summary>
    /// Forces the current turn to the specified player (e.g. Artillery Dominance).
    /// </summary>
    /// <summary>
    /// Forces the current turn to the specified player with unlimited time.
    /// While forced, AdvanceTurn/SkipTurn always return to this player.
    /// Call ClearForcedPlayer() to resume normal turn rotation.
    /// </summary>
    public void ForceCurrentPlayer(PlayerSlot player)
    {
        int idx = _turnOrder.IndexOf(player);
        if (idx >= 0)
        {
            _forcedPlayer = player;
            _turnIndex = idx;
            _remainingTurnTime = float.PositiveInfinity;
            _isRunning = true;
            EmitTurnChanged(float.PositiveInfinity);
        }
    }

    public void ClearForcedPlayer()
    {
        _forcedPlayer = null;
    }

    public void RemovePlayer(PlayerSlot player, float turnTimeSeconds)
    {
        int removedIndex = _turnOrder.IndexOf(player);
        if (removedIndex < 0)
        {
            return;
        }

        bool isCurrentPlayer = removedIndex == _turnIndex;

        _turnOrder.RemoveAt(removedIndex);
        if (_turnOrder.Count == 0)
        {
            _turnIndex = 0;
            _isRunning = false;
            return;
        }

        if (removedIndex <= _turnIndex)
        {
            _turnIndex = Math.Max(0, _turnIndex - 1);
        }

        _turnIndex %= _turnOrder.Count;

        // Only reset timer and emit TurnChanged if the removed player was the current player.
        // Removing a non-active player should adjust the index silently.
        if (isCurrentPlayer)
        {
            _remainingTurnTime = turnTimeSeconds;
            EmitTurnChanged(turnTimeSeconds);
        }
    }

    /// <summary>
    /// Force-syncs the turn state to match the host's broadcast.
    /// Used by clients receiving TurnAdvance messages.
    /// </summary>
    public void SyncToState(PlayerSlot player, int roundNumber, float turnTime)
    {
        int idx = _turnOrder.IndexOf(player);
        if (idx < 0)
        {
            GD.PrintErr($"[TurnManager] SyncToState: {player} not in turn order!");
            return;
        }
        _turnIndex = idx;
        RoundNumber = roundNumber;
        _remainingTurnTime = turnTime;
        _isRunning = true;
        GD.Print($"[TurnManager] Synced to {player}, round {roundNumber}");
        EmitTurnChanged(turnTime);
    }

    public override void _Process(double delta)
    {
        if (!_isRunning || _turnOrder.Count == 0 || float.IsInfinity(_remainingTurnTime))
        {
            return;
        }

        _remainingTurnTime = Math.Max(0f, _remainingTurnTime - (float)delta);
        if (_remainingTurnTime <= 0f)
        {
            if (!IsAuthoritative) return; // Client: wait for host broadcast
            // Fire event so GameManager can advance through the authoritative path
            // (which broadcasts to clients in multiplayer)
            TurnTimedOut?.Invoke();
            if (_remainingTurnTime <= 0f)
            {
                // Fallback if nobody handled the event
                AdvanceTurn(GameConfig.DefaultTurnTime);
            }
        }
    }

    private void EmitTurnChanged(float turnTimeSeconds)
    {
        PlayerSlot? current = CurrentPlayer;
        if (current.HasValue)
        {
            EventBus.Instance?.EmitTurnChanged(new TurnChangedEvent(current.Value, RoundNumber, turnTimeSeconds));
        }
    }
}

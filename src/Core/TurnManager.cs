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

    public IReadOnlyList<PlayerSlot> TurnOrder => _turnOrder;
    public int RoundNumber { get; private set; } = 1;
    public PlayerSlot? CurrentPlayer => _turnOrder.Count == 0 ? null : _turnOrder[_turnIndex];
    public float RemainingTurnTime => _remainingTurnTime;
    public bool IsRunning => _isRunning;

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

    public override void _Process(double delta)
    {
        if (!_isRunning || _turnOrder.Count == 0 || float.IsInfinity(_remainingTurnTime))
        {
            return;
        }

        _remainingTurnTime = Math.Max(0f, _remainingTurnTime - (float)delta);
        if (_remainingTurnTime <= 0f)
        {
            AdvanceTurn(GameConfig.DefaultTurnTime);
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

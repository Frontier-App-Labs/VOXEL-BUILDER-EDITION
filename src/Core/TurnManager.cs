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

    public void Configure(IEnumerable<PlayerSlot> alivePlayers, float turnTimeSeconds, RandomNumberGenerator? rng = null)
    {
        _turnOrder.Clear();
        _turnOrder.AddRange(alivePlayers);

        RandomNumberGenerator localRng = rng ?? new RandomNumberGenerator();
        localRng.Randomize();
        for (int index = _turnOrder.Count - 1; index > 0; index--)
        {
            int swapIndex = localRng.RandiRange(0, index);
            PlayerSlot temp = _turnOrder[index];
            _turnOrder[index] = _turnOrder[swapIndex];
            _turnOrder[swapIndex] = temp;
        }

        _turnIndex = 0;
        RoundNumber = 1;
        _remainingTurnTime = turnTimeSeconds;
        _isRunning = _turnOrder.Count > 0;
        EmitTurnChanged(turnTimeSeconds);
    }

    public void StartTurnClock(float turnTimeSeconds)
    {
        _remainingTurnTime = turnTimeSeconds;
        _isRunning = _turnOrder.Count > 0;
        EmitTurnChanged(turnTimeSeconds);
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

    public void RemovePlayer(PlayerSlot player, float turnTimeSeconds)
    {
        int removedIndex = _turnOrder.IndexOf(player);
        if (removedIndex < 0)
        {
            return;
        }

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
        _remainingTurnTime = turnTimeSeconds;
        EmitTurnChanged(turnTimeSeconds);
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

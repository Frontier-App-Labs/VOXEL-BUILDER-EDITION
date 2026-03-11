namespace VoxelSiege.Core;

public enum GamePhase
{
    Menu,
    Lobby,
    Building,
    FogReveal,
    Combat,
    GameOver,
}

public enum PlayerSlot
{
    Player1,
    Player2,
    Player3,
    Player4,
}

public enum BuildToolMode
{
    Single,
    HalfBlock,
    Line,
    Wall,
    Box,
    Floor,
    Ramp,
    Eraser,
    Copy,
    Paste,
    Door,
    Blueprint,
}

public enum BuildSymmetryMode
{
    None,
    MirrorX,
    MirrorZ,
    MirrorXZ,
}

public enum MatchVisibility
{
    Public,
    FriendsOnly,
    Private,
}

public enum FogMode
{
    Full,
    Partial,
    None,
}

public enum WeaponTier
{
    Tier1,
    Tier2,
    Tier3,
}

public enum WeaponType
{
    Cannon,
    Mortar,
    Railgun,
    MissileLauncher,
    Drill,
}

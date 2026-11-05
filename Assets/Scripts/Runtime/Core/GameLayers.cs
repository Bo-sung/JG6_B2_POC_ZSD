// ProjectSettings/TagManager.asset의 레이어 번호를 옮겨 둔 컴파일 타임 상수.
// 레이어 번호(~LayerNumber)와 Physics 질의용 비트마스크(~Mask)를 구분해 사용한다.
// TagManager.asset의 레이어 배치를 바꿀 때는 이 파일의 번호와 이름을 함께 고치고
// Tools/MyJG/Validate Project로 어긋남이 없는지 확인한다.
public static class GameLayers
{
    // 레이어 번호(TagManager.asset 순서 기준, 0부터 시작).
    public const int GroundLayerNumber = 7;
    public const int ObstacleLayerNumber = 8;
    public const int PlayerLayerNumber = 9;
    public const int EnemyLayerNumber = 10;
    public const int PropLayerNumber = 11;
    public const int WeaponLayerNumber = 12;
    public const int ProjectileLayerNumber = 13;

    // Physics 질의에 쓰는 비트마스크. 위 번호에서 파생된 컴파일 타임 상수.
    public const int GroundMask = 1 << GroundLayerNumber;
    public const int ObstacleMask = 1 << ObstacleLayerNumber;
    public const int PlayerMask = 1 << PlayerLayerNumber;
    public const int EnemyMask = 1 << EnemyLayerNumber;
    public const int PropMask = 1 << PropLayerNumber;
    public const int WeaponMask = 1 << WeaponLayerNumber;
    public const int ProjectileMask = 1 << ProjectileLayerNumber;

    // 자주 쓰는 결합 질의. 개별 질의마다 즉석 마스크를 만들지 않는다.
    public const int GroundPlayerMask = GroundMask | PlayerMask;
    public const int GroundObstacleMask = GroundMask | ObstacleMask;
    public const int CharacterMask = PlayerMask | EnemyMask;
    public const int WorldMask = GroundMask | ObstacleMask | PropMask;

    // Validate Project가 TagManager.asset의 이름과 대조하는 표. 번호를 바꾸면 여기도 함께 고친다.
    public static readonly (int number, string name)[] TagManagerNames =
    {
        (GroundLayerNumber, "Ground"),
        (ObstacleLayerNumber, "Obstacle"),
        (PlayerLayerNumber, "Player"),
        (EnemyLayerNumber, "Enemy"),
        (PropLayerNumber, "Prop"),
        (WeaponLayerNumber, "Weapon"),
        (ProjectileLayerNumber, "Projectile"),
    };
}

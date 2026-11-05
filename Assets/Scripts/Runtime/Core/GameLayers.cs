// ProjectSettings/TagManager.asset의 레이어 번호를 옮겨 둔 컴파일 타임 상수.
// 레이어 번호(~LayerNumber)와 Physics 질의용 비트마스크(~Mask)를 구분해 사용한다.
// TagManager.asset의 레이어 배치를 바꿀 때는 이 파일의 번호와 이름을 함께 고치고
// Tools/MyJG/Validate Project로 어긋남이 없는지 확인한다.
public static class GameLayers
{
    // 레이어 번호(TagManager.asset 순서 기준, 0부터 시작).
    public const int GROUND_LAYER_NUMBER = 7;
    public const int OBSTACLE_LAYER_NUMBER = 8;
    public const int PLAYER_LAYER_NUMBER = 9;
    public const int ENEMY_LAYER_NUMBER = 10;
    public const int PROP_LAYER_NUMBER = 11;
    public const int WEAPON_LAYER_NUMBER = 12;
    public const int PROJECTILE_LAYER_NUMBER = 13;

    // Physics 질의에 쓰는 비트마스크. 위 번호에서 파생된 컴파일 타임 상수.
    public const int GROUND_MASK = 1 << GROUND_LAYER_NUMBER;
    public const int OBSTACLE_MASK = 1 << OBSTACLE_LAYER_NUMBER;
    public const int PLAYER_MASK = 1 << PLAYER_LAYER_NUMBER;
    public const int ENEMY_MASK = 1 << ENEMY_LAYER_NUMBER;
    public const int PROP_MASK = 1 << PROP_LAYER_NUMBER;
    public const int WEAPON_MASK = 1 << WEAPON_LAYER_NUMBER;
    public const int PROJECTILE_MASK = 1 << PROJECTILE_LAYER_NUMBER;

    // 자주 쓰는 결합 질의. 개별 질의마다 즉석 마스크를 만들지 않는다.
    public const int GROUND_PLAYER_MASK = GROUND_MASK | PLAYER_MASK;
    public const int GROUND_OBSTACLE_MASK = GROUND_MASK | OBSTACLE_MASK;
    public const int CHARACTER_MASK = PLAYER_MASK | ENEMY_MASK;
    public const int WORLD_MASK = GROUND_MASK | OBSTACLE_MASK | PROP_MASK;

    // Validate Project가 TagManager.asset의 이름과 대조하는 표. 번호를 바꾸면 여기도 함께 고친다.
    public static readonly (int number, string name)[] TAG_MANAGER_NAMES =
    {
        (GROUND_LAYER_NUMBER, "Ground"),
        (OBSTACLE_LAYER_NUMBER, "Obstacle"),
        (PLAYER_LAYER_NUMBER, "Player"),
        (ENEMY_LAYER_NUMBER, "Enemy"),
        (PROP_LAYER_NUMBER, "Prop"),
        (WEAPON_LAYER_NUMBER, "Weapon"),
        (PROJECTILE_LAYER_NUMBER, "Projectile"),
    };
}

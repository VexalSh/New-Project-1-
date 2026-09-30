using Godot;

public partial class Projectile : CharacterBody2D
{
	[Export] public float Speed = 300f;
	[Export] public float Lifetime = 5f;

	[Export] public NodePath SpritePath = "AnimatedSprite2D";
	[Export] public string AnimDefault = "default";

	public Vector2 Direction = Vector2.Right;

	private AnimatedSprite2D _sprite;
	private float _lifeTimer;

	public override void _Ready()
	{
		_sprite = GetNodeOrNull<AnimatedSprite2D>(SpritePath);
		if (_sprite != null && _sprite.SpriteFrames != null && _sprite.SpriteFrames.HasAnimation(AnimDefault))
			_sprite.Play(AnimDefault);

		Direction = Direction.Normalized();
		Rotation = Direction.Angle();
		Velocity = Direction * Speed;
		_lifeTimer = Lifetime;
	}

	public override void _PhysicsProcess(double delta)
	{
		_lifeTimer -= (float)delta;
		if (_lifeTimer <= 0f)
		{
			QueueFree();
			return;
		}

		MoveAndSlide();

		if (GetSlideCollisionCount() > 0)
			QueueFree();
	}
}

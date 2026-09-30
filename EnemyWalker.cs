using Godot;

public partial class EnemyWalker : CharacterBody2D
{
	[Export] public float WalkSpeed = 40f;
	[Export] public float Gravity = 900f;
	[Export] public float MaxFallSpeed = 600f;

	[ExportGroup("Node References")]
	[Export] public NodePath SpritePath = "AnimatedSprite2D";
	[Export] public NodePath EdgeRayPath = "EdgeRayCast2D";

	[ExportGroup("Animation Clip Names")]
	[Export] public string AnimWalking = "walking";

	private AnimatedSprite2D _sprite;
	private RayCast2D _edgeRay;
	private Node2D _player;
	private int _facing = 1;
	private bool _wasActive = true;

	public override void _Ready()
	{
		_sprite = GetNodeOrNull<AnimatedSprite2D>(SpritePath);
		_edgeRay = GetNodeOrNull<RayCast2D>(EdgeRayPath);
		_player = GetTree().GetFirstNodeInGroup("player") as Node2D;

		FaceDirection(_facing);
		_sprite.Play(AnimWalking);
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		bool active = RoomActivity.IsPlayerInSameRoom(this, _player);

		if (active != _wasActive)
		{
			_wasActive = active;
			if (active) _sprite.Play(AnimWalking);
			else _sprite?.Stop();
		}

		Vector2 v = Velocity;
		v.Y = Mathf.Min(v.Y + Gravity * dt, MaxFallSpeed);
		v.X = active ? WalkSpeed * _facing : 0f;
		Velocity = v;

		MoveAndSlide();

		if (!active) return;

		bool aboutToFallOffEdge = _edgeRay != null && !_edgeRay.IsColliding();
		if (IsOnWall() || aboutToFallOffEdge)
			TurnAround();
	}

	private void TurnAround()
	{
		FaceDirection(-_facing);
		GlobalPosition += new Vector2(_facing * 2f, 0f);
	}

	private void FaceDirection(int facing)
	{
		_facing = facing;
		if (_sprite != null) _sprite.FlipH = _facing < 0;

		if (_edgeRay != null)
		{
			Vector2 pos = _edgeRay.Position;
			pos.X = Mathf.Abs(pos.X) * _facing;
			_edgeRay.Position = pos;

			Vector2 target = _edgeRay.TargetPosition;
			target.X = Mathf.Abs(target.X) * _facing;
			_edgeRay.TargetPosition = target;
		}
	}
}

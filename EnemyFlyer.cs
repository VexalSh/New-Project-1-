using Godot;

public partial class EnemyFlyer : CharacterBody2D
{
	[Export] public float FlySpeed = 60f;
	[Export] public float DetectionRadius = 220f;
	[Export] public float FieldOfViewDegrees = 100f; // total cone width, centered on facing direction
	[Export] public bool RequireLineOfSight = true;
	[Export] public float TurnSpeed = 6f;

	[ExportGroup("Node References")]
	[Export] public NodePath SpritePath = "AnimatedSprite2D";

	[ExportGroup("Animation Clip Names")]
	[Export] public string AnimStationary = "stationary";
	[Export] public string AnimFlying = "flying";

	private AnimatedSprite2D _sprite;
	private Node2D _player;
	private int _facing = 1;
	private bool _chasing = false;

	public override void _Ready()
	{
		_sprite = GetNodeOrNull<AnimatedSprite2D>(SpritePath);
		_player = GetTree().GetFirstNodeInGroup("player") as Node2D;

		PlayAnim(AnimStationary);
	}

	public override void _PhysicsProcess(double delta)
	{

		bool active = RoomActivity.IsPlayerInSameRoom(this, _player);
		bool canSeePlayer = active && IsInstanceValid(_player) && CanSeePlayer();

		if (canSeePlayer)
		{
			Vector2 toPlayer = _player.GlobalPosition - GlobalPosition;
			Velocity = toPlayer.Normalized() * FlySpeed;
			
			float targetRotation = toPlayer.Angle();
			Rotation = Mathf.LerpAngle(Rotation, targetRotation, TurnSpeed * (float)delta);

			if (!_chasing)
			{
				_chasing = true;
				PlayAnim(AnimFlying);
			}
		}
		else
		{
			Velocity = Vector2.Zero;
			if (_chasing)
			{
				_chasing = false;
				PlayAnim(AnimStationary);
			}
		}

		MoveAndSlide();
	}

	private bool CanSeePlayer()
	{
		Vector2 toPlayer = _player.GlobalPosition - GlobalPosition;
		float distance = toPlayer.Length();
		if (distance > DetectionRadius) return false;

		Vector2 forward = Transform.X;
		float angle = Mathf.Abs(Mathf.RadToDeg(forward.AngleTo(toPlayer)));
		if (angle > FieldOfViewDegrees * 0.5f) return false;

		if (RequireLineOfSight)
		{
			PhysicsDirectSpaceState2D spaceState = GetWorld2D().DirectSpaceState;
			PhysicsRayQueryParameters2D query = PhysicsRayQueryParameters2D.Create(GlobalPosition, _player.GlobalPosition);
			query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };

			Godot.Collections.Dictionary result = spaceState.IntersectRay(query);
			if (result.Count > 0 && result["collider"].As<GodotObject>() != (GodotObject)_player)
				return false; // something else is blocking the view
		}

		return true;
	}

	private void PlayAnim(string clipName)
	{
		if (_sprite != null && _sprite.SpriteFrames != null && _sprite.SpriteFrames.HasAnimation(clipName))
			_sprite.Play(clipName);
	}
}

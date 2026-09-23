using Godot;

/// <summary>
/// Hovers in place until the player enters its field of view (within range,
/// within a facing-cone, and optionally with clear line of sight), then
/// flies straight at them. Dies when the player stomps it from above.
///
/// Setup:
///   - Node type: CharacterBody2D, with a CollisionShape2D. Give it its own
///     collision layer/mask as you like (it ignores gravity, so it doesn't
///     need to be on a "ground" layer) - but it does need to be included
///     in the player's CollisionMask for stomping to register.
///   - Child AnimatedSprite2D with clips "stationary", "flying", and
///     "death" (death non-looping).
///   - Relies on the player's CharacterBody2D being in the "player" group
///     (PlayerController already adds itself to this automatically).
/// </summary>
public partial class EnemyFlyer : CharacterBody2D, IStompable
{
	[Export] public float FlySpeed = 60f;
	[Export] public float DetectionRadius = 220f;
	[Export] public float FieldOfViewDegrees = 100f; // total cone width, centered on facing direction
	[Export] public bool RequireLineOfSight = true;

	[ExportGroup("Node References")]
	[Export] public NodePath SpritePath = "AnimatedSprite2D";

	[ExportGroup("Animation Clip Names")]
	[Export] public string AnimStationary = "stationary";
	[Export] public string AnimFlying = "flying";
	[Export] public string AnimDeath = "death";

	private AnimatedSprite2D _sprite;
	private Node2D _player;
	private int _facing = 1;
	private bool _chasing = false;
	private bool _dead = false;

	public override void _Ready()
	{
		_sprite = GetNodeOrNull<AnimatedSprite2D>(SpritePath);
		_player = GetTree().GetFirstNodeInGroup("player") as Node2D;

		if (_sprite != null)
			_sprite.AnimationFinished += OnAnimationFinished;

		PlayAnim(AnimStationary);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_dead) return;

		bool active = RoomActivity.IsPlayerInSameRoom(this, _player);
		bool canSeePlayer = active && IsInstanceValid(_player) && CanSeePlayer();

		if (canSeePlayer)
		{
			Vector2 toPlayer = _player.GlobalPosition - GlobalPosition;
			Velocity = toPlayer.Normalized() * FlySpeed;
			_facing = toPlayer.X >= 0f ? 1 : -1;
			if (_sprite != null) _sprite.FlipH = _facing < 0;

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

		Vector2 forward = new Vector2(_facing, 0f);
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

	/// <summary>IStompable implementation - the player calls this when it
	/// lands on this enemy from above.</summary>
	public bool Stomp(CharacterBody2D player)
	{
		if (_dead) return false;

		_dead = true;
		Velocity = Vector2.Zero;
		CollisionLayer = 0;
		CollisionMask = 0;
		PlayAnim(AnimDeath);
		return true;
	}

	private void PlayAnim(string clipName)
	{
		if (_sprite != null && _sprite.SpriteFrames != null && _sprite.SpriteFrames.HasAnimation(clipName))
			_sprite.Play(clipName);
	}

	private void OnAnimationFinished()
	{
		// "stationary"/"flying" loop, so this fires every cycle too - only
		// act on it once actually dead (playing the non-looping "death" clip).
		if (_dead) QueueFree();
	}
}

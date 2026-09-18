using Godot;

/// Walks back and forth, turning around when it hits a wall or is about to
/// walk off a ledge. Dies when the player stomps it from above.
///
/// Setup:
///   - Node type: CharacterBody2D, with a CollisionShape2D for its body.
///   - Add a RayCast2D child (see EdgeRayPath) positioned near its feet,
///     pointed straight down with a short TargetPosition (e.g. (0, 20)) -
///     it's used to detect "no floor ahead" before walking off an edge.
///     Enable it (Enabled = true).
///   - Child AnimatedSprite2D with clips "walking" and "death" (death
///     should be non-looping).
///   - Needs to be in the player's own CollisionMask for stomping to be
///     detected at all (stomp relies on real physical collision, unlike
///     the hazard layers).
public partial class EnemyWalker : CharacterBody2D, IStompable
{
	[Export] public float WalkSpeed = 40f;
	[Export] public float Gravity = 900f;
	[Export] public float MaxFallSpeed = 600f;

	[ExportGroup("Node References")]
	[Export] public NodePath SpritePath = "AnimatedSprite2D";
	[Export] public NodePath EdgeRayPath = "EdgeRayCast2D";

	[ExportGroup("Animation Clip Names")]
	[Export] public string AnimWalking = "walking";
	[Export] public string AnimDeath = "death";

	private AnimatedSprite2D _sprite;
	private RayCast2D _edgeRay;
	private Node2D _player;
	private int _facing = 1; // 1 = right, -1 = left
	private bool _dead = false;
	private bool _wasActive = true;

	public override void _Ready()
	{
		_sprite = GetNodeOrNull<AnimatedSprite2D>(SpritePath);
		_edgeRay = GetNodeOrNull<RayCast2D>(EdgeRayPath);
		_player = GetTree().GetFirstNodeInGroup("player") as Node2D;

		if (_sprite != null)
			_sprite.AnimationFinished += OnAnimationFinished;

		FaceDirection(_facing);
		PlayAnim(AnimWalking);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_dead) return;

		float dt = (float)delta;
		bool active = RoomActivity.IsPlayerInSameRoom(this, _player);

		if (active != _wasActive)
		{
			_wasActive = active;
			if (active) PlayAnim(AnimWalking);
			else _sprite?.Stop(); // freeze on whatever frame rather than "walking" in place
		}

		Vector2 v = Velocity;
		v.Y = Mathf.Min(v.Y + Gravity * dt, MaxFallSpeed);
		v.X = active ? WalkSpeed * _facing : 0f; // still falls/stays grounded while inactive, just doesn't patrol
		Velocity = v;

		MoveAndSlide();

		if (!active) return; // paused - skip wall/edge turn checks too

		bool aboutToFallOffEdge = _edgeRay != null && !_edgeRay.IsColliding();
		if (IsOnWall() || aboutToFallOffEdge)
			TurnAround();
	}

	private void TurnAround()
	{
		FaceDirection(-_facing);

		// Nudge slightly away from the wall/edge so the same trigger doesn't
		// immediately fire again next frame before it's had a chance to move.
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

	public bool Stomp(CharacterBody2D player)
	{
		if (_dead) return false;

		_dead = true;
		Velocity = Vector2.Zero;
		CollisionLayer = 0; // stop blocking movement or being stomped again
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
		if (_dead) QueueFree();
	}
}

using Godot;

public partial class Turret : CharacterBody2D
{
	[Export] public float Gravity = 900f;
	[Export] public float MaxFallSpeed = 600f;

	[ExportGroup("Aiming")]
	[Export] public bool TrackPlayer = true;
	[Export] public float DetectionRadius = 300f;        // 0 = unlimited
	[Export(PropertyHint.Range, "0,180")]
	public float AimArcDegrees = 180f;                  // total arc it can look through, centered on straight-out
	[Export] public bool SnapShotsToSpriteAngle = true; // fire exactly where the sprite visibly points
	[Export] public float SpawnOffset { get; set; } = 20.0f;

	[ExportGroup("Firing")]
	[Export] public PackedScene ProjectileScene;
	[Export] public float FireInterval = 2f;
	[Export] public NodePath MuzzlePath = "";

	[ExportGroup("Node References")]
	[Export] public NodePath SpritePath = "AnimatedSprite2D";

	[ExportGroup("Animation Clip Names")]
	[Export] public string AnimLooking90 = "looking 90";
	[Export] public string AnimLooking135 = "looking 135";
	[Export] public string AnimLooking180 = "looking 180";
	[Export] public string AnimLooking225 = "looking 225";
	[Export] public string AnimLooking270 = "looking 270";

	// Local "straight out from the surface" direction in the art: LEFT (180 deg).
	private const float LocalOutAngle = Mathf.Pi;

	private AnimatedSprite2D _sprite;
	private Node2D _muzzle;
	private Node2D _player;

	private bool _attached = false;
	private Vector2 _attachNormal = Vector2.Left; // world direction away from the mounting surface
	private Vector2 _aimDirection = Vector2.Left; // world direction the turret is looking / firing
	private float _fireTimer;

	private string _currentClip = "";

	public override void _Ready()
	{
		_sprite = GetNodeOrNull<AnimatedSprite2D>(SpritePath);
		_muzzle = !MuzzlePath.IsEmpty ? GetNodeOrNull<Node2D>(MuzzlePath) : null;
		_player = GetTree().GetFirstNodeInGroup("player") as Node2D;
		_fireTimer = FireInterval;

		if (_sprite != null)
		{
			_sprite.FlipH = false;
			_sprite.FlipV = false;
		}

		UpdateLookVisual(Vector2.Left);
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		if (!_attached)
		{
			Vector2 v = Velocity;
			v.Y = Mathf.Min(v.Y + Gravity * dt, MaxFallSpeed);
			Velocity = v;

			MoveAndSlide();
			TryAttach();
			return;
		}

		// Attached: never moves again.
		Velocity = Vector2.Zero;

		bool playerActive = IsInstanceValid(_player) && RoomActivity.IsPlayerInSameRoom(this, _player);

		// --- Aiming ---
		Vector2 desiredAim = _attachNormal; // rest pose: straight out from the surface
		if (TrackPlayer && playerActive)
		{
			Vector2 toPlayer = _player.GlobalPosition - GetFireOrigin();
			if (DetectionRadius <= 0f || toPlayer.Length() <= DetectionRadius)
				desiredAim = toPlayer;
		}
		UpdateLookVisual(desiredAim);

		// --- Firing (only while player is in the room; timer pauses otherwise) ---
		if (ProjectileScene != null && playerActive)
		{
			_fireTimer -= dt;
			if (_fireTimer <= 0f)
			{
				_fireTimer = FireInterval;
				Fire();
			}
		}
	}

	private void TryAttach()
	{
		Vector2 normal;

		if (IsOnFloor())
			normal = GetFloorNormal();
		else if (IsOnWall())
			normal = GetWallNormal();
		else if (IsOnCeiling())
		{
			normal = Vector2.Down;
			for (int i = 0; i < GetSlideCollisionCount(); i++)
			{
				Vector2 n = GetSlideCollision(i).GetNormal();
				if (n.Y > 0.5f) { normal = n; break; }
			}
		}
		else
		{
			return; // still falling
		}

		_attached = true;
		_attachNormal = normal.Normalized();
		Velocity = Vector2.Zero;

		// PHYSICAL attachment: rotate so the art's local "out" (LEFT) = surface normal.
		//   right wall (normal Left)  ->   0 deg
		//   floor      (normal Up)    ->  90 deg
		//   left wall  (normal Right) -> 180 deg
		//   ceiling    (normal Down)  -> 270 deg
		Rotation = _attachNormal.Angle() - LocalOutAngle;

		UpdateLookVisual(_attachNormal);
	}

	/// <summary>
	/// Converts a desired WORLD aim direction into the turret's local
	/// (mount-relative) space, clamps it to the allowed arc, picks the
	/// matching "looking N" clip, and stores the resulting world aim.
	/// Never flips the sprite.
	/// </summary>
	private void UpdateLookVisual(Vector2 worldAim)
	{
		if (worldAim == Vector2.Zero) worldAim = _attachNormal;

		// World -> local (undo the mount rotation).
		float localAngle = worldAim.Rotated(-Rotation).Angle();

		// Clamp around local "straight out" (180 deg), max +/-90 deg.
		float halfArc = Mathf.DegToRad(Mathf.Clamp(AimArcDegrees, 0f, 180f) * 0.5f);
		float offset = Mathf.Wrap(localAngle - LocalOutAngle, -Mathf.Pi, Mathf.Pi);
		offset = Mathf.Clamp(offset, -halfArc, halfArc);
		localAngle = LocalOutAngle + offset;

		// Snap to nearest 45-degree clip within [90, 270].
		float deg = Mathf.PosMod(Mathf.RadToDeg(localAngle), 360f);
		int snapped = Mathf.Clamp(Mathf.RoundToInt(deg / 45f) * 45, 90, 270);

		string clip = snapped switch
		{
			90 => AnimLooking90,
			135 => AnimLooking135,
			180 => AnimLooking180,
			225 => AnimLooking225,
			_ => AnimLooking270,
		};

		// Store the world aim direction for firing.
		float fireLocalAngle = SnapShotsToSpriteAngle ? Mathf.DegToRad(snapped) : localAngle;
		_aimDirection = Vector2.FromAngle(fireLocalAngle + Rotation);

		// Apply visuals only when the clip changes. No flipping, ever.
		if (_sprite != null && clip != _currentClip)
		{
			_currentClip = clip;
			if (_sprite.SpriteFrames != null && _sprite.SpriteFrames.HasAnimation(clip))
				_sprite.Play(clip);
		}
	}

	private Vector2 GetFireOrigin()
	{
		return _muzzle != null ? _muzzle.GlobalPosition : GlobalPosition;
	}

	private void Fire()
	{
		Node2D projectile = ProjectileScene.Instantiate<Node2D>();
		
		Vector2 normalizedDirection = _aimDirection.Normalized();

		if (projectile is Projectile p)
		{
			p.Direction = normalizedDirection;
		}

		projectile.GlobalPosition = GetFireOrigin() + (normalizedDirection * SpawnOffset);
		
		GetParent().AddChild(projectile);
	}

}

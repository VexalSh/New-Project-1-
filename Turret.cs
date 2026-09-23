using Godot;

/// <summary>
/// Falls under gravity until it touches a surface, then permanently
/// attaches to it (stops moving) and orients itself using whichever of the
/// 5 "looking N" animations best matches the surface's angle - floor,
/// ceiling, and either wall are all covered by mirroring (FlipH) rather
/// than needing separate art for left vs right walls. Does not die. Once
/// attached, periodically fires a projectile straight out from the
/// surface it's mounted on.
///
/// Orientation logic: the 5 clips are treated as 45-degree steps covering
/// the angle range [90, 270] (Godot's 2D angle convention: 0=right,
/// 90=down, 180=left, 270=up). A surface normal that already falls in that
/// range is used as-is; one outside it is mirrored (angle -> 180 - angle,
/// plus FlipH) to bring it into range. With that convention: a ceiling
/// (normal pointing down) lands near 90, a wall normal pointing left lands
/// near 180, and a floor (normal pointing up) lands near 270 - reassign
/// AnimLooking* below if your art uses a different convention.
///
/// Setup:
///   - Node type: CharacterBody2D, with a CollisionShape2D.
///   - Child AnimatedSprite2D with clips "looking 90" / 135 / 180 / 225 /
///     270 (all looping is fine, or a held single frame - whichever you
///     drew them as).
///   - Priority when touching multiple surfaces at once (e.g. a corner) is
///     floor, then wall, then ceiling.
///   - Set ProjectileScene to a scene using Projectile.cs to enable firing;
///     leave it empty for a purely decorative attaching creature.
/// </summary>
public partial class Turret : CharacterBody2D
{
	[Export] public float Gravity = 900f;
	[Export] public float MaxFallSpeed = 600f;

	[ExportGroup("Firing")]
	[Export] public PackedScene ProjectileScene;
	[Export] public float FireInterval = 2f;
	[Export] public NodePath MuzzlePath = ""; // optional child Node2D marking the spawn offset; defaults to this node's own position

	[ExportGroup("Node References")]
	[Export] public NodePath SpritePath = "AnimatedSprite2D";

	[ExportGroup("Animation Clip Names")]
	[Export] public string AnimLooking90 = "looking 90";
	[Export] public string AnimLooking135 = "looking 135";
	[Export] public string AnimLooking180 = "looking 180";
	[Export] public string AnimLooking225 = "looking 225";
	[Export] public string AnimLooking270 = "looking 270";

	private AnimatedSprite2D _sprite;
	private Node2D _muzzle;
	private Node2D _player;
	private bool _attached = false;
	private Vector2 _attachNormal = Vector2.Up; // direction to fire in - away from the mounting surface
	private float _fireTimer;

	public override void _Ready()
	{
		_sprite = GetNodeOrNull<AnimatedSprite2D>(SpritePath);
		_muzzle = !MuzzlePath.IsEmpty ? GetNodeOrNull<Node2D>(MuzzlePath) : null;
		_player = GetTree().GetFirstNodeInGroup("player") as Node2D;
		_fireTimer = FireInterval;
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
			TryAttach(); // always settles onto a surface regardless of the player's room
			return;
		}

		// Attached: sit completely still from here on.
		Velocity = Vector2.Zero;

		// Firing only happens while the player's in the same room - the timer
		// simply doesn't tick down otherwise, so it doesn't "catch up" with a
		// surprise shot the instant the player walks back in.
		if (ProjectileScene != null && RoomActivity.IsPlayerInSameRoom(this, _player))
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
			return; // still falling, nothing to attach to yet
		}

		_attached = true;
		_attachNormal = normal;
		ApplyOrientation(normal);
	}

	private void ApplyOrientation(Vector2 normal)
	{
		float rawDegrees = Mathf.RadToDeg(normal.Angle());
		rawDegrees = ((rawDegrees % 360f) + 360f) % 360f; // normalize to [0, 360)

		bool flip = rawDegrees < 90f || rawDegrees > 270f;
		float effectiveDegrees = flip ? 180f - rawDegrees : rawDegrees;
		effectiveDegrees = ((effectiveDegrees % 360f) + 360f) % 360f;

		// Snap to the nearest 45-degree step among the 5 clips.
		float snapped = Mathf.Round(effectiveDegrees / 45f) * 45f;
		snapped = Mathf.Clamp(snapped, 90f, 270f);

		string clip = snapped switch
		{
			90f => AnimLooking90,
			135f => AnimLooking135,
			180f => AnimLooking180,
			225f => AnimLooking225,
			_ => AnimLooking270,
		};

		if (_sprite != null)
		{
			_sprite.FlipH = flip;
			if (_sprite.SpriteFrames != null && _sprite.SpriteFrames.HasAnimation(clip))
				_sprite.Play(clip);
		}
	}

	private void Fire()
	{
		Node2D projectile = ProjectileScene.Instantiate<Node2D>();
		GetParent().AddChild(projectile); // sibling, not a child, so it isn't affected by this node's own transform and survives independently
		projectile.GlobalPosition = _muzzle != null ? _muzzle.GlobalPosition : GlobalPosition;

		if (projectile is Projectile p)
			p.Direction = _attachNormal;
	}
}

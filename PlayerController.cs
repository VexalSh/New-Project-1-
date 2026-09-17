// Generated initially using Claude

using Godot;

public partial class PlayerController : CharacterBody2D, IKnockbackable
{
	[ExportGroup("Movement Speeds")]
	[Export] public float WalkSpeed = 30f;
	[Export] public float RunSpeed = 110f;

	[ExportGroup("Acceleration")]
	[Export] public float GroundAcceleration = 1200f;
	[Export] public float AirAcceleration = 600f;

	[Export] public float InputDeadzone = 0.15f;

	[ExportGroup("Jump")]
	[Export] public float JumpVelocity = -320f;
	[Export] public float Gravity = 900f;
	[Export] public float MaxFallSpeed = 600f;

	[ExportGroup("Wall Jump")]
	[Export] public float WallJumpPushVelocity = 250f;
	[Export] public float WallJumpVerticalVelocity = -300f;
	[Export] public float WallSlideGravityScale = 0.3f;
	[Export] public bool RequireInputTowardWallToHang = false;

	[ExportGroup("Health")]
	[Export] public float MaxHealth = 100f;
	[Export] public float RegenRate = 5f;
	[Export] public float RegenDelay = 2f;
	[Export] public float InvulnerabilityDuration = 1f;
	[Export] public float HurtFlickerInterval = 0.08f;

	[ExportGroup("Hazard Layers")]
	[Export(PropertyHint.Layers2DPhysics)] public uint HazardLayerMask = 1u << 4;
	[Export(PropertyHint.Layers2DPhysics)] public uint HazardHeavyLayerMask = 1u << 5;
	[Export(PropertyHint.Layers2DPhysics)] public uint HazardInstantLayerMask = 1u << 6;

	[Export] public float HazardDamage = 10f;
	[Export] public Vector2 HazardKnockbackForce = Vector2.Zero;
	[Export] public float HazardKnockbackDuration = 0.25f;

	[ExportGroup("Death")]
	[Export] public PackedScene DeathScene;

	[ExportGroup("Air Jump Refill")]
	[Export] public float DoubleJumpVelocity = -280f;

	[ExportGroup("Node References")]
	[Export] public NodePath SpritePath = "AnimatedSprite2D";
	[Export] public NodePath FeetSpritePath = "";
	[Export] public NodePath CollisionShapePath = "CollisionShape2D";

	[ExportGroup("Animation Clip Names")]
	[Export] public string AnimIdle = "idle";
	[Export] public string AnimWalk = "walk";
	[Export] public string AnimJog = "jog";
	[Export] public string AnimRun = "run";
	[Export] public string AnimRunTurn = "run_turn";
	[Export] public string AnimJumpStart = "jump_start";
	[Export] public string AnimJump = "jump";
	[Export] public string AnimJumpTurn = "jump_turn";
	[Export] public string AnimJumpEnd = "jump_end";
	[Export] public string AnimHang = "hang";

	[Signal] public delegate void StateChangedEventHandler(string newState);
	[Signal] public delegate void HealthChangedEventHandler(float current, float max);
	[Signal] public delegate void DiedEventHandler();

	private enum State
	{
		Idle, Walk, Jog, Run, RunTurn,
		JumpStart, JumpLoop, JumpTurn, JumpEnd,
		WallHang
	}

	private AnimatedSprite2D _sprite;
	private AnimatedSprite2D _feetSprite;
	private CollisionShape2D _collisionShape;

	private State _state = State.Idle;
	private int _facing = 1;
	private int _preHangFacing = 1;
	private bool _wasOnFloor = true;

	private bool _hasMoveTierBaseline = false;
	private bool _lastTierWasWalk = false;

	private float _knockbackTimer = 0f;

	public float CurrentHealth { get; private set; }
	private bool _isInvulnerable = false;
	private bool _isDead = false;
	private float _invulnTimer = 0f;
	private float _flickerTimer = 0f;
	private float _regenDelayTimer = 0f;

	private bool _airJumpAvailable = false;

	public override void _Ready()
	{
		AddToGroup("player");
		CurrentHealth = MaxHealth;

		_sprite = GetNodeOrNull<AnimatedSprite2D>(SpritePath);

		if (_sprite == null)
		{
			GD.PushError("PlayerController: no AnimatedSprite2D found at " + SpritePath);
		}
		else
		{
			_sprite.AnimationFinished += OnAnimationFinished;
		}

		if (!FeetSpritePath.IsEmpty)
		{
			_feetSprite = GetNodeOrNull<AnimatedSprite2D>(FeetSpritePath);
			if (_feetSprite == null)
				GD.PushWarning("PlayerController: FeetSpritePath is set but no AnimatedSprite2D found at " + FeetSpritePath);
		}

		_collisionShape = GetNodeOrNull<CollisionShape2D>(CollisionShapePath);
		if (_collisionShape == null)
			GD.PushWarning("PlayerController: no CollisionShape2D found at " + CollisionShapePath + " - hazard overlap detection will be disabled.");

		ChangeState(State.Idle);
	}

	public void ApplyKnockback(Vector2 impulse, float duration)
	{
		Velocity = impulse;
		_knockbackTimer = Mathf.Max(duration, 0f);
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		if (_knockbackTimer > 0f)
		{
			_knockbackTimer -= dt;
			bool floorNow = IsOnFloor();
			ApplyGravity(dt, false);
			MoveAndSlide();
			CheckHazardOverlap();
			_wasOnFloor = floorNow;
			return;
		}

		bool onFloor = IsOnFloor();
		bool onWall = IsOnWall();
		bool justLanded = onFloor && !_wasOnFloor;

		float rawAxis = Input.GetAxis("move_left", "move_right");
		float magnitude = Mathf.Abs(rawAxis);
		bool hasInput = magnitude > InputDeadzone;
		bool reversed = hasInput && (int)Mathf.Sign(rawAxis) != _facing;

		bool jumpPressed = Input.IsActionJustPressed("jump");
		bool walkModifierHeld = Input.IsActionPressed("walk_modifier");

		bool wallSlideActive = !onFloor && onWall && Velocity.Y >= 0f
			&& (!RequireInputTowardWallToHang || PressingTowardWall(rawAxis))
			&& !walkModifierHeld;

		ApplyGravity(dt, wallSlideActive);
		UpdateHealth(dt);

		if (wallSlideActive)
		{
			HandleWallHang(jumpPressed);
		}
		else if (!onFloor)
		{
			HandleAirborne(rawAxis, hasInput, reversed, jumpPressed, dt);
		}
		else if (justLanded)
		{
			ChangeState(State.JumpEnd);
			Velocity = new Vector2(Mathf.MoveToward(Velocity.X, 0f, GroundAcceleration * dt), Velocity.Y);
		}
		else if (_state == State.JumpEnd)
		{
			Velocity = new Vector2(Mathf.MoveToward(Velocity.X, 0f, GroundAcceleration * dt), Velocity.Y);
		}
		else if (jumpPressed)
		{
			Velocity = new Vector2(Velocity.X, JumpVelocity);
			ChangeState(State.JumpStart);
		}
		else
		{
			HandleLocomotion(rawAxis, hasInput, dt);
		}

		UpdateFacing(rawAxis, hasInput);
		MoveAndSlide();
		CheckHazardOverlap();
		_wasOnFloor = onFloor;
	}

	private void HandleLocomotion(float rawAxis, bool hasInput, float dt)
	{
		bool velocityOpposesInput = hasInput && Mathf.Abs(Velocity.X) > 1f
			&& Mathf.Sign(rawAxis) != Mathf.Sign(Velocity.X);

		if (_state != State.RunTurn && !_lastTierWasWalk && velocityOpposesInput)
		{
			ChangeState(State.RunTurn);
			Velocity = new Vector2(0f, Velocity.Y);
			return;
		}

		if (_state == State.RunTurn)
		{
			Velocity = new Vector2(0f, Velocity.Y);
			return;
		}

		if (!hasInput)
		{
			_hasMoveTierBaseline = false;
			ChangeState(State.Idle);
			Velocity = new Vector2(Mathf.MoveToward(Velocity.X, 0f, GroundAcceleration * dt), Velocity.Y);
			return;
		}

		bool walkHeld = Input.IsActionPressed("walk_modifier");

		if (_state == State.Jog)
		{
			float coastSpeed = walkHeld ? WalkSpeed : RunSpeed;
			float coastTarget = coastSpeed * Mathf.Sign(rawAxis);
			Velocity = new Vector2(Mathf.MoveToward(Velocity.X, coastTarget, GroundAcceleration * dt), Velocity.Y);
			return;
		}

		if (!_hasMoveTierBaseline)
		{
			_hasMoveTierBaseline = true;
			_lastTierWasWalk = walkHeld;
			ChangeState(walkHeld ? State.Walk : State.Run);
		}
		else if (walkHeld != _lastTierWasWalk)
		{
			_lastTierWasWalk = walkHeld;
			ChangeState(State.Jog);
		}
		else
		{
			ChangeState(walkHeld ? State.Walk : State.Run);
		}

		float speed = walkHeld ? WalkSpeed : RunSpeed;
		float target = speed * Mathf.Sign(rawAxis);
		Velocity = new Vector2(Mathf.MoveToward(Velocity.X, target, GroundAcceleration * dt), Velocity.Y);
	}

	private void HandleAirborne(float rawAxis, bool hasInput, bool reversed, bool jumpPressed, float dt)
	{
		if (jumpPressed && _airJumpAvailable && _state != State.JumpTurn)
		{
			_airJumpAvailable = false;
			Velocity = new Vector2(Velocity.X, DoubleJumpVelocity);
			ChangeState(State.JumpStart);
			return;
		}

		float targetSpeed = hasInput ? RunSpeed * Mathf.Sign(rawAxis) : 0f;
		Velocity = new Vector2(Mathf.MoveToward(Velocity.X, targetSpeed, AirAcceleration * dt), Velocity.Y);

		if (_state == State.JumpStart || _state == State.JumpTurn || _state == State.JumpEnd)
			return;

		if (_state == State.JumpLoop && hasInput && reversed)
		{
			ChangeState(State.JumpTurn);
			return;
		}

		if (_state != State.JumpLoop)
			ChangeState(State.JumpLoop);
	}

	private void HandleWallHang(bool jumpPressed)
	{
		ChangeState(State.WallHang);
		Velocity = new Vector2(0f, Mathf.Min(Velocity.Y, MaxFallSpeed * WallSlideGravityScale));

		float wallNormalX = GetWallNormal().X;
		SetFacing(wallNormalX > 0 ? -1 : 1);

		if (jumpPressed)
		{
			float pushDir = GetWallNormal().X;
			Velocity = new Vector2(pushDir * WallJumpPushVelocity, WallJumpVerticalVelocity);
			SetFacing(_preHangFacing);
			ChangeState(State.JumpStart);
		}
	}

	private State ComputeLocomotionTier(out float speed)
	{
		float rawAxis = Input.GetAxis("move_left", "move_right");
		bool hasInput = Mathf.Abs(rawAxis) > InputDeadzone;

		if (!hasInput)
		{
			speed = 0f;
			return State.Idle;
		}

		bool walkHeld = Input.IsActionPressed("walk_modifier");
		speed = walkHeld ? WalkSpeed : RunSpeed;
		return walkHeld ? State.Walk : State.Run;
	}

	private void ApplyGravity(float dt, bool wallSliding)
	{
		float g = Gravity * (wallSliding ? WallSlideGravityScale : 1f);
		Vector2 v = Velocity;
		v.Y = Mathf.Min(v.Y + g * dt, MaxFallSpeed);
		Velocity = v;
	}

	private void UpdateHealth(float dt)
	{
		if (_invulnTimer > 0f)
		{
			_invulnTimer -= dt;
			if (_invulnTimer <= 0f)
			{
				_isInvulnerable = false;
				SetSpritesVisible(true);
			}
			else
			{
				_flickerTimer -= dt;
				if (_flickerTimer <= 0f)
				{
					_flickerTimer = HurtFlickerInterval;
					SetSpritesVisible(!(_sprite?.Visible ?? true));
				}
			}
		}

		if (_regenDelayTimer > 0f)
		{
			_regenDelayTimer -= dt;
		}
		else if (CurrentHealth < MaxHealth)
		{
			CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + RegenRate * dt);
			EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
		}
	}

	private void SetSpritesVisible(bool visible)
	{
		if (_sprite != null) _sprite.Visible = visible;
		if (_feetSprite != null) _feetSprite.Visible = visible;
	}

	public void TakeDamage(float amount)
	{
		if (_isDead || _isInvulnerable || amount <= 0f) return;

		CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
		_regenDelayTimer = RegenDelay;
		_isInvulnerable = true;
		_invulnTimer = InvulnerabilityDuration;
		_flickerTimer = HurtFlickerInterval;

		EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);

		if (CurrentHealth <= 0f)
			Die();
	}

	public void KillInstantly()
	{
		if (_isDead) return;

		CurrentHealth = 0f;
		EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
		Die();
	}

	private void Die()
	{
		if (_isDead) return;
		_isDead = true;

		SetSpritesVisible(true);
		SetPhysicsProcess(false);
		EmitSignal(SignalName.Died);

		CallDeferred(nameof(GoToDeathScene));
	}

	private void GoToDeathScene()
	{
		if (DeathScene != null)
			GetTree().ChangeSceneToPacked(DeathScene);
		else
			GetTree().ReloadCurrentScene();
	}

	private void CheckHazardOverlap()
	{
		if (_isDead || _collisionShape == null || _collisionShape.Shape == null) return;

		uint combinedMask = HazardLayerMask | HazardHeavyLayerMask | HazardInstantLayerMask;
		if (combinedMask == 0) return;

		var query = new PhysicsShapeQueryParameters2D
		{
			Shape = _collisionShape.Shape,
			Transform = _collisionShape.GlobalTransform,
			CollisionMask = combinedMask,
			CollideWithBodies = true,
			CollideWithAreas = false,
			Exclude = new Godot.Collections.Array<Rid> { GetRid() }
		};

		var results = GetWorld2D().DirectSpaceState.IntersectShape(query);
		foreach (Godot.Collections.Dictionary result in results)
		{
			Rid bodyRid = result["rid"].AsRid();
			uint colliderLayer = PhysicsServer2D.BodyGetCollisionLayer(bodyRid);

			if ((colliderLayer & HazardInstantLayerMask) != 0)
			{
				KillInstantly();
				return;
			}

			if ((colliderLayer & HazardHeavyLayerMask) != 0)
			{
				TakeDamage(HazardDamage);
				ApplyHazardKnockback();
				continue;
			}

			if ((colliderLayer & HazardLayerMask) != 0)
			{
				TakeDamage(HazardDamage);
			}
		}
	}

	private void ApplyHazardKnockback()
	{
		if (HazardKnockbackForce == Vector2.Zero) return;

		float pushX = -_facing * Mathf.Abs(HazardKnockbackForce.X);
		ApplyKnockback(new Vector2(pushX, HazardKnockbackForce.Y), HazardKnockbackDuration);
	}

	public void Heal(float amount)
	{
		if (amount <= 0f) return;
		CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
		EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
	}

	public void RefreshAirJump()
	{
		_airJumpAvailable = true;
	}

	private void UpdateFacing(float rawAxis, bool hasInput)
	{
		if (_state == State.WallHang) return;

		if (!hasInput) return;
		SetFacing(rawAxis > 0 ? 1 : -1);
	}

	private void SetFacing(int newFacing)
	{
		if (newFacing == _facing) return;
		_facing = newFacing;

		bool flip = _facing < 0;
		if (_sprite != null) _sprite.FlipH = flip;
		if (_feetSprite != null) _feetSprite.FlipH = flip;
	}

	private bool PressingTowardWall(float rawAxis)
	{
		if (Mathf.Abs(rawAxis) <= InputDeadzone) return false;
		float wallNormalX = GetWallNormal().X;
		return Mathf.Sign(rawAxis) == -Mathf.Sign(wallNormalX);
	}

	private void ForceReevaluateLocomotion()
	{
		State desired = ComputeLocomotionTier(out _);

		if (desired == State.Idle)
		{
			_hasMoveTierBaseline = false;
		}
		else
		{
			_hasMoveTierBaseline = true;
			_lastTierWasWalk = desired == State.Walk;
		}

		ChangeState(desired);
	}

	private void ChangeState(State newState)
	{
		if (_state == newState) return;

		if (newState == State.WallHang)
			_preHangFacing = _facing;

		_state = newState;

		switch (newState)
		{
			case State.Idle: PlayAnim(AnimIdle); break;
			case State.Walk: PlayAnim(AnimWalk); break;
			case State.Jog: PlayAnim(AnimJog); break;
			case State.Run: PlayAnim(AnimRun); break;
			case State.RunTurn: PlayAnim(AnimRunTurn); break;
			case State.JumpStart: PlayAnim(AnimJumpStart); break;
			case State.JumpLoop: PlayAnim(AnimJump); break;
			case State.JumpTurn: PlayAnim(AnimJumpTurn); break;
			case State.JumpEnd: PlayAnim(AnimJumpEnd); break;
			case State.WallHang: PlayAnim(AnimHang); break;
		}

		EmitSignal(SignalName.StateChanged, newState.ToString());
	}

	private void PlayAnim(string clipName)
	{
		PlayOnSprite(_sprite, clipName);
		PlayOnSprite(_feetSprite, clipName);
	}

	private void PlayOnSprite(AnimatedSprite2D sprite, string clipName)
	{
		if (sprite == null || sprite.SpriteFrames == null) return;

		if (sprite.SpriteFrames.HasAnimation(clipName))
			sprite.Play(clipName);
		else
			GD.PushWarning($"PlayerController: animation clip '{clipName}' not found in '{sprite.Name}' SpriteFrames.");
	}

	private void OnAnimationFinished()
	{
		switch (_state)
		{
			case State.JumpStart:
				ChangeState(State.JumpLoop);
				break;

			case State.JumpTurn:
				ChangeState(State.JumpLoop);
				break;

			case State.JumpEnd:
				ForceReevaluateLocomotion();
				break;

			case State.RunTurn:
				ForceReevaluateLocomotion();
				break;

			case State.Jog:
				ForceReevaluateLocomotion();
				break;
		}
	}
}

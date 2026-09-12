/// Generated initially using Claude

using Godot;

public partial class PlayerController : CharacterBody2D
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

	[ExportGroup("Node References")]
	[Export] public NodePath SpritePath = "AnimatedSprite2D";r.
	[Export] public NodePath FeetSpritePath = "AnimatedSprite2D2";

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

	private enum State
	{
		Idle, Walk, Jog, Run, RunTurn,
		JumpStart, JumpLoop, JumpTurn, JumpEnd,
		WallHang
	}

	private AnimatedSprite2D _sprite;
	private AnimatedSprite2D _feetSprite;

	private State _state = State.Idle;
	private int _facing = 1;
	private int _preHangFacing = 1;
	private bool _wasOnFloor = true;

	private bool _hasMoveTierBaseline = false;
	private bool _lastTierWasWalk = false;

	public override void _Ready()
	{
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

		ChangeState(State.Idle);
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

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

		if (wallSlideActive)
		{
			HandleWallHang(jumpPressed);
		}
		else if (!onFloor)
		{
			HandleAirborne(rawAxis, hasInput, reversed, dt);
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

	private void HandleAirborne(float rawAxis, bool hasInput, bool reversed, float dt)
	{
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

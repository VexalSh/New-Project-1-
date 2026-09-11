using Godot;
using System.Collections.Generic;

public partial class PlayerCamera2D : Camera2D
{
	[ExportGroup("Follow")]
	[Export] public bool SmoothFollow = true;
	[Export] public float FollowSpeed = 8f; // maps to PositionSmoothingSpeed

	[ExportGroup("Bounds Transition")]
	[Export] public float LimitTransitionDuration = 0.4f;

	private readonly List<CameraRoom2D> _rooms = new();
	private Tween _limitTween;
	private bool _hasBounds = false;

	// Current animated limit values (floats so they can be tweened smoothly).
	private float _curLeft, _curTop, _curRight, _curBottom;

	public override void _Ready()
	{
		PositionSmoothingEnabled = SmoothFollow;
		PositionSmoothingSpeed = FollowSpeed;

		foreach (Node node in GetTree().GetNodesInGroup("camera_rooms"))
		{
			if (node is CameraRoom2D room)
				_rooms.Add(room);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector2 targetPos = GetParent() is Node2D parent ? parent.GlobalPosition : GlobalPosition;

		Rect2? containing = null;
		foreach (CameraRoom2D room in _rooms)
		{
			if (!room.WorldRect.HasPoint(targetPos)) continue;

			// If overlapping two rooms at once (a doorway), intersect them so the
			// camera respects both while straddling the seam.
			containing = containing.HasValue ? containing.Value.Intersection(room.WorldRect) : room.WorldRect;
		}

		if (containing.HasValue)
			AnimateLimitsTo(containing.Value);
		// else: target isn't inside any room right now - keep the last bounds as-is.
	}

	private void AnimateLimitsTo(Rect2 target)
	{
		if (!_hasBounds)
		{
			// First room found: snap immediately, nothing to tween from yet.
			_hasBounds = true;
			_curLeft = target.Position.X;
			_curTop = target.Position.Y;
			_curRight = target.End.X;
			_curBottom = target.End.Y;
			ApplyCurrentLimits();
			return;
		}

		bool unchanged = Mathf.IsEqualApprox(_curLeft, target.Position.X)
			&& Mathf.IsEqualApprox(_curTop, target.Position.Y)
			&& Mathf.IsEqualApprox(_curRight, target.End.X)
			&& Mathf.IsEqualApprox(_curBottom, target.End.Y);
		if (unchanged) return;

		_limitTween?.Kill();

		if (LimitTransitionDuration <= 0f)
		{
			_curLeft = target.Position.X;
			_curTop = target.Position.Y;
			_curRight = target.End.X;
			_curBottom = target.End.Y;
			ApplyCurrentLimits();
			return;
		}

		_limitTween = CreateTween();
		_limitTween.SetParallel(true);
		_limitTween.TweenMethod(Callable.From<float>(v => { _curLeft = v; ApplyCurrentLimits(); }),
			_curLeft, target.Position.X, LimitTransitionDuration);
		_limitTween.TweenMethod(Callable.From<float>(v => { _curTop = v; ApplyCurrentLimits(); }),
			_curTop, target.Position.Y, LimitTransitionDuration);
		_limitTween.TweenMethod(Callable.From<float>(v => { _curRight = v; ApplyCurrentLimits(); }),
			_curRight, target.End.X, LimitTransitionDuration);
		_limitTween.TweenMethod(Callable.From<float>(v => { _curBottom = v; ApplyCurrentLimits(); }),
			_curBottom, target.End.Y, LimitTransitionDuration);
	}

	private void ApplyCurrentLimits()
	{
		LimitLeft = Mathf.RoundToInt(_curLeft);
		LimitTop = Mathf.RoundToInt(_curTop);
		LimitRight = Mathf.RoundToInt(_curRight);
		LimitBottom = Mathf.RoundToInt(_curBottom);
	}
}

using Godot;
using System.Collections.Generic;

/// <summary>
/// Attach to a Camera2D placed as a child of the CharacterBody2D you want it
/// to follow.
///
/// This camera does NOT use Camera2D's built-in Limit*/PositionSmoothing
/// properties - it sets TopLevel = true and computes its own clamped,
/// smoothed GlobalPosition every physics frame instead. That gives a
/// guarantee that's easy to verify by reading the math below: the camera's
/// half-view-size (in world units, at whatever zoom is CURRENTLY applied
/// this frame) is subtracted from the room rectangle before clamping, so
/// the rendered view edge can never cross the room boundary, regardless of
/// how zoom or the target position are changing that same frame.
///
/// Every physics frame:
///   1. Finds which CameraRoom2D (if any) the player is physically
///      overlapping (GetOverlappingBodies - a real physics query).
///   2. If that room is smaller than what's visible at the baseline zoom
///      (whatever Zoom was set to in the editor), smoothly zooms in just
///      enough to fit it - never zooms out past the baseline.
///   3. Clamps the camera's target position so the current view, at the
///      zoom level actually in effect this frame, stays fully inside the
///      room. No room overlapping -> no clamp, normal free follow.
///   4. Smoothly eases the camera's actual position toward that target.
/// </summary>
public partial class PlayerCamera2D : Camera2D
{
	[ExportGroup("Follow")]
	[Export] public bool SmoothFollow = true;
	[Export] public float FollowSpeed = 8f; // higher = snappier

	[ExportGroup("Room Fitting")]
	[Export] public bool AutoZoomToFitRoom = true;
	[Export] public float ZoomSmoothSpeed = 6f;

	private readonly List<CameraRoom2D> _rooms = new();
	private Node2D _player;

	private Vector2 _currentPos;
	private float _baselineZoom;
	private float _curZoom;

	public override void _Ready()
	{
		// We fully own our own global position/zoom from here on, instead of
		// inheriting the parent's transform each frame or using Limit*.
		TopLevel = true;
		PositionSmoothingEnabled = false;

		_player = GetParent() as Node2D;
		_currentPos = _player != null ? _player.GlobalPosition : GlobalPosition;
		GlobalPosition = _currentPos;

		foreach (Node node in GetTree().GetNodesInGroup("camera_rooms"))
		{
			if (node is CameraRoom2D room)
				_rooms.Add(room);
		}

		// Whatever zoom was set on this node in the editor becomes the
		// baseline/"most zoomed out" level. Assumes a uniform Zoom (X == Y),
		// which covers the typical non-stretched 2D camera setup.
		_baselineZoom = Zoom.X;
		_curZoom = _baselineZoom;
		Zoom = new Vector2(_curZoom, _curZoom);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_player == null) return;
		float dt = (float)delta;

		CameraRoom2D currentRoom = null;
		foreach (CameraRoom2D room in _rooms)
		{
			if (room.ContainsBody(_player))
			{
				currentRoom = room;
				break; // first overlapping room wins if two rooms briefly overlap
			}
		}

		// --- Zoom ---
		float targetZoom = _baselineZoom;
		if (AutoZoomToFitRoom && currentRoom != null)
		{
			Vector2 roomSize = currentRoom.WorldRect.Size;
			if (roomSize.X > 0f && roomSize.Y > 0f)
			{
				Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
				float neededX = viewportSize.X / roomSize.X;
				float neededY = viewportSize.Y / roomSize.Y;
				float needed = Mathf.Max(neededX, neededY);

				// Only ever zoom IN further than the baseline, never out past it.
				targetZoom = Mathf.Max(_baselineZoom, needed);
			}
		}

		_curZoom = Mathf.Lerp(_curZoom, targetZoom, ExpSmoothFactor(ZoomSmoothSpeed, dt));
		Zoom = new Vector2(_curZoom, _curZoom);

		// --- Position ---
		// Half the visible world area at the zoom actually in effect THIS
		// frame - using the live, possibly-still-transitioning zoom value
		// (not the target) guarantees the clamp always matches what's really
		// being rendered, so there's no window where they're out of sync.
		Vector2 halfView = GetViewport().GetVisibleRect().Size / (2f * Mathf.Max(_curZoom, 0.0001f));

		Vector2 desired = _player.GlobalPosition;

		if (currentRoom != null)
		{
			Rect2 r = currentRoom.WorldRect;

			float minX = r.Position.X + halfView.X;
			float maxX = r.End.X - halfView.X;
			desired.X = minX <= maxX ? Mathf.Clamp(desired.X, minX, maxX) : r.Position.X + r.Size.X * 0.5f;

			float minY = r.Position.Y + halfView.Y;
			float maxY = r.End.Y - halfView.Y;
			desired.Y = minY <= maxY ? Mathf.Clamp(desired.Y, minY, maxY) : r.Position.Y + r.Size.Y * 0.5f;
		}

		_currentPos = SmoothFollow
			? _currentPos.Lerp(desired, ExpSmoothFactor(FollowSpeed, dt))
			: desired;

		GlobalPosition = _currentPos;
	}

	/// <summary>Frame-rate-independent exponential smoothing factor for Lerp.</summary>
	private static float ExpSmoothFactor(float speed, float dt) => 1f - Mathf.Exp(-speed * dt);
}
